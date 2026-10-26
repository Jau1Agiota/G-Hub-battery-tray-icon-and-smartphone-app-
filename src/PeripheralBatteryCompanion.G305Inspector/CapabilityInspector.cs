using System.ComponentModel;

namespace PeripheralBatteryCompanion.G305Inspector;

public sealed class CapabilityInspector(IHidMetadataBackend backend)
{
    public InspectionReport Inspect()
    {
        InterfaceEnumeration enumeration;
        try { enumeration = backend.EnumerateInterfaces(); }
        catch (Win32Exception error)
        {
            return new([], [new(InspectionStep.EnumerateInterfaces, error.NativeErrorCode)]);
        }
        var results = new List<CollectionInspection>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in enumeration.Paths)
        {
            if (!ReceiverFilter.TryMatch(path, out var collection) || !seen.Add(path)) continue;
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
        return new(results, enumeration.Failures);
    }
}
