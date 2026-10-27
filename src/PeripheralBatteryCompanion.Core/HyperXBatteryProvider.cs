using System.Diagnostics;

namespace PeripheralBatteryCompanion.Core;

public enum BatteryObservationState { Unknown, Unavailable, Stale, CandidateAmbiguous, ConfirmedFresh }
public enum ReceiverPresence { Unknown, Absent, Detected, Ambiguous, InspectionFailed }
public enum NgenuityPresence { Unknown, NotObserved, Running }
public enum HyperXQueryOutcome { CandidateObserved, Timeout, Cancelled, AccessDenied, InvalidResponse, Unavailable, Failed }
public enum HyperXStage { Idle, Discovery, OptIn, LifetimeGate, ProcessGuard, Busy, Exchange, Stopped, Disposed }

public sealed record HyperXDiscoveryResult(ReceiverPresence Presence, int? Win32Error = null, int? HidStatus = null);
// A transport must validate the entire fixed-length 66/89 response before returning a candidate.
public sealed record HyperXQueryResult(HyperXQueryOutcome Outcome, int? CandidatePercentage = null,
    bool RequestAttempted = false, bool OutputCompleted = false, int? Win32Error = null, int? HidStatus = null,
    bool CompletionSafeCleanup = false);
public sealed record HyperXDiagnostic(HyperXStage Stage, bool RequestAttempted = false, bool OutputCompleted = false,
    int? Win32Error = null, int? HidStatus = null, double ElapsedMilliseconds = 0,
    bool CompletionSafeCleanup = true, bool ResponseOwnershipAmbiguous = true);

public interface IDeviceBatteryProvider
{
    Task<BatteryDevice> ReadDeviceAsync(CancellationToken token = default);
}
public interface IHyperXReceiverDiscovery
{
    Task<HyperXDiscoveryResult> DiscoverAsync(CancellationToken token);
}
public interface INgenuityPresenceProbe
{
    Task<NgenuityPresence> InspectAsync(CancellationToken token);
}
// No production implementation is wired: the standalone POC lifetime is not sufficient proof.
// Implementations must keep native storage alive until completion, even after task cancellation.
public interface IHyperXManualQueryTransport : IAsyncDisposable
{
    bool HasVerifiedLongRunningLifetime { get; }
    Task<HyperXQueryResult> QueryOnceAsync(CancellationToken token);
}

