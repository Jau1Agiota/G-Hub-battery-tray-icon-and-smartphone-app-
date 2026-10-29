using System.Collections.Immutable;
using System.Text;
using PeripheralBatteryCompanion.Core.Snapshots;

namespace PeripheralBatteryCompanion.Core.SnapshotService;

// No record-generated ToString containing a credential. This request is transport-adapter input, NOT evidence of real TLS.
public sealed class SnapshotRequest(string method, string path, string? authorizationHeader, bool isTls, long bodyLength = 0)
{
    internal string Method { get; } = method;
    internal string Path { get; } = path;
    internal string? Authorization { get; } = authorizationHeader;
    internal bool IsTls { get; } = isTls;
    internal long BodyLength { get; } = bodyLength;
    public override string ToString() => "SnapshotRequest (credentials redacted)";
}
public sealed record SnapshotResponse(int StatusCode, ImmutableArray<byte> Body, int? RetryAfterSeconds = null)
{
    public string ContentType => "application/json";
    public string CacheControl => "no-store";
}

/// <summary>Non-listening synchronous response construction. It does not implement TLS, pairing or transport timeouts.</summary>
public sealed class SnapshotRequestCore(IReadOnlyPublishedSnapshot snapshots, ApprovedPhoneRegistry phones,
    TimeProvider? clock = null) : IDisposable
{
    public const string SnapshotPath = "/v1/battery-snapshot";
    public const int GlobalRequestsPerMinute = 60;
    public const int PhoneRequestsPerMinute = 10;
    private readonly object _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<Guid, int> _phoneRequests = [];
    private long? _window;
    private int _globalRequests;
    private bool _enabled, _stopped;

    public bool EnableByLocalOwner()
    {
        lock (_gate) { if (_stopped) return false; _enabled = true; return true; }
    }
    public void DisableByLocalOwner() { lock (_gate) _enabled = false; }
    public SnapshotResponse Handle(SnapshotRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (_stopped || !_enabled || cancellationToken.IsCancellationRequested) return Error(503, "service_unavailable");
            var now = _clock.GetTimestamp();
            if (_window is null || _clock.GetElapsedTime(_window.Value, now) >= TimeSpan.FromMinutes(1))
            { _window = now; _globalRequests = 0; _phoneRequests.Clear(); }
            if (_globalRequests >= GlobalRequestsPerMinute) return Error(429, "rate_limited", 60);
            _globalRequests++;
            if (!request.IsTls) return Error(403, "secure_transport_required");
            if (request.Method is null || request.Method.Length > 8 || request.Path is null || request.Path.Length > 64 || request.BodyLength != 0)
                return Error(400, "invalid_request");
            return phones.WithAuthorization(request.Authorization, id => Authorized(id, request, cancellationToken), Error(401, "unauthorized"));
        }
    }
    private SnapshotResponse Authorized(Guid id, SnapshotRequest request, CancellationToken stop)
    {
        var count = _phoneRequests.GetValueOrDefault(id);
        if (count >= PhoneRequestsPerMinute) return Error(429, "rate_limited", 60);
        _phoneRequests[id] = count + 1;
        if (request.Method != "GET") return Error(405, "method_not_allowed");
        if (request.Path != SnapshotPath) return Error(404, "not_found");
        if (stop.IsCancellationRequested) return Error(503, "service_unavailable");
        PublishedSnapshot? published;
        try { published = snapshots.ReadLatest(); }
        catch { return Error(503, "snapshot_unavailable"); }
        if (published is null) return Error(503, "snapshot_unavailable");
        if (published.Snapshot.Synthetic || published.Json.Length > BatterySnapshotJson.MaxPayloadBytes)
            return Error(503, "snapshot_unavailable");
        return new(200, published.Json);
    }
    private static SnapshotResponse Error(int status, string code, int? retry = null) =>
        new(status, ImmutableArray.CreateRange(Encoding.UTF8.GetBytes("{\"error\":\"" + code + "\"}")), retry);
    public void Dispose() { lock (_gate) { _stopped = true; _enabled = false; _phoneRequests.Clear(); } }
}
