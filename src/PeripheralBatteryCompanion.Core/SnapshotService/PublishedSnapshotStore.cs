using System.Collections.Immutable;
using PeripheralBatteryCompanion.Core.Snapshots;

namespace PeripheralBatteryCompanion.Core.SnapshotService;

public sealed class PublishedSnapshot
{
    internal PublishedSnapshot(BatterySnapshotV1 snapshot, byte[] bytes)
    { Snapshot = snapshot; Json = ImmutableArray.CreateRange(bytes); }
    public BatterySnapshotV1 Snapshot { get; }
    public ImmutableArray<byte> Json { get; }
}

/// <summary>The request boundary has read access only: no projection, provider or refresh methods.</summary>
public interface IReadOnlyPublishedSnapshot { PublishedSnapshot? ReadLatest(); }

/// <summary>Explicit producer publication only. Reads are immutable, atomic and do not serialize or change revisions.</summary>
public sealed class PublishedSnapshotStore(Guid instanceId, TimeProvider? clock = null) : IReadOnlyPublishedSnapshot, IDisposable
{
    private readonly object _gate = new();
    private readonly Guid _instanceId = instanceId != Guid.Empty ? instanceId : throw new ArgumentException("Instance identity required.");
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private PublishedSnapshot? _latest;
    private long _revision;
    private bool _closed;
    public Guid InstanceId => _instanceId;
    public PublishedSnapshot? ReadLatest() => Volatile.Read(ref _latest);
    public PublishedSnapshot Publish(BatterySnapshotV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Synthetic) throw new ArgumentException("Synthetic snapshots cannot be published as live data.");
        BatterySnapshotJson.Validate(input);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            var next = checked(_revision + 1);
            var snapshot = input with { ServerInstanceId = _instanceId, Revision = next, CapturedAt = _clock.GetUtcNow().UtcDateTime };
            var published = new PublishedSnapshot(snapshot, BatterySnapshotJson.Serialize(snapshot));
            _revision = next;
            Volatile.Write(ref _latest, published);
            return published;
        }
    }
    public void Dispose()
    {
        lock (_gate) { _closed = true; Volatile.Write(ref _latest, null); }
    }
}

/// <summary>Future existing-cache-event seam. No event subscription, monitoring startup or I/O is performed here.</summary>
public sealed class CachedSnapshotPublisher(PublishedSnapshotStore store,
    LogitechSnapshotOrigin origin = LogitechSnapshotOrigin.Gateway) : IDisposable
{
    private readonly object _gate = new();
    private BatteryMonitoringState? _logitech;
    private BatteryDevice? _hyperX;
    private bool _closed;
    public void OnLogitechCacheChanged(BatteryMonitoringState value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate) { ObjectDisposedException.ThrowIf(_closed, this); Publish(value, _hyperX); _logitech = value; }
    }
    public void OnHyperXCacheChanged(BatteryDevice value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate) { ObjectDisposedException.ThrowIf(_closed, this); Publish(_logitech, value); _hyperX = value; }
    }
    private void Publish(BatteryMonitoringState? logitech, BatteryDevice? hyperX) =>
        store.Publish(BatterySnapshotProjection.Build(logitech, hyperX, new(store.InstanceId, 0, null), origin));
    public void Dispose() { lock (_gate) { _closed = true; _logitech = null; _hyperX = null; } }
}
