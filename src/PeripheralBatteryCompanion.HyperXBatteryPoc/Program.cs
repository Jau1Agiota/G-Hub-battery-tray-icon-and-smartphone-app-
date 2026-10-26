using System.Diagnostics;
using System.Runtime.Versioning;
using PeripheralBatteryCompanion.G305Inspector;

namespace PeripheralBatteryCompanion.HyperXBatteryPoc;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var mode = PocPlan.Parse(args);
        if (mode == PocMode.Invalid)
        {
            Console.WriteLine("Default: passive HyperX inspection. Preview: --preview-request. Active: --read-battery AND --confirm-ngenuity-closed. No other modes/arguments.");
            return 2;
        }
        if (mode == PocMode.Preview) { Console.WriteLine(PocFormatter.Preview()); return 0; }
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Windows required; no device inspection or active request attempted.");
            return 2;
        }
        if (mode == PocMode.Inspect)
        {
            try
            {
                var metadata = new CapabilityInspector(new WindowsHidMetadataBackend(HidInspectionTarget.HyperX), HidInspectionTarget.HyperX).Inspect();
                Console.WriteLine(ReportFormatter.Format(metadata));
                return metadata.ExitCode;
            }
            catch { Console.WriteLine("Passive inspection failed; no active request attempted."); return 1; }
        }
        try { return await RunActiveAsync(); }
        catch (Exception error) { return ReportManagedFailure(error); }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> RunActiveAsync()
    {
        Console.WriteLine(PocFormatter.Preview(previewOnly: false)); // Always show the immutable request before native I/O.
        using var gate = new Semaphore(1, 1, @"Local\PeripheralBatteryCompanion.HyperXBatteryPoc");
        if (!gate.WaitOne(0)) { Console.WriteLine(PocFormatter.Format(new(AttemptStatus.Busy))); return 1; }
        try
        {
            var guard = CheckNgenuity();
            if (guard is not null) { Console.WriteLine(PocFormatter.Format(new(guard.Value))); return 1; }
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                var result = await new BatteryAttempt(new WindowsBatterySessionFactory()).RunAsync(cancellation.Token);
                Console.WriteLine(PocFormatter.Format(result));
                return PocFormatter.ExitCode(result.Status);
            }
            catch (Exception error)
            {
                return ReportManagedFailure(error);
            }
            finally { Console.CancelKeyPress -= cancel; }
        }
        finally { gate.Release(); }
    }

    private static int ReportManagedFailure(Exception error)
    {
        Console.WriteLine(PocFormatter.Format(new(AttemptStatus.UnexpectedFailure)));
        Console.WriteLine($"Managed failure type: {error.GetType().Name}; HResult=0x{unchecked((uint)error.HResult):X8}. No raw message/path printed.");
        return 1;
    }

    private static AttemptStatus? CheckNgenuity()
    {
        try
        {
            var processes = Process.GetProcesses();
            try
            {
                foreach (var process in processes)
                {
                    try { if (PocPlan.IsNgenuityName(process.ProcessName)) return AttemptStatus.NgenuityRunning; }
                    catch (InvalidOperationException) { } // Process already exited.
                }
                return null;
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        catch { return AttemptStatus.ProcessCheckFailed; }
    }
}
