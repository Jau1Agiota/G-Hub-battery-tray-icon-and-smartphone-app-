using System.Collections.Immutable;
using System.Security.Cryptography;

namespace PeripheralBatteryCompanion.Core.TransportSecurity;

/// <summary>SERVER identity of a FUTURE authenticated TLS channel (local or remote role), not a phone/client certificate. Data is not proof.</summary>
public sealed class PeerBinding(string endpoint, Guid identityId, ReadOnlySpan<byte> leafDer)
{
    public string Endpoint { get; } = endpoint;
    public Guid IdentityId { get; } = identityId;
    public ImmutableArray<byte> LeafDer { get; } = leafDer.Length is > 0 and <= 4096 ? ImmutableArray.CreateRange(leafDer.ToArray()) : [];
    public override string ToString() => "PeerBinding (redacted; platform proof required)";
}
/// <summary>Must verify the LIVE channel's server possession, exact leaf/profile/signature and origin; never requires phone mTLS. No production issuer exists.</summary>
public interface IPeerCertificateVerifier
{
    bool IsAuthenticatedPeer(EnrollmentIdentity expected, PeerBinding observed, DateTimeOffset now);
}
public sealed class DenyUnverifiedPeer : IPeerCertificateVerifier
{
    public bool IsAuthenticatedPeer(EnrollmentIdentity expected, PeerBinding observed, DateTimeOffset now) => false;
}

