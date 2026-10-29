using System.Text.Json;

namespace PeripheralBatteryCompanion.Core;

public sealed record HyperXMonitoringPreferences(bool Enabled = false, int IntervalSeconds = 120)
{
    public static bool ValidInterval(int seconds) => seconds is 60 or 120 or 300;
    public static HyperXMonitoringPreferences Parse(string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<HyperXMonitoringPreferences>(json);
            return value is not null && ValidInterval(value.IntervalSeconds) ? value : new();
        }
        catch (JsonException) { return new(); }
    }
    public string ToJson() => JsonSerializer.Serialize(this);
}

public enum HyperXCheckOrigin { None, Manual, Scheduled }
public sealed record HyperXMonitoringStatus(bool Enabled, int IntervalSeconds, bool Busy, bool Suspended,
    DateTimeOffset? LastChecked, DateTimeOffset? NextCheck, HyperXCheckOrigin Origin, int ConsecutiveFailures,
    string State);

// One owner for manual, scheduled and reconnect requests. Tick is metadata-only until the due instant.
// Scheduling is completion-relative: missed ticks never queue or catch up. No HID or UI dependencies.
public sealed class HyperXAutomaticMonitor(HyperXBatteryProvider provider, TimeProvider? clock = null) : IAsyncDisposable
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _flight = new(1, 1);
    private readonly object _sync = new();
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource? _operation;
    private Task? _shutdown;
    private bool _enabled, _busy, _suspended, _disposed;
    private int _interval = 120, _failures;
    private DateTimeOffset? _next, _checked, _retryNotBefore;
    private HyperXCheckOrigin _origin;
    private string _state = "Automatic monitoring off.";
    private ReceiverPresence _lastReceiver = ReceiverPresence.Unknown;
    public event Action? Changed;
    public HyperXMonitoringStatus Status
    {
        get { lock (_sync) return new(_enabled, _interval, _busy, _suspended, _checked, _next, _origin, _failures, _state); }
    }

    // A persisted preference never grants acknowledgement or starts native work. Readiness is a separate session gate.
    public void Configure(bool enabled, int intervalSeconds)
    {
        if (!HyperXMonitoringPreferences.ValidInterval(intervalSeconds)) throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
        lock (_sync)
        {
            if (_disposed) return;
            _enabled = enabled; _interval = intervalSeconds;
            _next = enabled && !_suspended && provider.CanQuery ? DueAfterInterval() : null;
            _state = !enabled ? "Automatic monitoring off." : _suspended ? "Paused; explicit Resume required."
                : !provider.ExperimentalEnabled ? "Waiting for safe monitoring initialization." : "Waiting for validated receiver and helper readiness.";
            if (!enabled) _operation?.Cancel();
        }
    }

    public void RestoreFailureState(int failures, DateTimeOffset? retryNotBefore, bool suspended)
    {
        lock (_sync)
        {
            _failures = Math.Clamp(failures, 0, 4); _retryNotBefore = retryNotBefore;
            _suspended = suspended || _failures >= 4;
        }
    }

    public bool Resume()
    {
        lock (_sync)
        {
            if (_disposed || _busy || !provider.ExperimentalEnabled) return false;
            if (!provider.CanQuery && !provider.TryResumeAfterSafeFailure()) return false;
            _suspended = false; _failures = 0; _retryNotBefore = null;
            _next = _enabled && provider.CanQuery ? _clock.GetUtcNow().AddSeconds(_interval) : null;
            _state = "Resumed; next request waits a full interval.";
            return true;
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            _suspended = true; _next = null; _state = "Paused by cancellation; explicit Resume required.";
            _operation?.Cancel();
        }
    }

    public async Task RefreshMetadataAsync(CancellationToken token = default)
    {
        if (!await _flight.WaitAsync(0, token)) return;
        try
        {
            lock (_sync) { if (_disposed) return; }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
            await provider.ReadDeviceAsync(linked.Token).ConfigureAwait(false);
        }
        finally { _flight.Release(); }
    }

    public Task TickAsync(CancellationToken token = default) => RunAsync(false, token);
    public Task QueryManuallyAsync(CancellationToken token = default) => RunAsync(true, token);
    // Coordinator uses this once per session after discovery and real preflight. Same flight/safety path.
    public Task QueryInitialAsync(CancellationToken token = default) => RunAsync(false, token, initial: true);

    private async Task RunAsync(bool manual, CancellationToken token, bool initial = false)
    {
        if (!await _flight.WaitAsync(0, token)) return; // Drop overlaps, including metadata refresh/reconnect ticks.
        CancellationTokenSource? operation = null;
        try
        {
            lock (_sync)
            {
                if (_disposed) return;
                operation = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
                _operation = operation;
            }
            await provider.ReadDeviceAsync(operation.Token).ConfigureAwait(false);
            bool shouldQuery;
            lock (_sync)
            {
                var now = _clock.GetUtcNow(); var receiver = provider.Current.Receiver;
                var ready = receiver == ReceiverPresence.Detected && provider.CanQuery;
                if (!ready)
                {
                    _next = null;
                    if (_enabled && !_suspended) _state = "Waiting for receiver/session readiness; no query scheduled.";
                }
                else if (_enabled && !_suspended && (_lastReceiver is not ReceiverPresence.Detected and not ReceiverPresence.Unknown || _next is null))
                {
                    // Reconnect is never an immediate query, and must not shorten an existing failure delay.
                    var due = DueAfterInterval();
                    if (_next is null || _next < due) _next = due;
                }
                _lastReceiver = receiver;
                if (_enabled && !_suspended && ready && _next is null && provider.CanQuery)
                    _next = DueAfterInterval();
                var retryEligible = _retryNotBefore is { } retry ? now >= retry : _failures == 0;
                shouldQuery = ready && !_suspended && (manual ? retryEligible : _enabled && (initial ? retryEligible : _next <= now));
                if (!shouldQuery) return;
                if (!provider.CanQuery && !provider.TryResumeAfterSafeFailure())
                {
                    Suspend("Safety gate blocked; restart/review required. Resume cannot override containment failure."); return;
                }
                _busy = true; _origin = manual ? HyperXCheckOrigin.Manual : HyperXCheckOrigin.Scheduled;
                _checked = now; _next = null; _state = "Checking battery candidate…";
            }
            Changed?.Invoke();
            await provider.QueryOnceAsync(operation.Token).ConfigureAwait(false);
            lock (_sync)
            {
                if (_disposed) return;
                _busy = false;
                if (operation.IsCancellationRequested || _suspended)
                {
                    Suspend("Paused after cancellation; late replies are not scheduled. Explicit Resume required."); return;
                }
                if (provider.LastOutcome == HyperXQueryOutcome.CandidateObserved &&
                    provider.Current.ObservationState == BatteryObservationState.CandidateAmbiguous && provider.CanQuery)
                {
                    _failures = 0; _retryNotBefore = null; _state = "Candidate observed (ambiguous); headset connectivity unknown.";
                    _next = _enabled ? _clock.GetUtcNow().AddSeconds(_interval) : null;
                }
                else
                {
                    _failures++;
                    var safeRefusal = provider.Diagnostic.Stage == HyperXStage.ProcessGuard && provider.CanQuery;
                    if (!safeRefusal && (provider.LastExchangeDiagnostic?.CompletionSafeCleanup != true || !provider.TryResumeAfterSafeFailure()))
                    { Suspend("Safety/cleanup failure; querying disabled. Review diagnostics before restarting."); return; }
                    if (_failures >= 4) { Suspend("Paused after four consecutive failures; explicit Resume required."); return; }
                    var delay = _failures switch { 1 => 120, 2 => 300, _ => 900 };
                    _state = $"Check failed; conservative backoff {delay / 60} minutes. No immediate retry.";
                    _retryNotBefore = _clock.GetUtcNow().AddSeconds(delay);
                    _next = _enabled ? _retryNotBefore : null;
                }
            }
        }
        catch (OperationCanceledException)
        {
            lock (_sync) { if (!_disposed) Suspend("Paused after cancellation; explicit Resume required."); }
        }
        catch
        {
            lock (_sync) { if (!_disposed) Suspend("Monitoring failed; no automatic fallback. Review diagnostics."); }
        }
        finally
        {
            lock (_sync) { _busy = false; _operation = null; }
            operation?.Dispose(); _flight.Release();
            Changed?.Invoke();
        }
    }
    private DateTimeOffset DueAfterInterval()
    {
        var due = _clock.GetUtcNow().AddSeconds(_interval);
        return _retryNotBefore is { } retry && retry > due ? retry : due;
    }
    public void ReceiverRemoved()
    {
        lock (_sync) { _next = null; _lastReceiver = ReceiverPresence.Absent; _operation?.Cancel(); }
        provider.MarkReceiverAbsent();
    }
    private void Suspend(string reason) { _suspended = true; _next = null; _state = reason; }
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_shutdown is not null) return new(_shutdown);
            _disposed = true; _enabled = false; _next = null;
            _stop.Cancel();
            return new(_shutdown = DrainAsync());
        }
    }
    private async Task DrainAsync()
    {
        await _flight.WaitAsync().ConfigureAwait(false);
        _flight.Release(); // Provider owns transport disposal; caller awaits both in order.
    }
}
