using System.Diagnostics;
using System.ComponentModel;
using PeripheralBatteryCompanion.HyperXHelperProtocol;

namespace PeripheralBatteryCompanion.Core;

public interface IHyperXHelperProcess : IAsyncDisposable
{
    bool Contained { get; }
    int? StartupWin32Error => null;
    int? ExitCode { get; }
    Task<string?> ReadFrameAsync(CancellationToken token);
    Task SendAsync(HelperCommand command, CancellationToken token);
    void CloseInput();
    void RequestTermination();
    Task<bool> WaitForExitAsync(TimeSpan timeout);
    void RetainUntilExit(); // Transfer an unconfirmed child to the factory's owned quarantine; never abandon it.
}
public interface IHyperXHelperProcessFactory
{
    bool IsAvailable { get; }
    Task<IHyperXHelperProcess> StartAsync(HelperMode mode, string nonce, CancellationToken token);
}
public interface IHyperXTransportStatus
{
    string SafetyBlocker { get; }
    string Diagnostics { get; }
}

public sealed class HyperXHelperTransport(IHyperXHelperProcessFactory factory) : IHyperXManualQueryTransport, IHyperXTransportStatus
{
    private static readonly SemaphoreSlim GlobalFlight = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _lifetime = new();
    private Task? _active, _shutdown;
    private volatile bool _ready, _stopped;
    private string _diagnostics = "Helper not prepared; no query attempted.";
    public bool HasVerifiedLongRunningLifetime => _ready && !_stopped && !_stop.IsCancellationRequested && factory.IsAvailable;
    public TimeSpan InvocationTimeout => TimeSpan.FromSeconds(20); // Child exchange is separately fixed at 3 s.
    public string Diagnostics => Volatile.Read(ref _diagnostics);
    public string SafetyBlocker => _stopped ? "Helper stopped after failure; review diagnostics before restarting for any further test."
        : !factory.IsAvailable ? "Production helper missing, unsupported or startup still owned; queries disabled."
        : !HasVerifiedLongRunningLifetime ? "Helper containment/exit preflight required after session acknowledgement." : "";