public sealed class HyperXBatteryProvider(IHyperXReceiverDiscovery discovery, INgenuityPresenceProbe processes,
    IHyperXManualQueryTransport? transport = null, TimeProvider? clock = null) : IDeviceBatteryProvider, IAsyncDisposable
{
    public const string LifetimeBlocker = "Active queries blocked: native I/O lifetime has not been validated for a long-running tray process.";
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _lifetime = new();
    private Task? _shutdown;
    private volatile bool _experimentalEnabled, _attemptsStopped;
    private BatteryDevice _current = new("HyperX Cloud III Wireless", null, null, BatteryAvailability.Unavailable,
        "Receiver not inspected. Battery not queried.", Source: "HID capability metadata; no battery query");
    public BatteryDevice Current => Volatile.Read(ref _current);
    public HyperXDiagnostic Diagnostic { get; private set; } = new(HyperXStage.Idle);
    public HyperXDiagnostic? LastExchangeDiagnostic { get; private set; }
    public bool ExperimentalEnabled => _experimentalEnabled;
    public bool CanQuery => !_stop.IsCancellationRequested && !_attemptsStopped && _experimentalEnabled &&
        transport?.HasVerifiedLongRunningLifetime == true && Current.Receiver == ReceiverPresence.Detected;
    public string QueryBlocker => _stop.IsCancellationRequested ? "Provider stopped." : _attemptsStopped
        ? "Further attempts stopped after a failure; review diagnostics before any new physical test."
        : transport?.HasVerifiedLongRunningLifetime != true ? LifetimeBlocker
        : !_experimentalEnabled ? "Experimental active queries are disabled." : Current.Receiver != ReceiverPresence.Detected
        ? "Exactly one validated receiver is required." : "";

    // Session-only setting. Never enabled implicitly or persisted across application restarts.
    public bool ConfigureExperimentalQueries(bool enabled, bool acknowledgeInterferenceRisk = false)
    {
        _experimentalEnabled = enabled && acknowledgeInterferenceRisk && !_stop.IsCancellationRequested;
        return _experimentalEnabled;
    }

    public async Task<BatteryDevice> ReadDeviceAsync(CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        if (!await _gate.WaitAsync(0, linked.Token)) return Current;
        try
        {
            var result = await discovery.DiscoverAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            ApplyDiscovery(result);
            Diagnostic = new(HyperXStage.Discovery, Win32Error: result.Win32Error, HidStatus: result.HidStatus);
            return Current;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            ApplyDiscovery(new(ReceiverPresence.InspectionFailed));
            Diagnostic = new(HyperXStage.Discovery);
            return Current;
        }
        finally { _gate.Release(); }
    }

    private void ApplyDiscovery(HyperXDiscoveryResult result)
    {
        var previous = Current;
        var stale = previous.Percentage.HasValue && (result.Presence != ReceiverPresence.Detected ||
            previous.Receiver != ReceiverPresence.Detected || previous.ObservationState == BatteryObservationState.Stale);
        var state = stale ? BatteryObservationState.Stale : result.Presence != ReceiverPresence.Detected
            ? BatteryObservationState.Unavailable : previous.Percentage.HasValue
            ? BatteryObservationState.CandidateAmbiguous : BatteryObservationState.Unknown;
        var status = result.Presence switch
        {
            ReceiverPresence.Detected => "Receiver detected; headset connectivity unknown. No automatic battery queries.",
            ReceiverPresence.Absent => "Receiver not observed; battery unavailable.",
            ReceiverPresence.Ambiguous => "Multiple matching receivers; no target selected.",
            _ => "Receiver inspection unavailable; no target selected."
        };
        Volatile.Write(ref _current, previous with { Receiver = result.Presence, ObservationState = state,
            Availability = state == BatteryObservationState.CandidateAmbiguous ? BatteryAvailability.Available : BatteryAvailability.Unavailable,
            Status = status });
    }

    public async Task<BatteryDevice> QueryOnceAsync(CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        if (!await _gate.WaitAsync(0, linked.Token)) { Diagnostic = new(HyperXStage.Busy); return Current; }
        var elapsed = Stopwatch.StartNew();
        bool enteringTransport = false;
        Task<HyperXQueryResult>? exchange = null;
        try
        {
            if (_attemptsStopped) return Refuse(HyperXStage.Stopped, QueryBlocker);
            if (!_experimentalEnabled) return Refuse(HyperXStage.OptIn, "Experimental active queries are disabled.");
            if (transport?.HasVerifiedLongRunningLifetime != true) return Refuse(HyperXStage.LifetimeGate, LifetimeBlocker);
            ApplyDiscovery(await discovery.DiscoverAsync(linked.Token));
            linked.Token.ThrowIfCancellationRequested();
            if (Current.Receiver != ReceiverPresence.Detected) return Refuse(HyperXStage.Discovery, Current.Status);
            var presence = await processes.InspectAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (presence != NgenuityPresence.Running)
                return Refuse(HyperXStage.ProcessGuard, "Concurrent query refused: NGENUITY must be observed running; process state alone cannot prove every helper's state.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            enteringTransport = true;
            exchange = transport.QueryOnceAsync(deadline.Token);
            var result = await exchange.WaitAsync(deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            RecordExchange(new(HyperXStage.Exchange, result.RequestAttempted, result.OutputCompleted,
                result.Win32Error, result.HidStatus, elapsed.Elapsed.TotalMilliseconds, result.CompletionSafeCleanup));
            if (result.Outcome == HyperXQueryOutcome.CandidateObserved && result.CandidatePercentage is >= 0 and <= 100 &&
                result.RequestAttempted && result.OutputCompleted && result.CompletionSafeCleanup)
            {
                Volatile.Write(ref _current, Current with { Percentage = result.CandidatePercentage,
                    LastReported = _clock.GetUtcNow(), Availability = BatteryAvailability.Available,
                    ObservationState = BatteryObservationState.CandidateAmbiguous,
                    Source = "Experimental one-shot HID; response ownership ambiguous",
                    Status = "Candidate observed; NGENUITY may have requested it. Freshness and headset connectivity unknown." });
                return Current;
            }
            return StopAfterFailure(result.Outcome == HyperXQueryOutcome.CandidateObserved
                ? "Candidate rejected: invalid value, incomplete output or unverified cleanup. No retry."
                : $"Experimental query stopped: {result.Outcome}; no retry scheduled.");
        }
        catch (OperationCanceledException)
        {
            RecordExchange(new(HyperXStage.Exchange, RequestAttempted: enteringTransport,
                ElapsedMilliseconds: elapsed.Elapsed.TotalMilliseconds, CompletionSafeCleanup: !enteringTransport));
            StopAfterFailure(linked.IsCancellationRequested ? "Query cancelled; delivery may have occurred. No retry." : "Query timed out; delivery may have occurred. No retry.");
            return Current;
        }
        catch
        {
            RecordExchange(new(HyperXStage.Exchange, RequestAttempted: enteringTransport,
                ElapsedMilliseconds: elapsed.Elapsed.TotalMilliseconds, CompletionSafeCleanup: !enteringTransport));
            return StopAfterFailure("Query failed; delivery/cleanup may be uncertain. No fallback or retry.");
        }
        finally
        {
            if (exchange is not null) _ = exchange.ContinueWith(completed => { _ = completed.Exception; },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            _gate.Release();
        }
    }

    private void RecordExchange(HyperXDiagnostic value)
    {
        Diagnostic = value;
        LastExchangeDiagnostic = value; // Preserve numeric failure/ownership details across later metadata refreshes.
    }

    private BatteryDevice Refuse(HyperXStage stage, string reason)
    {
        Diagnostic = new(stage);
        Volatile.Write(ref _current, Current with { Status = reason });
        return Current;
    }
    private BatteryDevice StopAfterFailure(string reason)
    {
        _attemptsStopped = true;
        Volatile.Write(ref _current, Current with { Status = reason, Availability = BatteryAvailability.Unavailable,
            ObservationState = Current.Percentage.HasValue ? BatteryObservationState.Stale : BatteryObservationState.Unavailable });
        return Current;
    }
    public ValueTask DisposeAsync()
    {
        lock (_lifetime) return new(_shutdown ??= StopAsync());
    }
    private async Task StopAsync()
    {
        _experimentalEnabled = false;
        await _stop.CancelAsync();
        await _gate.WaitAsync();
        try { if (transport is not null) await transport.DisposeAsync(); }
        finally { _gate.Release(); }
        // Keep cancellation/gate objects alive so late calls cancel instead of touching disposed primitives.
    }
}
