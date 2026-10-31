using System.Collections.Immutable;
using System.Security.Cryptography;
using PeripheralBatteryCompanion.Core.SnapshotService;
using PeripheralBatteryCompanion.Core.TransportSecurity;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class TransportSecurityPrerequisiteTests
{
    private static readonly ImmutableArray<byte> Pin = ImmutableArray.CreateRange(Enumerable.Repeat((byte)1,32));
    private static readonly DateTimeOffset Now = new(2026,10,29,12,0,0,TimeSpan.Zero);
    private static ApprovedLanEndpoint Endpoint => new("synthetic-interface-a", "192.168.1.10", 8443, "pbc-synthetic.local", Pin, LanAuthenticationChoice.PinnedTlsBearer);
    private static LanOwnerApproval Approval => new(Endpoint, TransportLimitPolicy.ReviewDefaults, Now, Now.AddHours(1), true);
    private static WindowsSecurityEvidence Evidence => new(new(Endpoint.InterfaceId,Endpoint.Address,true,NetworkTrustProfile.Private),
        new(Endpoint.TlsDnsName,Pin,Now.AddDays(-1),Now.AddDays(90),true,true,true),ProtectedStorageEvidence.WindowsCurrentUserProtected);

    [Fact]
    public void NewSessionIsOffAndDoesNotInspectOrProvisionAnything()
    {
        var source = new Source(); using var session = new LanApprovalSession(source,new Clock());
        Assert.False(session.DesiredSettings.OwnerEnabled); Assert.Equal(SecurityBlocker.Off,session.Evaluate().Blocker);
        Assert.Equal(0,source.Observations);
    }
    [Fact]
    public void PersistedOptInNeverRestoresSessionApproval()
    {
        var source=new Source(); using var session=new LanApprovalSession(source,new Clock(),new(true));
        Assert.True(session.DesiredSettings.OwnerEnabled); Assert.False(session.Evaluate().Approved);
        Assert.Equal(SecurityBlocker.OwnerApprovalRequired,session.Evaluate().Blocker); Assert.Equal(0,source.Observations);
    }
    [Theory]
    [InlineData(LanAuthenticationChoice.PinnedTlsBearer)] [InlineData(LanAuthenticationChoice.MutualTls)]
    public void ExplicitOwnerChoiceIsRequiredButNoAuthenticationModeIsChosenImplicitly(LanAuthenticationChoice choice)
    {
        var source=new Source(); using var session=new LanApprovalSession(source,new Clock());
        Assert.True(session.TryEnableByOwner(Approval with {Endpoint=Endpoint with {Authentication=choice}}).Approved);
        Assert.True(session.Evaluate().Approved); Assert.Equal(2,source.Observations);
    }
    [Theory]
    [InlineData(LanAuthenticationChoice.Unspecified)] [InlineData((LanAuthenticationChoice)999)]
    public void UndecidedAuthenticationCannotBecomeApproval(LanAuthenticationChoice choice)
    {
        var source=new Source(); using var session=new LanApprovalSession(source,new Clock());
        Assert.Equal(SecurityBlocker.AuthenticationUndecided,session.TryEnableByOwner(Approval with {Endpoint=Endpoint with {Authentication=choice}}).Blocker);
        Assert.Equal(0,source.Observations);
    }
    [Fact]
    public void MissingOwnerConsentAndInvalidApprovalWindowFailBeforeObservation()
    {
        var source=new Source(); using var session=new LanApprovalSession(source,new Clock());
        Assert.Equal(SecurityBlocker.InvalidApproval,session.TryEnableByOwner(Approval with {OwnerConsentGranted=false}).Blocker);
        Assert.Equal(SecurityBlocker.InvalidApproval,session.TryEnableByOwner(Approval with {ApprovedAt=Now.AddSeconds(1)}).Blocker);
        Assert.Equal(SecurityBlocker.InvalidApproval,session.TryEnableByOwner(Approval with {ExpiresAt=Now}).Blocker);
        Assert.Equal(0,source.Observations);
    }
    [Fact]
    public void ExpirationClearsApprovalAndDoesNotAutomaticallyResume()
    {
        var source=new Source(); var clock=new Clock(); using var session=new LanApprovalSession(source,clock);
        Assert.True(session.TryEnableByOwner(Approval).Approved); clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(SecurityBlocker.ExpiredApproval,session.Evaluate().Blocker);
        var count=source.Observations; Assert.False(session.Evaluate().Approved); Assert.Equal(count,source.Observations);
        Assert.Equal(SecurityBlocker.ExpiredApproval,session.TryEnableByOwner(Approval).Blocker);
    }
    [Fact]
    public void WallClockRollbackCannotExtendAnApprovedSession()
    {
        var clock=new Clock(); using var session=new LanApprovalSession(new Source(),clock);
        Assert.True(session.TryEnableByOwner(Approval).Approved);
        clock.Advance(TimeSpan.FromMinutes(30)); clock.MoveWallClockBack(TimeSpan.FromMinutes(20));
        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(SecurityBlocker.ExpiredApproval,session.Evaluate().Blocker);
    }
    [Theory]
    [InlineData("0.0.0.0")] [InlineData("::")] [InlineData("127.0.0.1")] [InlineData("::1")]
    [InlineData("8.8.8.8")] [InlineData("224.0.0.1")] [InlineData("169.254.1.1")]
    [InlineData("fe80::1%2")] [InlineData("::ffff:192.168.1.10")] [InlineData("fd00::1%2")]
    [InlineData("192.168.001.010")] [InlineData("192.168.1.10:8443")]
    public void WildcardPublicLoopbackMulticastScopedAndNoncanonicalAddressesAreRejected(string address)
    {
        var source=new Source(); using var session=new LanApprovalSession(source,new Clock());
        Assert.Equal(SecurityBlocker.InvalidEndpoint,session.TryEnableByOwner(Approval with {Endpoint=Endpoint with {Address=address}}).Blocker);
        Assert.Equal(0,source.Observations);
    }
    [Theory]
    [InlineData("10.0.0.1")] [InlineData("172.16.0.1")] [InlineData("172.31.255.254")]
    [InlineData("192.168.1.10")] [InlineData("fd00::1")]
    public void PrivatePrefixStillRequiresExactInterfaceAndPrivateProfileEvidence(string address)
    {
        var endpoint=Endpoint with {Address=address}; var source=new Source {Current=Evidence with {Network=Evidence.Network! with {Address=address}}};
        using var session=new LanApprovalSession(source,new Clock());
        Assert.True(session.TryEnableByOwner(Approval with {Endpoint=endpoint}).Approved);
        source.Current=source.Current with {Network=source.Current.Network! with {Profile=NetworkTrustProfile.Public}};
        Assert.Equal(SecurityBlocker.NetworkNotPrivate,session.Evaluate().Blocker);
        source.Current=Evidence with {Network=Evidence.Network! with {Address=address}};
        Assert.False(session.Evaluate().Approved); // restoring network alone never re-enables.
    }
    [Theory]
    [InlineData(NetworkTrustProfile.Public)] [InlineData(NetworkTrustProfile.Unknown)] [InlineData(NetworkTrustProfile.Domain)]
    public void UnapprovedNetworkProfilesFailClosed(NetworkTrustProfile profile)
    {
        var source=new Source {Current=Evidence with {Network=Evidence.Network! with {Profile=profile}}};
        using var session=new LanApprovalSession(source,new Clock());
        Assert.Equal(SecurityBlocker.NetworkNotPrivate,session.TryEnableByOwner(Approval).Blocker);
    }
    [Fact]
    public void InterfaceChangeOrDownStateRequiresNewExplicitApproval()
    {
        var source=new Source(); using var session=new LanApprovalSession(source,new Clock());
        Assert.True(session.TryEnableByOwner(Approval).Approved);
        source.Current=Evidence with {Network=Evidence.Network! with {InterfaceId="synthetic-interface-b"}};
        Assert.Equal(SecurityBlocker.WrongInterface,session.Evaluate().Blocker);
        source.Current=Evidence; Assert.False(session.Evaluate().Approved);
        Assert.True(session.TryEnableByOwner(Approval).Approved);
        source.Current=Evidence with {Network=Evidence.Network! with {IsUp=false}};
        Assert.Equal(SecurityBlocker.WrongInterface,session.Evaluate().Blocker);
    }
    [Fact]
    public void CertificateOrPrivateKeyLossNeverCreatesOrTrustsReplacement()
    {
        var source=new Source(); using var session=new LanApprovalSession(source,new Clock());
        Assert.True(session.TryEnableByOwner(Approval).Approved);
        source.Current=Evidence with {Identity=null}; Assert.Equal(SecurityBlocker.IdentityUnavailable,session.Evaluate().Blocker);
        source.Current=Evidence; Assert.False(session.Evaluate().Approved);
        source.Current=Evidence with {Identity=Evidence.Identity! with {PrivateKeyPresent=false}};
        Assert.Equal(SecurityBlocker.IdentityUnavailable,session.TryEnableByOwner(Approval).Blocker);
    }
    [Fact]
    public void PinOrHostnameChangeNeverSilentlyRotatesIdentity()
    {
        var source=new Source {Current=Evidence with {Identity=Evidence.Identity! with {SpkiSha256=ImmutableArray.CreateRange(new byte[32])}}};
        using var session=new LanApprovalSession(source,new Clock());
        Assert.Equal(SecurityBlocker.IdentityChanged,session.TryEnableByOwner(Approval).Blocker);
        source.Current=Evidence with {Identity=Evidence.Identity! with {TlsDnsName="unapproved.local"}};
        Assert.Equal(SecurityBlocker.IdentityChanged,session.TryEnableByOwner(Approval).Blocker);
    }
    [Fact]
    public void CertificateLifetimeMustCoverCurrentTimeAndApproval()
    {
        var source=new Source(); using var session=new LanApprovalSession(source,new Clock());
        foreach(var identity in new[]{Evidence.Identity! with {ValidUntil=Now},Evidence.Identity! with {ValidFrom=Now.AddMinutes(1)},Evidence.Identity! with {ValidUntil=Now.AddMinutes(1)}}) {
            source.Current=Evidence with {Identity=identity}; Assert.Equal(SecurityBlocker.CertificateTimeInvalid,session.TryEnableByOwner(Approval).Blocker);
        }
    }
    [Fact]
    public void ExportableOrUnprotectedKeysAndUnprotectedEnrollmentStorageAreRejected()
    {
        var source=new Source(); using var session=new LanApprovalSession(source,new Clock());
        source.Current=Evidence with {Identity=Evidence.Identity! with {NonExportable=false}};
        Assert.Equal(SecurityBlocker.KeyNotProtected,session.TryEnableByOwner(Approval).Blocker);
        source.Current=Evidence with {Identity=Evidence.Identity! with {CurrentUserAclRestricted=false}};
        Assert.Equal(SecurityBlocker.KeyNotProtected,session.TryEnableByOwner(Approval).Blocker);
        source.Current=Evidence with {EnrollmentStorage=ProtectedStorageEvidence.Unavailable};
        Assert.Equal(SecurityBlocker.EnrollmentStorageUnavailable,session.TryEnableByOwner(Approval).Blocker);
    }
    [Fact]
    public void PortPinAndTlsAuthorityMustBeExplicitAndValid()
    {
        var source=new Source(); using var session=new LanApprovalSession(source,new Clock());
        foreach(var endpoint in new[]{Endpoint with {Port=0},Endpoint with {Port=443},Endpoint with {Port=65536},Endpoint with {ExpectedSpkiSha256=default},Endpoint with {ExpectedSpkiSha256=ImmutableArray.CreateRange(new byte[32])},Endpoint with {TlsDnsName="*"},Endpoint with {InterfaceId=""}})
            Assert.Equal(SecurityBlocker.InvalidEndpoint,session.TryEnableByOwner(Approval with {Endpoint=endpoint}).Blocker);
        Assert.Equal(0,source.Observations);
    }
    [Fact]
    public void TransportBudgetRejectsUnlimitedAndBodyBearingConfigurations()
    {
        var source=new Source(); using var session=new LanApprovalSession(source,new Clock()); var limits=TransportLimitPolicy.ReviewDefaults;
        foreach(var limit in new[]{limits with {MaxConnections=17},limits with {TlsHandshakeTimeout=Timeout.InfiniteTimeSpan},limits with {HeaderTimeout=TimeSpan.Zero},limits with {MaxHeaderBytes=8193},limits with {MaxBodyBytes=1},limits with {IdleTimeout=TimeSpan.FromDays(1)},limits with {MaxResponseBytes=65537},limits with {ShutdownTimeout=TimeSpan.Zero}})
            Assert.Equal(SecurityBlocker.InvalidTransportLimits,session.TryEnableByOwner(Approval with {Limits=limit}).Blocker);
        Assert.Equal(0,source.Observations);
    }
    [Fact]
    public void FailedPlatformObservationReturnsOnlyTypedFailure()
    {
        var source=new Source {Throw=true}; using var session=new LanApprovalSession(source,new Clock());
        var result=session.TryEnableByOwner(Approval); Assert.Equal(SecurityBlocker.EvidenceUnavailable,result.Blocker);
        Assert.DoesNotContain("private",result.ToString());
    }
    [Fact]
    public void DisableAndShutdownAreTerminalForCurrentApprovalAndDoNotObserve()
    {
        var source=new Source(); using var session=new LanApprovalSession(source,new Clock()); session.TryEnableByOwner(Approval);
        session.DisableByOwner(); var count=source.Observations; Assert.Equal(SecurityBlocker.Off,session.Evaluate().Blocker);
        Assert.Equal(count,source.Observations); Assert.False(session.DesiredSettings.OwnerEnabled);
        session.TryEnableByOwner(Approval); session.Dispose(); count=source.Observations;
        Assert.Equal(SecurityBlocker.Shutdown,session.TryEnableByOwner(Approval).Blocker);
        session.DisableByOwner(); Assert.Equal(SecurityBlocker.Shutdown,session.Evaluate().Blocker); Assert.Equal(count,source.Observations);
    }
    [Fact]
    public void GeneratedSecretIs256BitsRedactedRevocableAndDisposed()
    {
        using var secret=Ephemeral256BitSecret.Create(); var bytes=new byte[32]; secret.CopyTo(bytes);
        try {
            Assert.Equal(32,bytes.Length); Assert.Equal(SHA256.HashData(bytes),secret.ComputeSha256Verifier().ToArray());
            Assert.Contains("redacted",secret.ToString());
            var registry=new ApprovedPhoneRegistry(); var phone=Guid.NewGuid(); registry.ApproveVerifier(phone,secret.ComputeSha256Verifier().AsSpan());
            using var core=new SnapshotRequestCore(new EmptySource(),registry); core.EnableByLocalOwner();
            var header="Bearer "+Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
            Assert.Equal(503,core.Handle(new("GET",SnapshotRequestCore.SnapshotPath,header,true)).StatusCode); // approved, but no snapshot
            registry.Revoke(phone); Assert.Equal(401,core.Handle(new("GET",SnapshotRequestCore.SnapshotPath,header,true)).StatusCode);
        } finally {CryptographicOperations.ZeroMemory(bytes);}
        secret.Dispose(); Assert.Throws<ObjectDisposedException>(()=>secret.CopyTo(new byte[32]));
        Assert.Throws<ObjectDisposedException>(()=>secret.ComputeSha256Verifier());
    }
    [Fact]
    public void SecretExportRequiresExplicitCorrectSizedBuffer()
    {
        using var secret=Ephemeral256BitSecret.Create(); Assert.Throws<ArgumentException>(()=>secret.CopyTo(new byte[31]));
        Assert.Throws<ArgumentException>(()=>secret.CopyTo(new byte[33]));
    }
    private sealed class Source : IWindowsSecurityEvidenceSource
    {
        public int Observations {get;private set;}
        public WindowsSecurityEvidence Current {get;set;}=Evidence;
        public bool Throw {get;set;}
        public WindowsSecurityEvidence Observe(ApprovedLanEndpoint selection) { Observations++; if(Throw)throw new InvalidOperationException("private platform details"); return Current; }
    }
    private sealed class EmptySource : IReadOnlyPublishedSnapshot {public PublishedSnapshot? ReadLatest()=>null;}
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now=Now;
        private long _ticks;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>_ticks;
        public override DateTimeOffset GetUtcNow()=>_now;
        public void Advance(TimeSpan by) { _now+=by; _ticks+=by.Ticks; }
        public void MoveWallClockBack(TimeSpan by)=>_now-=by;
    }
}
