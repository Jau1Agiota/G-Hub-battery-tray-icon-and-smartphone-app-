using System.ComponentModel;

namespace PeripheralBatteryCompanion.G305Inspector;

public sealed class CapabilityInspector(IHidMetadataBackend backend, HidInspectionTarget? target = null)
{
    private readonly HidInspectionTarget _target = target ?? HidInspectionTarget.G305;

    public InspectionReport Inspect()
    {
        InterfaceEnumeration enumeration;
        try { enumeration = backend.EnumerateInterfaces(); }
        catch (Win32Exception error)
        {
            return new([], [new(InspectionStep.EnumerateInterfaces, error.NativeErrorCode)], _target);
        }
        var results = new List<CollectionInspection>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<(string Path, ReceiverCollection Collection)>();
        foreach (var path in enumeration.Paths)
            if (ReceiverFilter.TryMatch(path, _target, out var collection) && seen.Add(path)) matches.Add((path, collection));
        // Preserve the G305 inspection order; HyperX's vendor collection is the first research target.
        var ordered = _target == HidInspectionTarget.HyperX ? matches.OrderBy(item => item.Collection) : matches.AsEnumerable();
        foreach (var (path, collection) in ordered)
        {
            try
            {
                var read = backend.ReadCapabilities(path);
                results.Add(new(collection, read.Capabilities, read.Failure ??
                    (read.Capabilities is null ? new(InspectionStep.UnexpectedResult) : null)));
            }
            catch (Win32Exception error)
            {
                // Do not include error.Message: it can contain a device path/instance ID.
                results.Add(new(collection, null, new(InspectionStep.ReadCollection, error.NativeErrorCode)));
            }
        }
        return new(results, enumeration.Failures, _target);
    }
}
