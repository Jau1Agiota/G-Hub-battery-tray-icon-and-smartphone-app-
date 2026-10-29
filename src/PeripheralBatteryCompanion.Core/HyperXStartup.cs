using System.Text.Json;

namespace PeripheralBatteryCompanion.Core;

public sealed record HyperXStartupPreferences(bool UserMonitoringEnabled = true, int IntervalSeconds = 120,
    int Version = 2, bool SafetyLatched = false, bool Suspended = false, int Failures = 0, DateTimeOffset? RetryNotBefore = null)
{
    public static HyperXStartupPreferences Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 4096) return new();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return new();
            var root = doc.RootElement;
            bool Bool(string name, bool fallback) => root.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : fallback;
            // Legacy files were written on UI edits, but intent is not perfectly knowable. Preserve any explicit false field.
            var enabled = Bool("UserMonitoringEnabled", Bool("Enabled", true));
            var interval = root.TryGetProperty("IntervalSeconds", out var i) && i.ValueKind == JsonValueKind.Number && i.TryGetInt32(out var seconds) &&
                HyperXMonitoringPreferences.ValidInterval(seconds) ? seconds : 120;
            var failures = root.TryGetProperty("Failures", out var f) && f.ValueKind == JsonValueKind.Number && f.TryGetInt32(out var count) ? Math.Clamp(count, 0, 4) : 0;
            DateTimeOffset? retry = root.TryGetProperty("RetryNotBefore", out var t) && t.ValueKind == JsonValueKind.String &&
                t.TryGetDateTimeOffset(out var time) ? time : null;
            return new(enabled, interval, SafetyLatched: Bool("SafetyLatched", false), Suspended: Bool("Suspended", false), Failures: failures, RetryNotBefore: retry);
        }
        catch (JsonException) { return new(); }
        catch (InvalidOperationException) { return new(); }
    }
    public string ToJson() => JsonSerializer.Serialize(this);
}

public interface IHyperXStartupStore
{
    HyperXStartupPreferences Load();
    bool Save(HyperXStartupPreferences value);
}

public sealed class HyperXStartupState(IHyperXStartupStore store)
{
    private readonly object _sync = new();
    private HyperXStartupPreferences _current = store.Load();
    public HyperXStartupPreferences Current { get { lock (_sync) return _current; } }
    public bool Update(Func<HyperXStartupPreferences, HyperXStartupPreferences> change)
    {
        lock (_sync)
        {
            var next = change(_current);
            if (next == _current) return true;
            try { if (!store.Save(next)) return false; }
            catch { return false; }
            _current = next; return true;
        }
    }
}

// Durable crash marker wraps the existing transport; it neither implements nor duplicates native I/O.
public sealed class HyperXDurableTransport(IHyperXManualQueryTransport inner, HyperXStartupState state, TimeProvider? clock = null)
    : IHyperXManualQueryTransport, IHyperXTransportStatus
{
    private volatile bool _latched = state.Current.SafetyLatched;
    public bool HasVerifiedLongRunningLifetime => !_latched && inner.HasVerifiedLongRunningLifetime;
    public TimeSpan InvocationTimeout => inner.InvocationTimeout;
    public string SafetyBlocker => _latched ? "Persistent safety stop; review diagnostics before owner-authorized recovery."
        : (inner as IHyperXTransportStatus)?.SafetyBlocker ?? "Transport unavailable.";
    public string Diagnostics => (inner as IHyperXTransportStatus)?.Diagnostics ?? "";
    public async Task<HyperXQueryResult> QueryOnceAsync(CancellationToken token)
    {
        if (!HasVerifiedLongRunningLifetime || !state.Update(p => p with { SafetyLatched = true }))
        { _latched = true; return new(HyperXQueryOutcome.Failed, CompletionSafeCleanup: true); }
        try
        {
            var result = await inner.QueryOnceAsync(token).ConfigureAwait(false);
            if (!result.CompletionSafeCleanup || !inner.HasVerifiedLongRunningLifetime)
            { _latched = true; return result; }
            var candidate = result.Outcome == HyperXQueryOutcome.CandidateObserved && result.CandidatePercentage is >= 0 and <= 100;
            // Persist worst-case ordinary backoff before returning; scheduler subsequently records its exact 2/5/15 delay.
            if (!state.Update(p => p with { SafetyLatched = false, Failures = candidate ? 0 : Math.Min(4, p.Failures + 1),
                RetryNotBefore = candidate ? null : (clock ?? TimeProvider.System).GetUtcNow().AddMinutes(15) })) _latched = true;
            return _latched ? result with { Outcome = HyperXQueryOutcome.Failed, CandidatePercentage = null } : result;
        }
        catch { _latched = true; throw; } // Durable marker intentionally remains set.
    }
    public ValueTask DisposeAsync() => inner.DisposeAsync();
}

