namespace PeripheralBatteryCompanion.G305Inspector;

public enum HyperXMetadataPresence { Absent, Detected, Ambiguous, InspectionFailed }
public sealed record HyperXMetadataResult(HyperXMetadataPresence Presence, InspectionFailure? Failure = null);

// Shared pure selection/capability rules for the diagnostic and production metadata paths.
public static class HyperXCollectionRules
{
    public static string[] TargetPaths(IEnumerable<string> paths) => paths.Where(path =>
        ReceiverFilter.TryMatch(path, HidInspectionTarget.HyperX, out var col) && col == ReceiverCollection.Col01)
        .Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
    public static bool ExpectedCapabilities(HidCapabilities caps) => caps is
        { UsagePage: 0xFF13, Usage: 1, InputReportBytes: 62, OutputReportBytes: 62, FeatureReportBytes: 0 };

    public static HyperXMetadataResult Inspect(IHidMetadataBackend backend)
    {
        var interfaces = backend.EnumerateInterfaces();
        if (interfaces.Failures.Count > 0) return new(HyperXMetadataPresence.InspectionFailed, interfaces.Failures[0]);
        var paths = TargetPaths(interfaces.Paths);
        if (paths.Length != 1) return new(paths.Length == 0 ? HyperXMetadataPresence.Absent : HyperXMetadataPresence.Ambiguous);
        var read = backend.ReadCapabilities(paths[0]);
        if (read.Failure is not null) return new(HyperXMetadataPresence.InspectionFailed, read.Failure);
        return new(read.Capabilities is not null && ExpectedCapabilities(read.Capabilities)
            ? HyperXMetadataPresence.Detected : HyperXMetadataPresence.InspectionFailed);
    }
}
