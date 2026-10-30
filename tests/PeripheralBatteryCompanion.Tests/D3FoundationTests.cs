using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PeripheralBatteryCompanion.Core.ConnectionFoundations;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

// All observations, clocks, stores and payloads are synthetic. No native TLS/crypto/transport/storage.
public sealed class D3FoundationTests
{
    private static ClockSample Now(long utc = 10, long tick = 10, bool certain = true) => new(utc,tick,certain);
    private const string Pin = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Anchor = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static EndpointExpectation Endpoint => new("192.168.1.2",41000,SanKind.Ip,Anchor,Pin);
    private static SyntheticTrust Trust => new("192.168.1.2",41000,SanKind.Ip,"192.168.1.2",Anchor,Pin,0,100,true,true);
    private sealed class Store : IEnrollmentMetadataStore, IRevocationMetadataStore
    {
        public int Writes; public bool Fail, ThrowAfterEffect; public Action? DuringWrite;
        public bool CommitSyntheticEnrollment(string flow) { Writes++; DuringWrite?.Invoke(); if (ThrowAfterEffect) throw new IOException(); return !Fail; }
        public bool CommitSyntheticRevocation() { Writes++; DuringWrite?.Invoke(); if (ThrowAfterEffect) throw new IOException(); return !Fail; }
    }
    [Fact] public void ConsentIsOffOnConstructionAndRestartAndFailureLatches()
    {
        var c = new ConsentGate(); Assert.False(c.Evaluate(true));
        c.LocalDecision(false,true); Assert.False(c.Evaluate(true));
        c.LocalDecision(true,false); Assert.False(c.Evaluate(true));
        c.LocalDecision(true,true); Assert.True(c.Evaluate(true));
        Assert.False(c.Evaluate(false)); Assert.False(c.Evaluate(true));
        c.LocalDecision(true,true); c.Restart(); Assert.False(c.Evaluate(true));
        c.Shutdown(); c.LocalDecision(true,true); c.Restart(); Assert.False(c.Evaluate(true));
    }
    [Fact] public void AdmissionIsBoundedAndLeaseCannotBeReleasedTwice()
    {
        var b = new AdmissionBudget(new(1,10,2,5));
        Assert.Null(b.Enter(false,1,0,0)); Assert.Null(b.Enter(true,11,0,0));
        Assert.Null(b.Enter(true,1,3,0)); Assert.Null(b.Enter(true,1,0,5)); Assert.Null(b.Enter(true,1,0,-1));
        var lease = b.Enter(true,10,2,4); Assert.NotNull(lease); Assert.Null(b.Enter(true,1,0,0));
        Assert.True(b.Leave(lease!.Value)); Assert.False(b.Leave(lease.Value)); Assert.NotNull(b.Enter(true,1,0,0));
        Assert.Null(new AdmissionBudget(new(0,10,2,5)).Enter(true,1,0,0));
    }
    [Fact] public void AdmissionRacesHaveOnlyOneWinner()
    {
        var b = new AdmissionBudget(new(1,10,2,5));
        var results = new System.Collections.Concurrent.ConcurrentBag<long?>();
        Parallel.For(0,32,_ => results.Add(b.Enter(true,1,0,0)));
        Assert.Single(results, x => x is not null);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    public void TrustRequiresEveryIndependentMetadataCheck(int fault)
    {
        var seen = fault switch {
            0 => Trust with { SpkiSha256 = Anchor }, 1 => Trust with { AnchorSha256 = Pin },
            2 => Trust with { San = "192.168.1.3" }, 3 => Trust with { Kind = SanKind.Dns },
            4 => Trust with { PathValid = false }, 5 => Trust with { ServerEku = false },
            6 => Trust with { NotAfter = 10 }, 7 => Trust with { NotBefore = 11 },
            8 => Trust with { Port = 41001 }, _ => Trust with { Host = "rogue" }
        };
        Assert.False(TrustMetadataPolicy.Decide(Endpoint,seen,Now()));
    }
    [Fact] public void TrustMetadataSuccessDoesNotProveNativeEnforcement()
    {
        Assert.True(TrustMetadataPolicy.Decide(Endpoint,Trust,Now()));
        Assert.False(TrustMetadataPolicy.Decide(Endpoint,Trust,Now(certain:false)));
        foreach (var host in new[] { "0.0.0.0","127.0.0.1","8.8.8.8","host.local","192.168.001.2","fe80::1","::ffff:192.168.1.2" })
            Assert.False(TrustMetadataPolicy.Decide(Endpoint with { Host=host },Trust with { Host=host,San=host },Now()));
        Assert.True(TrustMetadataPolicy.Decide(Endpoint with {Host="fd00::1"},Trust with {Host="fd00::1",San="fd00::1"},Now()));
    }
    [Fact] public void PairingDeniesConsentMismatchAndExpiryWithoutStorageWrites()
    {
        var s = new Store();
        Assert.False(new PairingLifecycle(false,Now(),20,20).Claim("flow",true,Now()));
        var p = new PairingLifecycle(true,Now(),20,20);
        Assert.False(p.Claim("flow",false,Now())); Assert.False(p.Claim("flow",true,Now()));
        p = new(true,Now(),20,20); Assert.False(p.Claim("flow",true,Now(20,11)));
        p = new(true,Now(),20,20); Assert.True(p.Claim("flow",true,Now()));
        Assert.False(p.Confirm("flow",false,Now(),s)); Assert.Equal(0,s.Writes);
    }
    [Fact] public void SingleUseClaimRaceAndConfirmationPermitOneSyntheticWrite()
    {
        var p = new PairingLifecycle(true,Now(),20,20); var winners = 0;
        Parallel.For(0,32,_ => { if(p.Claim("flow",true,Now())) Interlocked.Increment(ref winners); });
        Assert.Equal(1,winners); var s = new Store();
        Parallel.For(0,32,_ => p.Confirm("flow",true,Now(),s));
        Assert.Equal(1,s.Writes); Assert.Equal(PairState.Consumed,p.State);
        Assert.False(p.Claim("new",true,Now()));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void FailedWriteOrErrorAfterSideEffectNeverReissues(bool afterEffect)
    {
        var p = new PairingLifecycle(true,Now(),20,20); var s = new Store {Fail=true,ThrowAfterEffect=afterEffect};
        Assert.True(p.Claim("flow",true,Now())); Assert.False(p.Confirm("flow",true,Now(),s));
        Assert.Equal(PairState.Uncertain,p.State); Assert.False(p.Confirm("flow",true,Now(),s));
        Assert.Equal(1,s.Writes);
    }
    [Fact] public void LostResponseCrashAndClockRollbackDoNotRecoverPairing()
    {
        var p = new PairingLifecycle(true,Now(),20,20); var s = new Store();
        Assert.True(p.Claim("flow",true,Now())); Assert.True(p.Confirm("flow",true,Now(),s));
        p.CrashOrLostResponse(); Assert.False(p.Confirm("flow",true,Now(),s)); Assert.Equal(1,s.Writes);
        p = new(true,Now(),20,20); Assert.False(p.Claim("flow",true,Now(9,11))); Assert.Equal(PairState.Closed,p.State);
        p = new(true,Now(),20,20); p.CrashOrLostResponse(); Assert.False(p.Claim("flow",true,Now()));
    }
    [Fact] public void CredentialScopeExpiryRevocationAndClockUncertaintyFailClosed()
    {
        var c = new CredentialPolicy(0,20,20,"snapshot.read");
        Assert.True(c.Allows(true,"snapshot.read",Now())); Assert.False(c.Allows(false,"snapshot.read",Now()));
        Assert.False(c.Allows(true,"admin",Now())); Assert.False(c.Allows(true,"snapshot.read",Now(20,11)));
        c = new(0,20,20,"snapshot.read"); Assert.False(c.Allows(true,"snapshot.read",Now(certain:false)));
        Assert.False(c.Allows(true,"snapshot.read",Now()));
        c = new(0,20,20,"snapshot.read"); Assert.True(c.Allows(true,"snapshot.read",Now()));
        Assert.False(c.Allows(true,"snapshot.read",Now(9,11))); Assert.False(c.Allows(true,"snapshot.read",Now(11,12)));
        c = new(0,20,20,"snapshot.read"); Assert.False(c.Revoke(new Store{ThrowAfterEffect=true}));
        Assert.False(c.Allows(true,"snapshot.read",Now()));
    }
    [Fact] public void PublicationNeverPromotesDemoDuplicateOrOldSessionToFresh()
    {
        var p = new PublicationTracker(2);
        Assert.Equal(PublicationDecision.Rejected,p.Observe("a",1,true,true,true));
        Assert.Equal(PublicationDecision.Rejected,p.Observe("a",1,false,false,true));
        Assert.Equal(PublicationDecision.Rejected,p.Observe("a",1,true,false,false));
        Assert.Equal(PublicationDecision.NewSessionUnknownFreshness,p.Observe("a",1,true,false,true));
        Assert.Equal(PublicationDecision.Duplicate,p.Observe("a",1,true,false,true));
        Assert.Equal(PublicationDecision.Rejected,p.Observe("a",0,true,false,true));
        Assert.Equal(PublicationDecision.AdvancedUnknownFreshness,p.Observe("a",2,true,false,true));
        Assert.Equal(PublicationDecision.Offline,p.Disconnect());
        Assert.Equal(PublicationDecision.Rejected,p.Observe("a",1,true,false,true));
        Assert.Equal(PublicationDecision.NewSessionUnknownFreshness,p.Observe("b",0,true,false,true));
        Assert.Equal(PublicationDecision.Rejected,p.Observe("a",3,true,false,true));
    }
    [Fact] public void FrozenSchemaAndEveryVectorMatchCheckedInDigestsAndExpectedPolicy()
    {
        var folder = Path.Combine(AppContext.BaseDirectory,"Contracts");
        Assert.Equal("2d9ac5bf5e5263340bcd15736df7a9f6f6c201f5f4ad30b1fb7e858ec694bdc2",Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder,"battery-snapshot-v1.schema.json")))));
        foreach (var line in File.ReadAllLines(Path.Combine(folder,"SHA256SUMS"))) {
            var pair = line.Split("  "); var bytes = File.ReadAllBytes(Path.Combine(folder,pair[1]));
            Assert.Equal(pair[0],Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        using var cases = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"scenarios.json")));
        foreach (var c in cases.RootElement.EnumerateArray()) {
            var bytes = File.ReadAllBytes(Path.Combine(folder,c.GetProperty("file").GetString()!));
            if (c.GetProperty("accept").GetBoolean()) Assert.True(SnapshotWirePolicy.Read(bytes).GetProperty("synthetic").GetBoolean());
            else Assert.ThrowsAny<Exception>(() => SnapshotWirePolicy.Read(bytes));
        }
    }
    [Fact] public void ZeroNullAndStaleRetainedProvenanceAreDistinct()
    {
        var folder = Path.Combine(AppContext.BaseDirectory,"Contracts","golden");
        var zero = SnapshotWirePolicy.Read(File.ReadAllBytes(Path.Combine(folder,"zero-and-unknown.json")));
        Assert.Equal(0,zero.GetProperty("devices")[0].GetProperty("current").GetProperty("batteryPercent").GetInt32());
        Assert.Equal(JsonValueKind.Null,zero.GetProperty("devices")[1].GetProperty("current").GetProperty("batteryPercent").ValueKind);
        var stale = SnapshotWirePolicy.Read(File.ReadAllBytes(Path.Combine(folder,"stale-last-known.json"))).GetProperty("devices")[0];
        Assert.Equal("STALE",stale.GetProperty("freshness").GetString()); Assert.Equal(99,stale.GetProperty("lastKnown").GetProperty("batteryPercent").GetInt32());
        Assert.Equal("GHUB_GATEWAY",stale.GetProperty("lastKnown").GetProperty("source").GetProperty("kind").GetString());
    }

    [Fact] public void CrashDuringSyntheticCommitDoesNotPromoteUncertainToConsumed()
    {
        var p = new PairingLifecycle(true,Now(),20,20);
        var s = new Store { DuringWrite = p.CrashOrLostResponse };
        Assert.True(p.Claim("flow",true,Now())); Assert.False(p.Confirm("flow",true,Now(),s));
        Assert.Equal(PairState.Uncertain,p.State); Assert.Equal(1,s.Writes);
    }
    [Fact] public void TickExpiryRollbackAndBoundedHistoryFailClosed()
    {
        Assert.False(new PairingLifecycle(true,Now(),20,20).Claim("flow",true,Now(11,20)));
        Assert.False(new PairingLifecycle(true,Now(),20,20).Claim("flow",true,Now(11,9)));
        var p = new PairingLifecycle(true,Now(),20,20);
        Assert.True(p.Claim("flow",true,Now())); Assert.False(p.Confirm("wrong",true,Now(),new Store()));
        var c = new CredentialPolicy(0,20,20,"snapshot.read");
        Assert.False(c.Allows(true,"snapshot.read",Now(11,20)));
        var t = new PublicationTracker(1);
        Assert.Equal(PublicationDecision.NewSessionUnknownFreshness,t.Observe("a",0,true,false,true));
        Assert.Equal(PublicationDecision.NewSessionUnknownFreshness,t.Observe("b",0,true,false,true));
        Assert.Equal(PublicationDecision.Rejected,t.Observe("c",0,true,false,true));
    }
    [Fact] public void WireRetainsDistinctReceiptObservationAndLastKnownWithoutDomainMapping()
    {
        var file = Path.Combine(AppContext.BaseDirectory,"Contracts","golden","stale-last-known.json");
        var text = File.ReadAllText(file).Replace("\"synthetic\": true","\"synthetic\": false")
            .Replace("\"readAt\": null","\"readAt\": \"2026-01-02T00:00:00Z\"");
        var parsed = SnapshotWirePolicy.Read(Encoding.UTF8.GetBytes(text));
        var retained = parsed.GetProperty("devices")[0].GetProperty("lastKnown");
        Assert.Equal("2026-01-02T00:00:00Z",retained.GetProperty("readAt").GetString());
        Assert.Equal(JsonValueKind.Null,retained.GetProperty("observedAt").ValueKind);
    }
    private sealed class Transport(byte[] payload) : ISyntheticSnapshotTransport { public byte[] ReadSyntheticPayload() => payload; }
    [Fact] public void FakeTransportRejectsOversizeAndMalformedWithoutAnyNetwork()
    {
        foreach(var payload in new[] {new byte[65537],Encoding.UTF8.GetBytes("{}"),Encoding.UTF8.GetBytes("[[[[[[[[[[[[[[[[[[]]]]]]]]]]]]]]]]]]")}) {
            ISyntheticSnapshotTransport t = new Transport(payload);
            Assert.ThrowsAny<Exception>(() => SnapshotWirePolicy.Read(t.ReadSyntheticPayload()));
        }
    }
}
