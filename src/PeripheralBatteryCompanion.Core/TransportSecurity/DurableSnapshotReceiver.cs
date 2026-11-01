using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using PeripheralBatteryCompanion.Core.ConnectionFoundations;

namespace PeripheralBatteryCompanion.Core.TransportSecurity;

/// <summary>Client-role bounded receive/replay core. No sockets, polling, platform peer issuer or native credentials.</summary>
public sealed class DurableSnapshotReceiver
{
    private readonly object _gate = new();
    private readonly EnrollmentIdentity _identity;
    private readonly AuthorizationRecord _authorization;
    private readonly IReplayAuthorizationJournal _journal;
    private readonly IPeerCertificateVerifier _peers;
    private readonly SecuritySessionClock _clock;
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);
    private ReplayHistory? _history;
    private string? _nonce;
    private long _attempt, _start, _credentialTick;
    private DateTimeOffset _expiry;
    private decimal _credentialSeconds;
    private bool _failed, _foreground;
    private ImmutableArray<byte> _visible = [];
    public bool RecoveryRequired { get { lock (_gate) return _failed; } }
    public ImmutableArray<byte> VisibleBody
    {
        get
        {
            lock (_gate)
            {
                try { if (_foreground && Current(out _, out _)) return _visible; }
                catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { Fail(); }
                return [];
            }
        }
    }
    public DurableSnapshotReceiver(EnrollmentIdentity identity, AuthorizationRecord authorization,
        IReplayAuthorizationJournal journal, TimeProvider? clock = null, IPeerCertificateVerifier? peers = null)
    {
        _identity = identity; _authorization = authorization; _journal = journal; _clock = new(clock ?? TimeProvider.System);
        _peers = peers ?? new DenyUnverifiedPeer();
        try
        {
            var loaded = journal.Read();
            if (loaded.Outcome != AuthorizationLoadOutcome.VerifiedRecord || loaded.Record is null || authorization.Revoked ||
                !identity.Matches(authorization.Identity) || !AuthorizationRecordCodec.Encode(loaded.Record).AsSpan().SequenceEqual(AuthorizationRecordCodec.Encode(authorization)) ||
                (_history = journal.ReadReplay(authorization.Generation)) is null || !_clock.Read(out var now, out _credentialTick) || now < authorization.IssuedAt || now >= authorization.ExpiresAt)
            { Fail(); return; }
            _expiry = authorization.ExpiresAt; _credentialSeconds = (decimal)(_expiry - now).Ticks / TimeSpan.TicksPerSecond;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { Fail(); }
    }
    public bool StartForegroundByLocalOwner() { lock (_gate) return _foreground = Current(out _, out _); }
    public void Pause() { lock (_gate) { _foreground = false; _nonce = null; _visible = []; } }
    /// <summary>The trusted future platform caller must supply a new CSPRNG nonce per attempt, including after restart.</summary>
    public bool TryBegin(PeerBinding peer, string nonce, out long attempt)
    {
        attempt = 0;
        lock (_gate)
        {
            if (!_foreground || _nonce is not null || !Current(out var now, out var tick) ||
                !ProtocolCredentials.PeerMatches(_identity, peer, _peers, now)) return false;
            Span<byte> digest = stackalloc byte[32];
            if (!ProtocolCredentials.TryHashNonce(nonce, digest) || _used.Count >= 4096 ||
                _history is null || digest.SequenceEqual(_history.LastNonceDigest.AsSpan()) || !_used.Add(Convert.ToHexString(digest))) { Fail(); return false; }
            if (_attempt == long.MaxValue) { Fail(); return false; }
            _attempt++; _nonce = nonce; _start = tick; attempt = _attempt; return true;
        }
    }
    public ReplayDisposition TryAccept(long attempt, PeerBinding peer, AuthenticatedSnapshotResponse response)
    {
        lock (_gate)
        {
            // Old callbacks are ignored without changing the current attempt or replay state.
            if (_nonce is null || attempt != _attempt || !_foreground) return ReplayDisposition.Rejected;
            string nonce = _nonce; _nonce = null;
            try
            {
                if (!Current(out var now, out var tick) || !_clock.Before(_start, tick, 5) ||
                    !ProtocolCredentials.PeerMatches(_identity, peer, _peers, now)) return Fail();
                var headers = response.Headers;
                if (headers.GetValueOrDefault("PBC-Protocol") != "1" || headers.GetValueOrDefault("PBC-Request-Nonce") != nonce ||
                    !SecurityHeaders.Positive(headers.GetValueOrDefault("PBC-Authorization-Generation"), out long generation) || generation != _authorization.Generation ||
                    headers.GetValueOrDefault("Content-Length") != response.Body.Length.ToString(CultureInfo.InvariantCulture) ||
                    headers.GetValueOrDefault("Cache-Control") != "no-store" ||
                    headers.GetValueOrDefault("Content-Type") is not ("application/json" or "application/json; charset=utf-8") ||
                    headers.ContainsKey("Transfer-Encoding") || headers.ContainsKey("Content-Encoding")) return Fail();
                byte[] digest = SHA256.HashData(response.Body.AsSpan());
                if (headers.GetValueOrDefault("PBC-Snapshot-SHA256") != Convert.ToHexString(digest).ToLowerInvariant()) return Fail();
                var snapshot = SnapshotWirePolicy.Read(response.Body.AsMemory());
                if (snapshot.GetProperty("synthetic").GetBoolean() ||
                    snapshot.GetProperty("serverInstanceId").GetString() != headers.GetValueOrDefault("PBC-Publisher-Instance")) return Fail();
                Guid instance = Guid.ParseExact(snapshot.GetProperty("serverInstanceId").GetString()!, "D");
                Span<byte> nonceDigest = stackalloc byte[32];
                if (!ProtocolCredentials.TryHashNonce(nonce, nonceDigest) || _history is null ||
                    !_history.TryAdvance(instance, snapshot.GetProperty("revision").GetInt64(), digest, nonceDigest, out var next, out var disposition) || next is null) return Fail();
                if (_journal.CommitReplay(generation, _history.Version, next) != AuthorizationCommitOutcome.Committed) return Fail();
                if (!Current(out _, out long finished) || !_clock.Before(_start, finished, 5)) return Fail();
                _history = next; _visible = response.Body;
                return disposition;
            }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { return Fail(); }
        }
    }
    public ReplayDisposition TryAcceptWire(long attempt, PeerBinding peer, ReadOnlyMemory<byte> body,
        IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        lock (_gate)
        {
            if (_nonce is null || attempt != _attempt || !_foreground) return ReplayDisposition.Rejected;
            try { return TryAccept(attempt, peer, new AuthenticatedSnapshotResponse(body.Span, headers)); }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { return Fail(); }
        }
    }
    private bool Current(out DateTimeOffset now, out long tick)
    {
        if (!_failed && _clock.Read(out now, out tick) && now >= _authorization.IssuedAt && now < _expiry &&
            _clock.Before(_credentialTick, tick, _credentialSeconds))
        {
            var loaded = _journal.Read();
            if (loaded.Outcome == AuthorizationLoadOutcome.VerifiedRecord && loaded.Record is { Revoked: false } current &&
                AuthorizationRecordCodec.Encode(current).AsSpan().SequenceEqual(AuthorizationRecordCodec.Encode(_authorization))) return true;
        }
        now = default; tick = 0; Fail(); return false;
    }
    private ReplayDisposition Fail()
    {
        if (!_failed) { _failed = true; _foreground = false; _nonce = null; _visible = []; try { _journal.MarkUncertain(); } catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { } }
        return ReplayDisposition.Rejected;
    }
}
