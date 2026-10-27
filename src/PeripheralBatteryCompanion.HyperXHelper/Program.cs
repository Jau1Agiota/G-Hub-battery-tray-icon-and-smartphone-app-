using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using PeripheralBatteryCompanion.HyperXBatteryPoc;
using PeripheralBatteryCompanion.HyperXHelperProtocol;

namespace PeripheralBatteryCompanion.HyperXHelper;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || !HelperProtocol.ValidNonce(args[1])) return 2; // No argument/default mode opens HID.
        HelperMode? mode = args[0] switch { "--serve" => HelperMode.NativeOneShot,
            "--simulate" => HelperMode.Simulate, "--simulate-hold" => HelperMode.SimulateHold, _ => null };
        if (mode is null) return 2;
        var build = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
        return await HelperServer.RunAsync(Console.In, Console.Out, args[1], mode.Value, build,
            token => ExecuteAsync(args[1], mode.Value, token));
    }

    private static Task<HelperReply> ExecuteAsync(string nonce, HelperMode mode, CancellationToken token)
    {
        if (mode == HelperMode.Simulate) return Task.FromResult(new HelperReply(1, nonce, mode, HelperStatus.SimulationCompleted, NativeCleanupCompleted: true));
        if (mode == HelperMode.SimulateHold) return HoldAsync(nonce); // Bounded no-HID test of explicit forced termination.
        if (!OperatingSystem.IsWindows()) return Task.FromResult(new HelperReply(1, nonce, mode, HelperStatus.Refused));
        return RunNativeAsync(nonce, token);
    }
    private static async Task<HelperReply> HoldAsync(string nonce)
    {
        await Task.Delay(TimeSpan.FromSeconds(30)); // Simulation only; no device/handle/native I/O exists.
        return new(1, nonce, HelperMode.SimulateHold, HelperStatus.SimulationCompleted, NativeCleanupCompleted: true);
    }

    [SupportedOSPlatform("windows")]
    private static async Task<HelperReply> RunNativeAsync(string nonce, CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        using var gate = new Semaphore(1, 1, @"Local\PeripheralBatteryCompanion.HyperXBatteryPoc");
        if (!gate.WaitOne(0)) return new(1, nonce, HelperMode.NativeOneShot, HelperStatus.Busy);
        try
        {
            var observation = InspectNgenuity();
            if (observation != HelperProcessObservation.Running)
                return new(1, nonce, HelperMode.NativeOneShot, HelperStatus.Refused, Ngenuity: observation);
            token.ThrowIfCancellationRequested();
            var factory = new TrackedFactory();
            var result = await new BatteryAttempt(factory).RunAsync(token); // Fixed 3-second exchange after handle setup.
            var cleaned = factory.Session is null;
            if (factory.Session is not null)
            {
                try { await factory.Session.CompletionSafeCleanup.WaitAsync(TimeSpan.FromSeconds(1)); cleaned = true; }
                catch (TimeoutException) { } // Never free pending OVERLAPPED storage to meet the timeout.
            }
            var status = result.Status switch
            {
                AttemptStatus.CandidateReceived => HelperStatus.CandidateObservedAmbiguous,
                AttemptStatus.Timeout => HelperStatus.Timeout, AttemptStatus.Cancelled => HelperStatus.Cancelled,
                AttemptStatus.NoReceiver => HelperStatus.NoReceiver, AttemptStatus.AmbiguousReceiver => HelperStatus.AmbiguousReceiver,
                AttemptStatus.CapabilitiesMismatch => HelperStatus.CapabilitiesMismatch,
                AttemptStatus.BufferedInputBeforeRequest => HelperStatus.BufferedInputBeforeRequest,
                AttemptStatus.InvalidResponse => HelperStatus.InvalidResponse, _ => HelperStatus.NativeFailure
            };
            var stage = result.ErrorStep switch
            {
                BatteryIoStep.Enumerate => HelperStage.Enumerate, BatteryIoStep.Inspect => HelperStage.Inspect,
                BatteryIoStep.OpenReadHandle => HelperStage.OpenReadHandle, BatteryIoStep.ReadInput => HelperStage.ReadInput,
                BatteryIoStep.SubmitOutput => HelperStage.SubmitOutput, _ => HelperStage.Exchange
            };
            return new(1, nonce, HelperMode.NativeOneShot, status, stage, observation,
                factory.ValidatedOpen, factory.ValidatedOpen, result.RequestAttempted, result.OutputCompleted,
                result.ValidatedResponse, result.Win32Error, result.HidStatus, cleaned, elapsed.Elapsed.TotalMilliseconds);
        }
        finally { gate.Release(); }
    }
    private static HelperProcessObservation InspectNgenuity()
    {
        try
        {
            var processes = Process.GetProcesses();
            try
            {
                foreach (var process in processes)
                {
                    try { if (PocName(process.ProcessName)) return HelperProcessObservation.Running; }
                    catch (InvalidOperationException) { }
                }
                return HelperProcessObservation.NotObserved;
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        catch { return HelperProcessObservation.Unknown; }
    }
    private static bool PocName(string name) => name.Contains("ngenuity", StringComparison.OrdinalIgnoreCase);
    [SupportedOSPlatform("windows")]
    private sealed class TrackedFactory : IHyperXBatterySessionFactory
    {
        public WindowsBatterySession? Session;
        public bool ValidatedOpen;
        public IHyperXBatterySession OpenValidatedSession() => Session = (WindowsBatterySession)
            new WindowsBatterySessionFactory(_ => ValidatedOpen = true).OpenValidatedSession();
    }
}

internal static class HelperServer
{
    public static async Task<int> RunAsync(TextReader input, TextWriter output, string nonce, HelperMode mode,
        string build, Func<CancellationToken, Task<HelperReply>> execute)
    {
        using var stop = new CancellationTokenSource();
        try
        {
            await output.WriteLineAsync(HelperProtocol.Encode(new HelperReady(1, nonce, mode, build)));
            await output.FlushAsync();
            using var armDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var frame = await Task.Run(() => HelperProtocol.ReadFrameAsync(input, armDeadline.Token)).WaitAsync(armDeadline.Token);
            if (frame is null) return 2;
            var command = HelperProtocol.Decode<HelperCommand>(frame);
            if (command.Version != 1 || command.Nonce != nonce || command.Action != HelperAction.Start ||
                mode == HelperMode.NativeOneShot && !command.AcknowledgeRisk) return 2;
            // No execution delegate can run before the parent's explicit arm, after Job assignment.
            var control = Task.Run(() => MonitorCancelAsync(input, nonce, stop));
            HelperReply reply;
            try { reply = await Task.Run(() => execute(stop.Token)).WaitAsync(TimeSpan.FromSeconds(12)); }
            catch (OperationCanceledException) { reply = new(1, nonce, mode, HelperStatus.Cancelled); }
            catch { reply = new(1, nonce, mode, HelperStatus.UnexpectedFailure, DeliveryUnknown: true); }
            await output.WriteLineAsync(HelperProtocol.Encode(reply));
            await output.FlushAsync();
            try { await control.WaitAsync(TimeSpan.FromSeconds(1)); }
            catch (TimeoutException) { await stop.CancelAsync(); }
            _ = control.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return 0; // Parent must still observe process exit before accepting any result.
        }
        catch { return 1; } // No raw errors, system paths or stack traces on IPC.
    }
    private static async Task MonitorCancelAsync(TextReader input, string nonce, CancellationTokenSource stop)
    {
        try
        {
            var frame = await HelperProtocol.ReadFrameAsync(input, stop.Token);
            if (frame is not null)
            {
                var command = HelperProtocol.Decode<HelperCommand>(frame);
                if (command.Version != 1 || command.Nonce != nonce || command.Action != HelperAction.Cancel)
                    throw new InvalidDataException("Unexpected command");
            }
        }
        catch { }
        finally { try { await stop.CancelAsync(); } catch (ObjectDisposedException) { } } // EOF/control errors stop; never request again.
    }
}
