using System.Collections.Immutable;
using PeripheralBatteryCompanion.Core.Snapshots;

namespace PeripheralBatteryCompanion.Core.TransportSecurity;

public enum LanAuthenticationChoice { Unspecified, PinnedTlsBearer, MutualTls }
public enum NetworkTrustProfile { Unknown, Private, Public, Domain }
public enum ProtectedStorageEvidence { Unavailable, WindowsCurrentUserProtected }
public enum SecurityBlocker
{
    None, Off, OwnerApprovalRequired, InvalidApproval, ExpiredApproval, AuthenticationUndecided,
    InvalidEndpoint, InvalidTransportLimits, EvidenceUnavailable, WrongInterface, NetworkNotPrivate,
    IdentityUnavailable, IdentityChanged, CertificateTimeInvalid, KeyNotProtected, EnrollmentStorageUnavailable, Shutdown
}

public sealed record LanOptInSettings(bool OwnerEnabled = false);
public sealed record ApprovedLanEndpoint(string InterfaceId, string Address, int Port, string TlsDnsName,
    ImmutableArray<byte> ExpectedSpkiSha256, LanAuthenticationChoice Authentication);
public sealed record TransportLimitPolicy(int MaxConnections, TimeSpan TlsHandshakeTimeout, TimeSpan HeaderTimeout,
    int MaxHeaderBytes, int MaxBodyBytes, TimeSpan IdleTimeout, int MaxResponseBytes, TimeSpan ShutdownTimeout)
{
    // Review budgets only: no transport exists to enforce these values in this stage.
    public static TransportLimitPolicy ReviewDefaults { get; } = new(16, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5),
        8192, 0, TimeSpan.FromSeconds(10), BatterySnapshotJson.MaxPayloadBytes, TimeSpan.FromSeconds(5));
    public bool IsBounded => MaxConnections is >= 1 and <= 16 &&
        TlsHandshakeTimeout > TimeSpan.Zero && TlsHandshakeTimeout <= TimeSpan.FromSeconds(5) &&
        HeaderTimeout > TimeSpan.Zero && HeaderTimeout <= TimeSpan.FromSeconds(5) &&
        MaxHeaderBytes is >= 1 and <= 8192 && MaxBodyBytes == 0 &&
        IdleTimeout > TimeSpan.Zero && IdleTimeout <= TimeSpan.FromSeconds(10) &&
        MaxResponseBytes is >= 1 and <= BatterySnapshotJson.MaxPayloadBytes &&
        ShutdownTimeout > TimeSpan.Zero && ShutdownTimeout <= TimeSpan.FromSeconds(5);
}
public sealed record LanOwnerApproval(ApprovedLanEndpoint Endpoint, TransportLimitPolicy Limits,
    DateTimeOffset ApprovedAt, DateTimeOffset ExpiresAt, bool OwnerConsentGranted = false);
public sealed record LocalInterfaceEvidence(string InterfaceId, string Address, bool IsUp, NetworkTrustProfile Profile);
public sealed record ServerIdentityEvidence(string TlsDnsName, ImmutableArray<byte> SpkiSha256,
    DateTimeOffset ValidFrom, DateTimeOffset ValidUntil, bool PrivateKeyPresent, bool NonExportable, bool CurrentUserAclRestricted);
public sealed record WindowsSecurityEvidence(LocalInterfaceEvidence? Network, ServerIdentityEvidence? Identity,
    ProtectedStorageEvidence EnrollmentStorage);
public sealed record SecurityDecision(bool Approved, SecurityBlocker Blocker);

/// <summary>Future trusted Windows-native adapter. None is implemented/registered in this stage.</summary>
public interface IWindowsSecurityEvidenceSource
{
    WindowsSecurityEvidence Observe(ApprovedLanEndpoint selection);
}