internal static class ProtocolCredentials
{
    internal static bool TryHashBearer(string? header, Span<byte> digest)
    {
        if (header is not { Length: 50 } || !header.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        return TryHashNonce(header[7..], digest);
    }
    internal static bool TryHashNonce(string? text, Span<byte> digest)
    {
        if (digest.Length != 32 || text is not { Length: 43 } || text.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) return false;
        Span<byte> bytes = stackalloc byte[32];
        try
        {
            if (!Convert.TryFromBase64String(text.Replace('-', '+').Replace('_', '/') + "=", bytes, out int count) || count != 32 ||
                Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') != text) return false;
            SHA256.HashData(bytes, digest); return true;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    internal static bool PeerMatches(EnrollmentIdentity identity, PeerBinding? peer, IPeerCertificateVerifier verifier, DateTimeOffset now)
    {
        if (peer is null || peer.Endpoint != identity.Endpoint || peer.IdentityId != identity.IdentityId ||
            !peer.LeafDer.AsSpan().SequenceEqual(identity.LeafDer.AsSpan()) || now < identity.LeafNotBefore || now >= identity.LeafNotAfter) return false;
        try { return verifier.IsAuthenticatedPeer(identity, peer, now); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { return false; }
    }
}

internal sealed class SecuritySessionClock(TimeProvider clock)
{
    private readonly object _gate = new();
    private DateTimeOffset _utc = clock.GetUtcNow();
    private long _tick = clock.GetTimestamp();
    private readonly long _frequency = clock.TimestampFrequency;
    private volatile bool _failed;
    internal bool Read(out DateTimeOffset utc, out long tick)
    {
        lock (_gate)
        {
        utc = default; tick = 0;
        if (_failed) return false;
        try
        {
            utc = clock.GetUtcNow(); tick = clock.GetTimestamp();
            if (utc.Offset != TimeSpan.Zero || utc < _utc || tick < _tick || _frequency <= 0 || clock.TimestampFrequency != _frequency)
            { _failed = true; return false; }
            _ = checked(tick - _tick); _utc = utc; _tick = tick; return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { _failed = true; return false; }
        }
    }
    internal bool Before(long start, long end, decimal seconds)
    {
        try { return checked(end - start) >= 0 && (decimal)checked(end - start) / _frequency < seconds; }
        catch (ArithmeticException) { _failed = true; return false; }
    }
}

public enum PairingOutcome { Rejected, Pending, Approved }
public sealed class PairingStatus
{
    public PairingOutcome Outcome { get; }
    public Guid? PhoneId { get; }
    public long? AuthorizationGeneration { get; }
    public DateTimeOffset? IssuedAt { get; }
    public DateTimeOffset? ExpiresAt { get; }
    internal PairingStatus(PairingOutcome outcome, AuthorizationRecord? record = null)
    {
        Outcome = outcome;
        if (outcome == PairingOutcome.Approved && record is not null)
        { PhoneId = record.PhoneId; AuthorizationGeneration = record.Generation; IssuedAt = record.IssuedAt; ExpiresAt = record.ExpiresAt; }
    }
    public override string ToString() => "PairingStatus (redacted)";
}

/// <summary>Authenticated claim/status policy, no routes, RNG, QR persistence, native calls or credential generation.</summary>
public sealed class PairingSession : IDisposable
{
    private readonly object _gate = new();
    private readonly EnrollmentIdentity _identity;
    private readonly EnrollmentAuthorization _authorization;
    private readonly IPeerCertificateVerifier _verifier;
    private readonly SecuritySessionClock _clock;
    private byte[]? _bootstrap;
    private Guid _id;
    private long _start, _lastStatus;
    private DateTimeOffset _expiry;
    private bool _queried, _closed;
    private int _attempts;
    private EnrollmentClaim? _claim;
    private PairingStatus _status = new(PairingOutcome.Rejected);
    public PairingSession(EnrollmentIdentity identity, EnrollmentAuthorization authorization,
        TimeProvider? clock = null, IPeerCertificateVerifier? verifier = null)
    {
        _identity = identity; _authorization = authorization; _clock = new(clock ?? TimeProvider.System);
        _verifier = verifier ?? new DenyUnverifiedPeer();
    }
    public bool OpenByLocalOwner(Guid enrollmentId, ReadOnlySpan<byte> bootstrapSha256)
    {
        lock (_gate)
        {
            if (_closed || _bootstrap is not null || bootstrapSha256.Length != 32 || !_clock.Read(out var now, out var tick) ||
                !_authorization.BeginEnrollmentByLocalOwner(enrollmentId)) return false;
            _bootstrap = bootstrapSha256.ToArray(); _id = enrollmentId; _start = tick;
            _expiry = _identity.LeafNotAfter - now < TimeSpan.FromSeconds(120) ? _identity.LeafNotAfter : now.AddSeconds(120);
            _status = new(PairingOutcome.Pending); return true;
        }
    }
    public bool TryClaim(PeerBinding peer, string? bootstrapHeader, EnrollmentClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        lock (_gate)
        {
            if (!AdmitClaim(peer, bootstrapHeader) || claim.EnrollmentId != _id) return false;
            if (!_authorization.TryClaim(claim)) return false;
            _claim = claim; return true;
        }
    }
    public bool TryClaimJson(PeerBinding peer, string? bootstrapHeader, ReadOnlyMemory<byte> body)
    {
        lock (_gate)
        {
            if (!AdmitClaim(peer, bootstrapHeader) || body.Length is < 1 or > EnrollmentClaimCodec.MaximumBytes ||
                !EnrollmentClaimCodec.TryDecode(body, out var claim) || claim is null || claim.EnrollmentId != _id ||
                !_authorization.TryClaim(claim)) return false;
            _claim = claim; return true;
        }
    }
    private bool AdmitClaim(PeerBinding peer, string? header)
    {
        if (!Window(out var now, out _) || _attempts >= 8 || _claim is not null) return false;
        _attempts++;
        return ProtocolCredentials.PeerMatches(_identity, peer, _verifier, now) && BootstrapMatches(header);
    }
    public bool ConfirmByLocalOwner(string fullTranscriptSha256)
    {
        lock (_gate)
        {
            if (!Window(out _, out _) || _claim is null || _status.Outcome != PairingOutcome.Pending) return false;
            if (!_authorization.ConfirmEnrollmentByLocalOwner(fullTranscriptSha256) ||
                !_authorization.TryReadApprovedEnrollment(_claim, out var record)) { _status = new(PairingOutcome.Rejected); return false; }
            _status = new(PairingOutcome.Approved, record); return true;
        }
    }
    public PairingStatus ReadStatus(PeerBinding peer, Guid enrollmentId, string? bootstrapHeader)
    {
        lock (_gate)
        {
            if (!Window(out var now, out var tick) || (_queried && _clock.Before(_lastStatus, tick, 5))) return new(PairingOutcome.Rejected);
            _queried = true; _lastStatus = tick;
            if (_claim is null || enrollmentId != _id || !ProtocolCredentials.PeerMatches(_identity, peer, _verifier, now) || !BootstrapMatches(bootstrapHeader)) return new(PairingOutcome.Rejected);
            if (_status.Outcome == PairingOutcome.Approved && !_authorization.TryReadApprovedEnrollment(_claim, out _))
                _status = new(PairingOutcome.Rejected);
            return _status;
        }
    }
    private bool BootstrapMatches(string? header)
    {
        Span<byte> digest = stackalloc byte[32];
        try { return _bootstrap is not null && ProtocolCredentials.TryHashBearer(header, digest) && CryptographicOperations.FixedTimeEquals(digest, _bootstrap); }
        finally { CryptographicOperations.ZeroMemory(digest); }
    }
    private bool Window(out DateTimeOffset now, out long tick)
    {
        if (!_closed && _bootstrap is not null && _clock.Read(out now, out tick) && now < _expiry && _clock.Before(_start, tick, 120)) return true;
        now = default; tick = 0; Close(); return false;
    }
    private void Close()
    {
        _closed = true;
        if (_status.Outcome == PairingOutcome.Pending) _authorization.RejectPendingEnrollment();
        if (_bootstrap is not null) CryptographicOperations.ZeroMemory(_bootstrap); _bootstrap = null;
    }
    public void Dispose() { lock (_gate) Close(); }
}