    public Task<bool> PrepareAsync(bool acknowledgeRisk, CancellationToken token = default)
    {
        lock (_lifetime)
        {
            if (!acknowledgeRisk || _shutdown is not null || _stopped || !factory.IsAvailable || _active is { IsCompleted: false })
                return Task.FromResult(false);
            var task = Task.Run(() => PrepareCoreAsync(token)); _active = task; return task;
        }
    }
    private async Task<bool> PrepareCoreAsync(CancellationToken token)
    {
        _ready = false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        if (!await GlobalFlight.WaitAsync(0, linked.Token)) return false;
        try
        {
            var normal = await RunChildAsync(HelperMode.Simulate, linked.Token);
            if (!normal.Cleanup || normal.Forced || normal.Reply?.Status != HelperStatus.SimulationCompleted) return false;
            // A deliberately unresponsive, bounded simulation tests actual Job termination/observed exit without HID.
            var terminated = await RunChildAsync(HelperMode.SimulateHold, linked.Token);
            _ready = terminated.Cleanup && terminated.Forced && !linked.IsCancellationRequested;
            return _ready;
        }
        finally { GlobalFlight.Release(); }
    }
    public Task<HyperXQueryResult> QueryOnceAsync(CancellationToken token)
    {
        lock (_lifetime)
        {
            if (!HasVerifiedLongRunningLifetime || _shutdown is not null || _active is { IsCompleted: false })
                return Task.FromResult(new HyperXQueryResult(HyperXQueryOutcome.Unavailable, CompletionSafeCleanup: true));
            var task = Task.Run(() => QueryCoreAsync(token)); _active = task; return task;
        }
    }
    private async Task<HyperXQueryResult> QueryCoreAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        if (!await GlobalFlight.WaitAsync(0, linked.Token)) return new(HyperXQueryOutcome.Unavailable, CompletionSafeCleanup: true);
        try
        {
            var run = await RunChildAsync(HelperMode.NativeOneShot, linked.Token);
            var reply = run.Reply;
            if (!run.Cleanup || run.Forced || reply is null || linked.IsCancellationRequested)
            {
                _stopped = true;
                return new(linked.IsCancellationRequested ? HyperXQueryOutcome.Cancelled : HyperXQueryOutcome.Failed,
                    RequestAttempted: run.Armed, CompletionSafeCleanup: run.Cleanup);
            }
            var outcome = reply.Status switch
            {
                HelperStatus.CandidateObservedAmbiguous => HyperXQueryOutcome.CandidateObserved,
                HelperStatus.NativeFailure when reply.Stage == HelperStage.OpenReadHandle && reply.Win32Error is 5 or 32 => HyperXQueryOutcome.AccessDenied,
                HelperStatus.Timeout => HyperXQueryOutcome.Timeout, HelperStatus.Cancelled => HyperXQueryOutcome.Cancelled,
                HelperStatus.InvalidResponse or HelperStatus.BufferedInputBeforeRequest => HyperXQueryOutcome.InvalidResponse,
                HelperStatus.NoReceiver or HelperStatus.AmbiguousReceiver or HelperStatus.CapabilitiesMismatch or HelperStatus.Refused or HelperStatus.Busy
                    => HyperXQueryOutcome.Unavailable,
                _ => HyperXQueryOutcome.Failed
            };
            int? percentage = null;
            if (outcome == HyperXQueryOutcome.CandidateObserved) HelperProtocol.ValidateResponse(reply.Response!, out percentage);
            else _stopped = true;
            ReceiverPresence? receiver = reply.Status switch
            {
                HelperStatus.NoReceiver => ReceiverPresence.Absent,
                HelperStatus.AmbiguousReceiver => ReceiverPresence.Ambiguous,
                HelperStatus.CapabilitiesMismatch => ReceiverPresence.InspectionFailed,
                _ => null
            };
            return new(outcome, percentage, reply.DeliveryUnknown ? run.Armed : reply.RequestAttempted, reply.OutputCompleted,
                reply.Win32Error, reply.HidStatus, run.Cleanup, receiver);
        }
        finally { GlobalFlight.Release(); }
    }

    private sealed record RunResult(HelperReply? Reply, bool Cleanup, bool Forced, bool Armed);
    private async Task<RunResult> RunChildAsync(HelperMode mode, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var nonce = Guid.NewGuid().ToString("N");
        IHyperXHelperProcess? child = null;
        Task<IHyperXHelperProcess>? starting = null;
        HelperReply? reply = null;
        string build = "unknown";
        bool armed = false, exited = false, forced = false;
        int? supervisorError = null;
        try
        {
            using var setup = CancellationTokenSource.CreateLinkedTokenSource(token);
            setup.CancelAfter(TimeSpan.FromSeconds(5));
            starting = Observe(factory.StartAsync(mode, nonce, setup.Token));
            child = await starting.WaitAsync(setup.Token).ConfigureAwait(false);
            if (!child.Contained) throw new Win32Exception(child.StartupWin32Error ?? 13);
            var ready = HelperProtocol.Decode<HelperReady>(await Observe(child.ReadFrameAsync(setup.Token)).WaitAsync(setup.Token) ?? throw new EndOfStreamException());
            if (!HelperProtocol.ValidReady(ready, nonce, mode)) throw new InvalidDataException("Invalid ready frame");
            build = ready.Build;
            token.ThrowIfCancellationRequested();
            armed = true; // Once submission is requested, delivery is uncertain if IPC/process fails.
            await Observe(child.SendAsync(new(1, nonce, HelperAction.Start, mode == HelperMode.NativeOneShot), setup.Token)).WaitAsync(setup.Token);
            if (mode == HelperMode.SimulateHold)
            {
                // The no-HID child intentionally ignores cancel; exercise forced job exit, without a timed sleep.
                throw new OperationCanceledException("Containment preflight");
            }
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(token);
            operation.CancelAfter(TimeSpan.FromSeconds(8)); // metadata/setup + 3-second native exchange + cleanup.
            reply = HelperProtocol.Decode<HelperReply>(await Observe(child.ReadFrameAsync(operation.Token)).WaitAsync(operation.Token) ?? throw new EndOfStreamException());
            if (!HelperProtocol.ValidReply(reply, nonce, mode)) throw new InvalidDataException("Invalid result frame");
            child.CloseInput(); // EOF lets the helper join its control reader and exit normally.
            exited = await SafeExitAsync(child, TimeSpan.FromSeconds(1));
            if (exited && child.ExitCode != 0) reply = null;
        }
        catch (Exception error) { supervisorError = (error as Win32Exception)?.NativeErrorCode; reply = null; } // No raw paths/messages.
        finally
        {
            if (child is not null)
            {
                if (!exited)
                {
                    try
                    {
                        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                        await Observe(child.SendAsync(new(1, nonce, HelperAction.Cancel), cancel.Token)).WaitAsync(cancel.Token);
                        child.CloseInput();
                    }
                    catch { }
                    exited = await SafeExitAsync(child, TimeSpan.FromSeconds(1));
                    if (!exited)
                    {
                        forced = true;
                        try { child.RequestTermination(); } catch (Win32Exception error) { supervisorError ??= error.NativeErrorCode; } catch { }
                        exited = await SafeExitAsync(child, TimeSpan.FromSeconds(2));
                    }
                }
                if (!exited) { child.RetainUntilExit(); _stopped = true; }
                if (exited && child.ExitCode != 0) reply = null;
                try { await child.DisposeAsync(); }
                catch { child.RetainUntilExit(); exited = false; _stopped = true; }
            }
            var cleanup = exited ? forced ? "forced exit observed" : "normal exit observed" : child is null ? starting is { IsCompleted: false } ? "startup unconfirmed; cancelled creation remains factory-owned" : "no active helper returned" : "exit unconfirmed; retained under owned Job";
            Volatile.Write(ref _diagnostics,
                $"Helper protocol=1; build={build}; mode={mode}; target={HelperProtocol.Target}; " +
                "request=66 89 + 60 zeros (62 bytes); access=GENERIC_READ (0x80000000), share=READ|WRITE (3), OVERLAPPED; " +
                $"NGENUITY={reply?.Ngenuity.ToString() ?? "unknown"}; caps gate={reply?.CapabilitiesValid}; shared open={reply?.SharedOpenSucceeded}; " +
                $"status={reply?.Status.ToString() ?? "no validated result"}; stage={reply?.Stage.ToString() ?? "IPC/supervision"}; supervisor Win32={supervisorError}; " +
                $"Win32={reply?.Win32Error}, HID={reply?.HidStatus}; arm attempted={armed && mode == HelperMode.NativeOneShot}; " +
                $"output completion confirmed={reply?.OutputCompleted == true}; delivery={((reply is null || reply.DeliveryUnknown) && armed && mode == HelperMode.NativeOneShot ? "unknown" : "see attempt flags")}; " +
                $"response validated={reply?.Status == HelperStatus.CandidateObservedAmbiguous}; response header={(reply?.Status == HelperStatus.CandidateObservedAmbiguous ? "66 89" : "unvalidated")}; ownership=ambiguous; " +
                $"helper elapsed={reply?.ElapsedMilliseconds:F1} ms; native cleanup={reply?.NativeCleanupCompleted}; termination requested={forced}; cleanup={cleanup}; exit={child?.ExitCode}; elapsed={watch.Elapsed.TotalMilliseconds:F1} ms.");
        }
        return new(reply, exited, forced, armed && mode == HelperMode.NativeOneShot);
    }
    private static T Observe<T>(T task) where T : Task
    {
        _ = task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }
    private static async Task<bool> SafeExitAsync(IHyperXHelperProcess child, TimeSpan timeout)
    {
        try { return await Observe(child.WaitForExitAsync(timeout)).WaitAsync(timeout); }
        catch { return false; }
    }
    public ValueTask DisposeAsync()
    {
        lock (_lifetime) return new(_shutdown ??= StopAsync());
    }
    private async Task StopAsync()
    {
        _ready = false;
        await _stop.CancelAsync();
        var active = _active;
        if (active is not null) { try { await active; } catch { } }
    }
}
