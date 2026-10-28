namespace PeripheralBatteryCompanion.G305Inspector;

public enum ReceiverCollection { Col01 = 1, Col02 = 2, Col03 = 3 }
public enum InspectionStep
{
    EnumerateInterfaces, InterfaceDetail, OpenInterface, GetPreparsedData, GetCapabilities,
    ReadCollection, UnsupportedPlatform, ApiUnavailable, UnexpectedResult
}

// Only numeric capability metadata and fixed identifiers reach the report model.
// Raw interface paths/instance identifiers never enter a clipboard/report object.
public sealed record InspectionFailure(InspectionStep Step, int? Win32Error = null, int? HidStatus = null);
public sealed record ReportCapabilityCounts(ushort Buttons, ushort Values, ushort DataIndices);
public sealed record HidCapabilities(ushort UsagePage, ushort Usage,
    ushort InputReportBytes, ushort OutputReportBytes, ushort FeatureReportBytes,
    ushort LinkCollectionNodes, ReportCapabilityCounts Input,
    ReportCapabilityCounts Output, ReportCapabilityCounts Feature, PassiveReportIds? ReportIds = null);
public sealed record CollectionInspection(ReceiverCollection Collection,
    HidCapabilities? Capabilities, InspectionFailure? Failure, int? ParentGroup = null);
public sealed record InterfaceEnumeration(IReadOnlyList<string> Paths, IReadOnlyList<InspectionFailure> Failures, IReadOnlyDictionary<string, int>? ParentGroups = null);
public sealed record CapabilityRead(HidCapabilities? Capabilities, InspectionFailure? Failure);

public interface IHidMetadataBackend
{
    InterfaceEnumeration EnumerateInterfaces();
    CapabilityRead ReadCapabilities(string interfacePath);
}

public sealed record InspectionReport(IReadOnlyList<CollectionInspection> Collections,
    IReadOnlyList<InspectionFailure> EnumerationFailures, HidInspectionTarget? Target = null)
{
    public HidInspectionTarget EffectiveTarget => Target ?? HidInspectionTarget.G305;
    public int ExitCode => EnumerationFailures.Any(error => error.Step == InspectionStep.UnsupportedPlatform) ? 2 :
        EnumerationFailures.Count > 0 || Collections.Any(item => item.Failure is not null || item.Capabilities is null ||
            (item.Capabilities.ReportIds is { } ids && new[] { ids.Input.Buttons, ids.Input.Values, ids.Output.Buttons, ids.Output.Values }
                .Any(c => c.Result is ReportIdResult.NativeFailure or ReportIdResult.Malformed or ReportIdResult.LimitExceeded))) ? 1 :
        Collections.Count == 0 ? 3 : 0;
}
