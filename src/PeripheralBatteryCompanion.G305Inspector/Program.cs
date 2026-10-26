namespace PeripheralBatteryCompanion.G305Inspector;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            Console.WriteLine("No command/report mode is supported. Use diagnose-g305.ps1 [-CopySummary].");
            return 2;
        }
        InspectionReport report;
        if (!OperatingSystem.IsWindows()) report = new([], [new(InspectionStep.UnsupportedPlatform)]);
        else
        {
            try { report = new CapabilityInspector(new WindowsHidMetadataBackend()).Inspect(); }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            { report = new([], [new(InspectionStep.ApiUnavailable)]); }
            catch (Exception)
            {
                // An unexpected exception must not dump paths, instance IDs or stack traces.
                report = new([], [new(InspectionStep.UnexpectedResult)]);
            }
        }
        Console.WriteLine(ReportFormatter.Format(report));
        return report.ExitCode;
    }
}
