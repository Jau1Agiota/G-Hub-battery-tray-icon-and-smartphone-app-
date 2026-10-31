using System.Text.Json;
using System.Security.Cryptography;
using PeripheralBatteryCompanion.Core.ConnectionFoundations;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class D33ATransportTests
{
    private static readonly EpochSample Start = new("boot-a",new(10,10,true));
    private static readonly TransportPolicyData Limits = new(2,20,10,10,5,5,10,5,true);
    private static string Folder => Path.Combine(AppContext.BaseDirectory,"Contracts","d3-3a");
    private static JsonDocument Fixture(string name) => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder,name+".json")));
    private static string S(JsonElement x,string p) => x.GetProperty(p).GetString()!;
    private static long L(JsonElement x,string p) => x.GetProperty(p).GetInt64();
    private static bool B(JsonElement x,string p) => x.GetProperty(p).GetBoolean();
    private static SanKind Kind(string s) => s == "IP" ? SanKind.Ip : SanKind.Dns;

    [Fact] public void IdentityVectorsHaveEquivalentSyntheticDecisionsWithoutNativeProof()
    {
        using var fixture = Fixture("identity");
        foreach (var c in fixture.RootElement.EnumerateArray()) {
            var e = c.GetProperty("expected"); var t = c.GetProperty("seen");
            var expected = new EndpointExpectation(S(e,"host"),(int)L(e,"port"),Kind(S(e,"kind")),S(e,"anchor"),S(e,"pin"));
            var seen = new SyntheticTrust(S(t,"host"),(int)L(t,"port"),Kind(S(t,"kind")),S(t,"san"),S(t,"anchor"),S(t,"pin"),
                L(t,"notBefore"),L(t,"notAfter"),B(t,"path"),B(t,"eku"));
            var session = new SyntheticTlsSession(expected,Start);
            var chain = t.GetProperty("chain").EnumerateArray().Select(x => x.GetString()!).ToArray();
            var times = c.GetProperty("times"); var revoked = c.GetProperty("revoked"); var accepts = c.GetProperty("accept");
            for(int i=0;i<times.GetArrayLength();i++) {
                var n=times[i]; var now=new EpochSample(S(n,"boot"),new(L(n,"utc"),L(n,"tick"),B(n,"certain")));
                Assert.Equal(accepts[i].GetBoolean(),session.Evaluate(seen,now,revoked[i].GetBoolean(),chain));
            }
        }
    }
    private sealed class Store : IAtomicSyntheticLedger
    {
        public int Mode, Writes; public long Version; public Action? DuringWrite;
        public SyntheticCredentialEntry? Persisted;
        public CommitAcknowledgment CommitAndFlush(long expectedVersion,SyntheticCredentialEntry replacement)
        {
            Writes++; DuringWrite?.Invoke();
            if(expectedVersion != Version) return new(CommitDisposition.NotCommitted,Version);
            if(Mode==1) return new(CommitDisposition.NotCommitted,Version); // before commit
            if(Mode==6) { Persisted=null; return new(CommitDisposition.Uncertain,Version); } // partial/corrupt
            Persisted=replacement; Version++;
            if(Mode==2) throw new IOException("synthetic lost acknowledgment"); // after side effect
            return new(Mode==3 ? CommitDisposition.Uncertain : CommitDisposition.CommittedAndFlushed,
                Mode==4 ? expectedVersion : Mode==5 ? Version+1 : Version);
        }
    }
    [Fact] public void CredentialTraceVectorsCoverIssuanceRotationRevocationCrashAndRestore()
    {
        using var fixture = Fixture("credentials");
        foreach(var trace in fixture.RootElement.EnumerateArray()) {
            var store = new Store(); var ledger=new SyntheticCredentialLedger(store,Start,8,true);
            foreach(var step in trace.GetProperty("steps").EnumerateArray()) {
                store.Mode=(int)L(step,"mode");
                var result = S(step,"op") switch {
                    "issue" => ledger.IssueSynthetic("phone","old",20,20,Start),
                    "rotate" or "replay-rotate" => ledger.RotateSynthetic("phone","new",20,20,Start),
                    "read" or "old-read" => ledger.ReadSyntheticDecision("phone","old","snapshot.read",Start),
                    "read-new" => ledger.ReadSyntheticDecision("phone","new","snapshot.read",Start),
                    "wrong-scope" => ledger.ReadSyntheticDecision("phone","old","admin",Start),
                    "expired-read" => ledger.ReadSyntheticDecision("phone","old","snapshot.read",new("boot-a",new(20,11,true))),
                    "rollback-read" => ledger.ReadSyntheticDecision("phone","old","snapshot.read",new("boot-a",new(9,11,true))),
                    "reboot-read" => ledger.ReadSyntheticDecision("phone","old","snapshot.read",new("boot-b",new(10,10,true))),
                    "revoke" => ledger.RevokeSynthetic("phone",Start),
                    "crash" => Crash(ledger),
                    "restore" => Restore(),
                    _ => throw new InvalidOperationException()
                };
                bool Restore() { ledger=new(store,Start,8,false); return false; }
                Assert.Equal(B(step,"accept"),result);
                Assert.Equal((int)L(step,"state"),(int)ledger.State);
            }
        }
    }
    private static bool Crash(SyntheticCredentialLedger ledger) { ledger.CrashOrLostAcknowledgment(); return false; }
    [Fact] public void FreshnessParityIncludesMaximumRevisionWithoutIncrementOverflow()
    {
        using var f=Fixture("freshness"); var tracker=new PublicationTracker(8);
        foreach(var s in f.RootElement.EnumerateArray())
            Assert.Equal((int)L(s,"result"),(int)tracker.Observe(S(s,"session"),L(s,"revision"),B(s,"authenticated"),B(s,"synthetic"),B(s,"uncached")));
    }
    [Fact] public void AllAdditionalPayloadsAreRejectedAndAllFixtureDigestsMatch()
    {
        foreach(var file in Directory.GetFiles(Path.Combine(Folder,"rejected")))
            Assert.ThrowsAny<Exception>(()=>SnapshotWirePolicy.Read(File.ReadAllBytes(file)));
        foreach(var line in File.ReadAllLines(Path.Combine(Folder,"SHA256SUMS"))) {
            var pair=line.Split("  "); Assert.Equal(pair[0],Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Folder,pair[1])))));
        }
        Assert.ThrowsAny<Exception>(()=>SnapshotWirePolicy.Read(new byte[65537]));
    }
    private sealed class PoisonSource : IReadOnlySnapshotSource
    {
        public byte[] ReadAlreadyPublishedSnapshot() => throw new InvalidOperationException("Must never call a snapshot source");
    }
    [Fact] public void NoCallerMetadataCanIssueNativeEvidenceOrReadProductionSnapshot()
    {
        Assert.Empty(typeof(AuthenticatedTlsPeer).GetConstructors());
        var b=new DisabledTlsBoundary();
        Assert.Equal(TransportDecision.NativeProofRequired,b.Validate(null));
        Assert.Equal(TransportDecision.Denied,b.AuthorizeSnapshotRead(null));
        Assert.Null(b.RetrieveSnapshot(null,new PoisonSource()));
    }
    [Fact] public void LifecycleEligibilityNeverBindsAndRestartRequiresConsentAgain()
    {
        var l=new InertTransportLifecycle(); Assert.Equal(TransportState.Off,l.State);
        Assert.False(l.EvaluateSynthetic(true)); Assert.True(l.ConfigureSynthetic(true,Limits));
        Assert.Equal(TransportState.Configured,l.State); Assert.True(l.EvaluateSynthetic(true));
        Assert.Equal(TransportState.Eligible,l.State); Assert.False(l.EvaluateSynthetic(false));
        Assert.False(l.EvaluateSynthetic(true)); l.Restart(); Assert.Equal(TransportState.Off,l.State);
        Assert.False(l.ConfigureSynthetic(false,Limits)); l.Restart();
        Assert.True(l.ConfigureSynthetic(true,Limits)); l.Shutdown(); l.Restart();
        Assert.False(l.ConfigureSynthetic(true,Limits)); Assert.Equal(TransportState.Shutdown,l.State);
    }
    [Fact] public void LimitsRejectOverflowZeroTimeoutAndMissingCancellation()
    {
        Assert.False((Limits with {HeaderBytes=int.MaxValue,BodyBytes=int.MaxValue,RequestBytes=int.MaxValue}).Valid);
        Assert.False((Limits with {HandshakeTicks=0}).Valid); Assert.False((Limits with {HeaderTicks=-1}).Valid);
        Assert.False((Limits with {RequestTicks=0}).Valid); Assert.False((Limits with {ShutdownTicks=0}).Valid);
        Assert.False((Limits with {CancelOnStop=false}).Valid); Assert.True(Limits.Valid);
    }
    [Fact] public void ReentrantCrashAndReadDuringCommitCannotAuthorizeOrResurrect()
    {
        var store=new Store(); var ledger=new SyntheticCredentialLedger(store,Start,8,true);
        store.DuringWrite=()=> {
            Assert.False(ledger.ReadSyntheticDecision("phone","old","snapshot.read",Start));
            ledger.CrashOrLostAcknowledgment();
        };
        Assert.False(ledger.IssueSynthetic("phone","old",20,20,Start));
        Assert.Equal(LedgerState.Closed,ledger.State);
        Assert.False(ledger.ReadSyntheticDecision("phone","old","snapshot.read",Start));
        Assert.False(new SyntheticCredentialLedger(store,Start,8,false).ReadSyntheticDecision("phone","old","snapshot.read",Start));
    }
    [Fact] public void ConcurrentRevocationReadAndRepeatedRevokeCannotRestoreAccess()
    {
        var store=new Store(); var ledger=new SyntheticCredentialLedger(store,Start,8,true);
        Assert.True(ledger.IssueSynthetic("phone","old",20,20,Start));
        Parallel.For(0,32,i=> {
            if(i%2==0) ledger.RevokeSynthetic("phone",Start);
            else ledger.ReadSyntheticDecision("phone","old","snapshot.read",Start);
        });
        Assert.False(ledger.ReadSyntheticDecision("phone","old","snapshot.read",Start));
        Assert.True(ledger.RevokeSynthetic("phone",Start)); Assert.Equal(2,store.Writes);
    }
    [Fact] public void InvalidRestoreAndBootClockNeverBecomeReady()
    {
        var store=new Store();
        foreach(var start in new[]{new EpochSample("",new(10,10,true)),new EpochSample("boot-a",new(10,-1,true)),new EpochSample("boot-a",new(10,10,false))})
            Assert.False(new SyntheticCredentialLedger(store,start,8,true).IssueSynthetic("phone","old",20,20,Start));
        Assert.False(new SyntheticCredentialLedger(store,Start,0,true).IssueSynthetic("phone","old",20,20,Start));
        Assert.Equal(0,store.Writes);
    }
}
