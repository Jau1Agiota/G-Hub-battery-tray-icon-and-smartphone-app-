using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using PeripheralBatteryCompanion.Core.SnapshotService;

namespace PeripheralBatteryCompanion.Core.TransportSecurity;

internal static class SecurityHeaders
{
    internal static bool TryRead(IReadOnlyList<KeyValuePair<string, string>> input, out ImmutableDictionary<string, string> headers)
    {
        headers = ImmutableDictionary<string, string>.Empty;
        if (input is null || input.Count is < 1 or > 32) return false;
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase); int bytes = 0;
        int count = input.Count;
        for (int i = 0; i < count; i++)
        {
            var pair = input[i];
            if (pair.Key is null || pair.Value is null || pair.Key.Length is < 1 or > 128 || pair.Value.Length > 8192 ||
                pair.Key.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')) || pair.Value.Any(c => c is < ' ' or > '~') ||
                builder.ContainsKey(pair.Key)) return false;
            bytes += pair.Key.Length + pair.Value.Length + 4;
            if (bytes > 8192) return false;
            builder.Add(pair.Key, pair.Value);
        }
        headers = builder.ToImmutable(); return true;
    }
    internal static bool Positive(string? text, out long value) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0 && value.ToString(CultureInfo.InvariantCulture) == text;
}

public sealed class AuthenticatedSnapshotResponse
{
    public ImmutableArray<byte> Body { get; }
    public ImmutableDictionary<string, string> Headers { get; }
    public AuthenticatedSnapshotResponse(ReadOnlySpan<byte> body, IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (body.Length is < 1 or > 65536 || !SecurityHeaders.TryRead(headers, out var accepted)) throw new ArgumentException("Response bound rejected.");
        Body = ImmutableArray.CreateRange(body.ToArray()); Headers = accepted;
    }
    public override string ToString() => "AuthenticatedSnapshotResponse (redacted)";
}