public enum HyperXStartupPhase { UserDisabled, ReceiverUnavailable, ReceiverAmbiguousOrIncompatible, PreflightPending,
    WaitingForInterval, Querying, CandidateAmbiguous, Backoff, RecoverableSuspended, SafetyLatched }

public sealed class HyperXStartupCoordinator : IAsyncDisposable
{
    private readonly HyperXBatteryProvider _provider;
    private readonly HyperXAutomaticMonitor _monitor;
    private readonly Func<CancellationToken, Task<bool>> _prepare;
    private readonly HyperXStartupState? _state;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _configuration = new();
    private int _configurationVersion;
    private readonly CancellationTokenSource _stop = new();
    private volatile bool _prepared, _preflightAttempted, _latched, _disposed, _preflightInProgress;
    private Task? _shutdown;
    private bool _initialReadPending = true; // Only the first eligible attempt bypasses the normal interval.
    private HyperXStartupPreferences _ephemeral = new();
    public HyperXStartupPreferences Preferences => _state?.Current ?? _ephemeral;
    public HyperXStartupCoordinator(HyperXBatteryProvider provider, HyperXAutomaticMonitor monitor,
        Func<CancellationToken, Task<bool>> prepare, HyperXStartupState? state = null)
    {
        _provider = provider; _monitor = monitor; _prepare = prepare; _state = state;
        _latched = Preferences.SafetyLatched;
        _monitor.RestoreFailureState(Preferences.Failures, Preferences.RetryNotBefore, Preferences.Suspended);
        _monitor.Configure(Preferences.UserMonitoringEnabled, Preferences.IntervalSeconds);
    }
    public HyperXStartupPhase Phase => !Preferences.UserMonitoringEnabled ? HyperXStartupPhase.UserDisabled
        : _preflightInProgress ? HyperXStartupPhase.PreflightPending
        : _latched || Preferences.SafetyLatched && !_monitor.Status.Busy ? HyperXStartupPhase.SafetyLatched
        : _provider.Current.Receiver == ReceiverPresence.Absent || _provider.Current.Receiver == ReceiverPresence.Unknown ? HyperXStartupPhase.ReceiverUnavailable
        : _provider.Current.Receiver != ReceiverPresence.Detected ? HyperXStartupPhase.ReceiverAmbiguousOrIncompatible
        : !_prepared ? HyperXStartupPhase.PreflightPending : _monitor.Status.Busy ? HyperXStartupPhase.Querying
        : _monitor.Status.Suspended ? HyperXStartupPhase.RecoverableSuspended
        : _monitor.Status.ConsecutiveFailures > 0 ? HyperXStartupPhase.Backoff
        : _provider.Current.ObservationState == BatteryObservationState.CandidateAmbiguous ? HyperXStartupPhase.CandidateAmbiguous
        : HyperXStartupPhase.WaitingForInterval;
    public Task StartAsync(CancellationToken token = default) => TickAsync(token);
    public Task TickAsync(CancellationToken token = default) => RunAsync(false, token);
    public Task QueryManuallyAsync(CancellationToken token = default) => RunAsync(true, token);
    public async Task<bool> ConfigureAsync(bool enabled, int interval, CancellationToken token = default)
    {
        if (!HyperXMonitoringPreferences.ValidInterval(interval)) throw new ArgumentOutOfRangeException(nameof(interval));
        lock (_configuration)
        {
            var wasEnabled = Preferences.UserMonitoringEnabled;
            if (!Update(p => p with { UserMonitoringEnabled = enabled, IntervalSeconds = interval }))
            { _latched = true; _provider.ConfigureMonitoring(false); _monitor.Cancel(); return false; }
            _configurationVersion++;
            if (!enabled) { _provider.ConfigureMonitoring(false); _monitor.Configure(false, interval); _prepared = false; }
            else
            {
                if (!wasEnabled && !_latched && !Preferences.SafetyLatched) _preflightAttempted = false;
                _monitor.Configure(true, interval);
            }
        }
        if (enabled) await TickAsync(token).ConfigureAwait(false);
        return true;
    }
    private async Task RunAsync(bool manual, CancellationToken token)
    {
        if (!await _gate.WaitAsync(0, token)) return;
        try
        {
            if (_disposed) return;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
            if (!Preferences.UserMonitoringEnabled || _latched || Preferences.SafetyLatched)
            { await _monitor.RefreshMetadataAsync(linked.Token).ConfigureAwait(false); return; }
            if (!_prepared)
            {
                await _monitor.RefreshMetadataAsync(linked.Token).ConfigureAwait(false);
                if (_provider.Current.Receiver != ReceiverPresence.Detected || _preflightAttempted) return;
                var configurationVersion = Volatile.Read(ref _configurationVersion);
                _preflightAttempted = true;
                if (!Update(p => p with { SafetyLatched = true })) { Latch(); return; }
                _preflightInProgress = true;
                bool ready;
                try { ready = await _prepare(linked.Token).ConfigureAwait(false); }
                finally { _preflightInProgress = false; }
                if (!ready || !Update(p => p with { SafetyLatched = false })) { Latch(); return; }
                lock (_configuration)
                {
                    if (linked.IsCancellationRequested || !Preferences.UserMonitoringEnabled || configurationVersion != _configurationVersion)
                    { _preflightAttempted = false; return; }
                    _prepared = true;
                    _provider.ConfigureMonitoring(true);
                    _monitor.Configure(true, Preferences.IntervalSeconds); // Normal cadence begins at readiness; first eligible read is separate.
                }
            }
            if (manual) await _monitor.QueryManuallyAsync(linked.Token).ConfigureAwait(false);
            else if (_initialReadPending) await _monitor.QueryInitialAsync(linked.Token).ConfigureAwait(false);
            else await _monitor.TickAsync(linked.Token).ConfigureAwait(false);
            if (_monitor.Status.LastChecked.HasValue) _initialReadPending = false;
            PersistSchedule();
        }
        catch (OperationCanceledException) { if (!_disposed) { _monitor.Cancel(); PersistSchedule(); } }
        catch { Latch(); }
        finally { _gate.Release(); }
    }
    public void Cancel() { _monitor.Cancel(); PersistSchedule(); }
    public bool Resume()
    {
        if (_latched || Preferences.SafetyLatched || !_monitor.Resume()) return false;
        _initialReadPending = false; // Explicit Resume retains its existing full-interval contract.
        PersistSchedule(); return true;
    }
    private void PersistSchedule()
    {
        var status = _monitor.Status;
        if (!Update(p => p with { Failures = status.ConsecutiveFailures, RetryNotBefore = status.ConsecutiveFailures > 0 ? status.NextCheck ?? p.RetryNotBefore : null,
            Suspended = status.Suspended })) Latch();
    }
    private bool Update(Func<HyperXStartupPreferences, HyperXStartupPreferences> change)
    {
        if (_state is not null) return _state.Update(change);
        _ephemeral = change(_ephemeral); return true;
    }
    private void Latch() { _latched = true; _provider.ConfigureMonitoring(false); _monitor.Cancel(); Update(p => p with { SafetyLatched = true }); }
    public ValueTask DisposeAsync()
    {
        lock (_stop) return new(_shutdown ??= DrainAsync());
    }
    private async Task DrainAsync()
    {
        _disposed = true; await _stop.CancelAsync();
        await _gate.WaitAsync().ConfigureAwait(false); _gate.Release();
    }
}
