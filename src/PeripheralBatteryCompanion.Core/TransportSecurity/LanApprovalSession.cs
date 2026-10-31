using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace PeripheralBatteryCompanion.Core.TransportSecurity;

/// <summary>Non-listening policy only. Evidence checks do not implement or prove TLS, native storage or a user-approval UI.</summary>
public sealed class LanApprovalSession(IWindowsSecurityEvidenceSource evidence, TimeProvider? clock = null,
    LanOptInSettings? desiredSettings = null) : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private LanOptInSettings _settings = desiredSettings ?? new();
    private LanOwnerApproval? _approval;
    private long? _approvalTick;
    private TimeSpan _approvalBudget;
    private SecurityDecision _last = new(false, desiredSettings?.OwnerEnabled == true ? SecurityBlocker.OwnerApprovalRequired : SecurityBlocker.Off);
    private bool _closed;
    public LanOptInSettings DesiredSettings { get { lock (_gate) return _settings; } }

    // Trusted future LOCAL owner UI API. Never expose this as a request, pairing route or persisted approval boolean.
    public SecurityDecision TryEnableByOwner(LanOwnerApproval approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        lock (_gate)
        {
            if (_closed) return _last;
            _approval = approval;
            _approvalTick = null;
            var result = Check();
            if (result.Approved) _settings = new(true);
            return _last = result;
        }
    }
    public SecurityDecision Evaluate()
    {
        lock (_gate)
        {
            if (_closed || _approval is null) return _last;
            return _last = Check();
        }
    }
    public void DisableByOwner()
    {
        lock (_gate) { if (_closed) return; _settings = new(); _approval = null; _last = new(false, SecurityBlocker.Off); }
    }
    private SecurityDecision Deny(SecurityBlocker reason) { _approval = null; _approvalTick = null; return new(false, reason); }
    private SecurityDecision Check()
    {
        var approval = _approval!;
        var now = _clock.GetUtcNow();
        if (!approval.OwnerConsentGranted || approval.ExpiresAt <= approval.ApprovedAt || approval.ApprovedAt > now)
            return Deny(SecurityBlocker.InvalidApproval);
        if (now >= approval.ExpiresAt || (_approvalTick is { } tick && _clock.GetElapsedTime(tick) >= _approvalBudget))
            return Deny(SecurityBlocker.ExpiredApproval);
        if (_approvalTick is null) { _approvalTick = _clock.GetTimestamp(); _approvalBudget = approval.ExpiresAt - now; }
        var endpoint = approval.Endpoint;
        if (endpoint is null || endpoint.Authentication == LanAuthenticationChoice.Unspecified || !Enum.IsDefined(endpoint.Authentication))
            return Deny(SecurityBlocker.AuthenticationUndecided);
        if (!ValidEndpoint(endpoint)) return Deny(SecurityBlocker.InvalidEndpoint);
        if (approval.Limits is null || !approval.Limits.IsBounded) return Deny(SecurityBlocker.InvalidTransportLimits);
        WindowsSecurityEvidence observed;
        try { observed = evidence.Observe(endpoint); }
        catch { return Deny(SecurityBlocker.EvidenceUnavailable); }
        if (observed is null) return Deny(SecurityBlocker.EvidenceUnavailable);
        var network = observed.Network;
        if (network is null || !network.IsUp || network.InterfaceId != endpoint.InterfaceId ||
            !string.Equals(network.Address, endpoint.Address, StringComparison.OrdinalIgnoreCase)) return Deny(SecurityBlocker.WrongInterface);
        if (network.Profile != NetworkTrustProfile.Private) return Deny(SecurityBlocker.NetworkNotPrivate);
        var identity = observed.Identity;
        if (identity is null || !identity.PrivateKeyPresent) return Deny(SecurityBlocker.IdentityUnavailable);
        if (identity.SpkiSha256.IsDefault || identity.SpkiSha256.Length != 32 ||
            !CryptographicOperations.FixedTimeEquals(identity.SpkiSha256.AsSpan(), endpoint.ExpectedSpkiSha256.AsSpan()) ||
            !string.Equals(identity.TlsDnsName, endpoint.TlsDnsName, StringComparison.OrdinalIgnoreCase)) return Deny(SecurityBlocker.IdentityChanged);
        if (identity.ValidFrom > now || identity.ValidUntil <= now || identity.ValidUntil <= identity.ValidFrom || approval.ExpiresAt > identity.ValidUntil)
            return Deny(SecurityBlocker.CertificateTimeInvalid);
        if (!identity.NonExportable || !identity.CurrentUserAclRestricted) return Deny(SecurityBlocker.KeyNotProtected);
        if (observed.EnrollmentStorage != ProtectedStorageEvidence.WindowsCurrentUserProtected)
            return Deny(SecurityBlocker.EnrollmentStorageUnavailable);
        if (_clock.GetUtcNow() >= approval.ExpiresAt || _clock.GetElapsedTime(_approvalTick!.Value) >= _approvalBudget)
            return Deny(SecurityBlocker.ExpiredApproval);
        return new(true, SecurityBlocker.None);
    }
    private static bool ValidEndpoint(ApprovedLanEndpoint value)
    {
        if (string.IsNullOrWhiteSpace(value.InterfaceId) || value.InterfaceId.Length > 128 || value.InterfaceId.Any(char.IsControl) ||
            value.Port is < 1024 or > 65535 || string.IsNullOrEmpty(value.TlsDnsName) || value.TlsDnsName.Length > 253 ||
            Uri.CheckHostName(value.TlsDnsName) != UriHostNameType.Dns || value.ExpectedSpkiSha256.IsDefault ||
            value.ExpectedSpkiSha256.Length != 32 || value.ExpectedSpkiSha256.All(b => b == 0) ||
            !IPAddress.TryParse(value.Address, out var ip) || !string.Equals(ip.ToString(), value.Address, StringComparison.OrdinalIgnoreCase)) return false;
        var bytes = ip.GetAddressBytes();
        return ip.AddressFamily switch
        {
            AddressFamily.InterNetwork => bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168,
            AddressFamily.InterNetworkV6 => ip.ScopeId == 0 && (bytes[0] & 0xfe) == 0xfc,
            _ => false
        };
    }
    public void Dispose()
    {
        lock (_gate) { _closed = true; _approval = null; _settings = new(); _last = new(false, SecurityBlocker.Shutdown); }
    }
}