/// <summary>Bounded non-listening server core. Peer proof is mandatory and deny-by-default; no production transport exists.</summary>
public sealed class AuthenticatedSnapshotCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly EnrollmentIdentity _identity;
    private readonly EnrollmentAuthorization _authorization;
    private readonly IReadOnlyPublishedSnapshot _snapshots;
    private readonly IPeerCertificateVerifier _peers;
    private readonly TimeProvider _time;
    private readonly SecuritySessionClock _clock;
    private readonly HashSet<string> _nonces = new(StringComparer.Ordinal);
    private int _slots, _closed, _globalRequests, _phoneRequests;
    private long? _rateTick;
    public int ActiveResponses => Volatile.Read(ref _slots);
    public AuthenticatedSnapshotCoordinator(EnrollmentIdentity identity, EnrollmentAuthorization authorization,
        IReadOnlyPublishedSnapshot snapshots, TimeProvider? clock = null, IPeerCertificateVerifier? peers = null)
    { _identity = identity; _authorization = authorization; _snapshots = snapshots; _time = clock ?? TimeProvider.System; _clock = new(_time); _peers = peers ?? new DenyUnverifiedPeer(); }

    public int TryPrepare(PeerBinding peer, string method, string path, long bodyLength,
        IReadOnlyList<KeyValuePair<string, string>> headers, out PreparedResponse? prepared)
    {
        prepared = null;
        while (true)
        {
            int count = Volatile.Read(ref _slots);
            if (Volatile.Read(ref _closed) != 0) return 503;
            if (count >= 4) return 429;
            if (Interlocked.CompareExchange(ref _slots, count + 1, count) == count) break;
        }
        bool retained = false;
        try
        {
            long start;
            lock (_gate)
            {
                if (!_clock.Read(out _, out start)) { Suspend(); _authorization.DenyForSecurityFailure(); return 503; }
                if (_rateTick is null || !_clock.Before(_rateTick.Value, start, 60))
                { _rateTick = start; _globalRequests = 0; _phoneRequests = 0; }
                if (_globalRequests >= 60) return 429;
                _globalRequests++;
            }
            if (method is null || method.Length is < 1 or > 8 || path is null || path.Length is < 1 or > 64 || bodyLength != 0 ||
                !SecurityHeaders.TryRead(headers, out var fields) || fields.ContainsKey("Transfer-Encoding") ||
                fields.ContainsKey("Content-Encoding") || (fields.TryGetValue("Content-Length", out var contentLength) && contentLength != "0") ||
                fields.GetValueOrDefault("PBC-Protocol") != "1" || !fields.TryGetValue("PBC-Request-Nonce", out var nonce)) return 400;
            if (method != "GET") return 405;
            if (path != SnapshotRequestCore.SnapshotPath) return 404;
            Span<byte> nonceDigest = stackalloc byte[32];
            if (!ProtocolCredentials.TryHashNonce(nonce, nonceDigest)) return 400;
            if (!_clock.Read(out var now, out _) || !ProtocolCredentials.PeerMatches(_identity, peer, _peers, now)) return 403;
            if (!_authorization.TryAuthenticateBearer(fields.GetValueOrDefault("Authorization"), out var lease) || lease is null) return 401;
            lock (_gate)
            {
                if (_phoneRequests >= 10) return 429;
                _phoneRequests++;
                if (_nonces.Count >= 4096) { Suspend(); return 503; }
                if (!_nonces.Add(Convert.ToHexString(nonceDigest))) return 400;
            }
            var published = _snapshots.ReadLatest();
            if (published is null || published.Snapshot.Synthetic || published.Json.Length is < 1 or > 65536) return 503;
            var response = new AuthenticatedSnapshotResponse(published.Json.AsSpan(), new Dictionary<string, string>
            {
                ["PBC-Protocol"] = "1", ["PBC-Request-Nonce"] = nonce,
                ["PBC-Publisher-Instance"] = published.Snapshot.ServerInstanceId.ToString("D"),
                ["PBC-Authorization-Generation"] = lease.Generation.ToString(CultureInfo.InvariantCulture),
                ["PBC-Snapshot-SHA256"] = Convert.ToHexString(SHA256.HashData(published.Json.AsSpan())).ToLowerInvariant(),
                ["Content-Length"] = published.Json.Length.ToString(CultureInfo.InvariantCulture),
                ["Content-Type"] = "application/json", ["Cache-Control"] = "no-store"
            }.ToArray());
            if (!_clock.Read(out _, out long end) || !_clock.Before(start, end, 5) || !_authorization.RecheckBeforeRelease(lease)) return 401;
            prepared = new(this, response, lease, peer, start); retained = true; return 200;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { Suspend(); return 503; }
        finally { if (!retained) Interlocked.Decrement(ref _slots); }
    }
    private bool Eligible(PreparedResponse response)
    {
        lock (_gate) return Volatile.Read(ref _closed) == 0 && _clock.Read(out var now, out var tick) &&
            _clock.Before(response.Start, tick, 5) && ProtocolCredentials.PeerMatches(_identity, response.Peer, _peers, now) &&
            _authorization.RecheckBeforeRelease(response.Lease);
    }
    private void Suspend() { Interlocked.Exchange(ref _closed, 1); _authorization.DisableExposureByLocalOwner(); }
    private TimeSpan Remaining(PreparedResponse response)
    {
        lock (_gate)
        {
            if (!_clock.Read(out _, out var tick) || !_clock.Before(response.Start, tick, 5)) return TimeSpan.Zero;
            return TimeSpan.FromSeconds(5 - (double)checked(tick - response.Start) / _time.TimestampFrequency);
        }
    }
    public void Dispose() => Suspend();

    public sealed class PreparedResponse : IDisposable
    {
        private readonly AuthenticatedSnapshotCoordinator _owner;
        private readonly CancellationTokenSource _cancel = new();
        private int _state, _released;
        internal EnrollmentAuthorization.ResponseLease Lease { get; }
        internal long Start { get; }
        internal PeerBinding Peer { get; }
        public AuthenticatedSnapshotResponse Response { get; }
        public CancellationToken Revocation => Lease.Stop;
        internal PreparedResponse(AuthenticatedSnapshotCoordinator owner, AuthenticatedSnapshotResponse response,
            EnrollmentAuthorization.ResponseLease lease, PeerBinding peer, long start) { _owner = owner; Response = response; Lease = lease; Peer = peer; Start = start; }
        public async Task<bool> SendAsync(Func<AuthenticatedSnapshotResponse, CancellationToken, Task> writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) return false;
            CancellationTokenSource? deadline = null, linked = null;
            Task? operation = null; bool deferred = false;
            try
            {
                if (!_owner.Eligible(this)) return false;
                var remaining = _owner.Remaining(this);
                if (remaining <= TimeSpan.Zero) return false;
                deadline = new CancellationTokenSource(remaining, _owner._time);
                linked = CancellationTokenSource.CreateLinkedTokenSource(Lease.Stop, _cancel.Token, deadline.Token);
                if (!_owner.Eligible(this) || linked.IsCancellationRequested) return false;
                operation = writer(Response, linked.Token) ?? throw new InvalidOperationException("Writer lifetime missing.");
                await operation.WaitAsync(linked.Token).ConfigureAwait(false);
                return _owner.Eligible(this);
            }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
            {
                _owner.Suspend();
                if (operation is { IsCompleted: false })
                {
                    // Cooperative cancellation cannot prove the writer stopped. Keep its slot
                    // until its real task completes and suspend every further transmission.
                    deferred = true;
                    _ = operation.ContinueWith(task => { if (task.IsFaulted) _ = task.Exception; linked!.Dispose(); deadline!.Dispose(); Release(); },
                        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
                return false;
            }
            finally { if (!deferred) { linked?.Dispose(); deadline?.Dispose(); Release(); } }
        }
        private void Release()
        {
            Interlocked.Exchange(ref _state, 2);
            if (Interlocked.Exchange(ref _released, 1) == 0) { Interlocked.Decrement(ref _owner._slots); _cancel.Dispose(); }
        }
        public void Dispose()
        {
            if (Volatile.Read(ref _released) != 0) return;
            Task cancel;
            try { cancel = _cancel.CancelAsync(); } catch (ObjectDisposedException) { return; }
            _ = cancel.ContinueWith(task => { if (task.IsFaulted) _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0) Release();
        }
    }
}
