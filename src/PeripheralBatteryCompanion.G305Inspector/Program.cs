namespace PeripheralBatteryCompanion.G305Inspector;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (!TrySelectTarget(args, out var target))
        {
            Console.WriteLine("Use diagnose-g305.ps1 or diagnose-hyperx.ps1 [-CopySummary]. No device/command/report options are supported.");
            return 2;
        }
        InspectionReport report;
        if (!OperatingSystem.IsWindows()) report = new([], [new(InspectionStep.UnsupportedPlatform)], target);
        else
        {
            try { report = new CapabilityInspector(new WindowsHidMetadataBackend(target, includeReportIds: target == HidInspectionTarget.G305), target).Inspect(); }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            { report = new([], [new(InspectionStep.ApiUnavailable)], target); }
            catch (Exception)
            {
                // An unexpected exception must not dump paths, instance IDs or stack traces.
                report = new([], [new(InspectionStep.UnexpectedResult)], target);
            }
        }
        Console.WriteLine(ReportFormatter.Format(report));
        return report.ExitCode;
    }

    internal static bool TrySelectTarget(string[] args, out HidInspectionTarget target)
    {
        target = HidInspectionTarget.G305;
        if (args.Length == 0) return true;
        if (args.Length != 1 || !string.Equals(args[0], "--hyperx", StringComparison.OrdinalIgnoreCase)) return false;
        target = HidInspectionTarget.HyperX;
        return true;
    }
}
