namespace PeripheralBatteryCompanion.G305GHubInspector;

public static class InspectorEntry
{
    public static int Execute(Func<InspectionReport> inspect, TextWriter output)
    {
        InspectionReport report;
        try { report = inspect(); }
        catch { report = new(InspectionStatus.UnexpectedFailure); }
        output.WriteLine(report.Format());
        return report.ExitCode;
    }
}
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 0 || !OperatingSystem.IsWindows())
        { Console.WriteLine("G305 G HUB inspection: Windows and no arguments required. No database or hardware inspection started."); return 2; }
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LGHUB", "settings.db");
        return InspectorEntry.Execute(() => DatabaseInspection.Read(path), Console.Out);
    }
}
