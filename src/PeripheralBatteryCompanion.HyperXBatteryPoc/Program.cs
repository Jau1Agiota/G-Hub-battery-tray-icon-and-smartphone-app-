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
            Console.WriteLine("Default: passive HyperX inspection. Passive shared-access probe: --passive-coexistence. Preview: --preview-request. Active: --read-battery AND --confirm-ngenuity-closed. Coexistence: --active-coexistence AND --acknowledge-coexistence-risk. No other modes/arguments.");
            return 2;
        }
        if (mode == PocMode.Preview) { Console.WriteLine(PocFormatter.Preview()); return 0; }
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Windows required; no device inspection or active request attempted.");
            return 2;
        }
        if (mode == PocMode.PassiveCoexistence)
        {
            try
            {
                var result = new PassiveCoexistence(new WindowsHidMetadataBackend(HidInspectionTarget.HyperX),
                    new WindowsPassiveSharedHandleProbe()).Inspect();
                Console.WriteLine(PassiveCoexistence.Format(result));
                return result.ExitCode;
            }
            catch (Exception error)
            {
                Console.WriteLine($"Passive coexistence inspection failed; HResult=0x{unchecked((uint)error.HResult):X8}. No report I/O attempted; owned handles disposed.");
                return 1;
            }
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
        try { return await RunActiveAsync(mode); }
        catch (Exception error) { return ReportManagedFailure(error, mode == PocMode.ActiveCoexistence); }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> RunActiveAsync(PocMode mode)
    {
        var coexistence = mode == PocMode.ActiveCoexistence;
        var elapsed = Stopwatch.StartNew();
        Console.WriteLine($"Mode: {mode}; target 03F0:05B7/MI_03/COL01; expected FF13:0001, reports=62/62/0.");
        Console.WriteLine("Handle requirements: GENERIC_READ=0x80000000; share READ|WRITE=0x00000003; OVERLAPPED=0x40000000; OPEN_EXISTING=3. No access fallback.");
        try { return await RunActiveCoreAsync(mode, coexistence); }
        finally { Console.WriteLine($"Active invocation elapsed ms: {elapsed.Elapsed.TotalMilliseconds:F1} (includes guards/metadata/open/cleanup initiation; exchange deadline remains 3000 ms)."); }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> RunActiveCoreAsync(PocMode mode, bool coexistence)
    {
        Console.WriteLine(PocFormatter.Preview(previewOnly: false, coexistence: coexistence)); // Always show the immutable request before native I/O.
        using var global = new Semaphore(1, 1, HyperXHelperProtocol.HelperProtocol.GlobalQueryGate);
        if (!global.WaitOne(0)) { Console.WriteLine(PocFormatter.Format(new(AttemptStatus.Busy), coexistence)); return 1; }
        try { return await RunWithLocalGateAsync(coexistence); }
        finally { global.Release(); }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> RunWithLocalGateAsync(bool coexistence)
    {
        var mode = coexistence ? PocMode.ActiveCoexistence : PocMode.ReadBattery;
        using var gate = new Semaphore(1, 1, @"Local\PeripheralBatteryCompanion.HyperXBatteryPoc");
        if (!gate.WaitOne(0)) { Console.WriteLine(PocFormatter.Format(new(AttemptStatus.Busy), coexistence)); return 1; }
        try
        {
            var observed = CheckNgenuity();
            Console.WriteLine($"NGENUITY process observation: {observed?.ToString() ?? "NotObserved"}; process names alone cannot identify every helper.");
            var guard = PocPlan.ActiveGuard(mode, observed);
            if (guard is not null) { Console.WriteLine(PocFormatter.Format(new(guard.Value), coexistence)); return 1; }
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                var result = await new BatteryAttempt(new WindowsBatterySessionFactory(caps =>
                {
                    Console.WriteLine($"Shared read handle opened; actual handle capability gate passed: usage={caps.UsagePage:X4}:{caps.Usage:X4}, input={caps.InputReportBytes}, output={caps.OutputReportBytes}, feature={caps.FeatureReportBytes}.");
                    Console.WriteLine($"Capability counts buttons/values/data indices: input={caps.Input.Buttons}/{caps.Input.Values}/{caps.Input.DataIndices}; output={caps.Output.Buttons}/{caps.Output.Values}/{caps.Output.DataIndices}; feature={caps.Feature.Buttons}/{caps.Feature.Values}/{caps.Feature.DataIndices}; links={caps.LinkCollectionNodes}.");
                })).RunAsync(cancellation.Token);
                Console.WriteLine(PocFormatter.Format(result, coexistence));
                return PocFormatter.ExitCode(result.Status);
            }
            catch (Exception error)
            {
                return ReportManagedFailure(error, coexistence);
            }
            finally { Console.CancelKeyPress -= cancel; }
        }
        finally { gate.Release(); }
    }

    private static int ReportManagedFailure(Exception error, bool coexistence = false)
    {
        Console.WriteLine(PocFormatter.Format(new(AttemptStatus.UnexpectedFailure), coexistence));
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
