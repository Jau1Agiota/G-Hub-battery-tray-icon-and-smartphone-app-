using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PeripheralBatteryCompanion.G305Inspector;
using PassiveNative = PeripheralBatteryCompanion.G305Inspector.NativeMethods;

namespace PeripheralBatteryCompanion.HyperXBatteryPoc;

public static class HyperXReceiverSelection
{
    public static string SelectSinglePath(IEnumerable<string> paths)
    {
        var matches = paths.Where(path => ReceiverFilter.TryMatch(path, HidInspectionTarget.HyperX, out var collection) &&
            collection == ReceiverCollection.Col01).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        if (matches.Length == 0) throw new AttemptRefusedException(AttemptStatus.NoReceiver);
        if (matches.Length > 1) throw new AttemptRefusedException(AttemptStatus.AmbiguousReceiver);
        return matches[0];
    }

    public static bool HasExpectedCapabilities(HidCapabilities caps) => caps.UsagePage == 0xFF13 && caps.Usage == 1 &&
        caps.InputReportBytes == 62 && caps.OutputReportBytes == 62 && caps.FeatureReportBytes == 0;
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsBatterySessionFactory(Action<HidCapabilities>? onValidatedOpen = null) : IHyperXBatterySessionFactory
{
    public IHyperXBatterySession OpenValidatedSession()
    {
        var metadata = new WindowsHidMetadataBackend(HidInspectionTarget.HyperX);
        var interfaces = metadata.EnumerateInterfaces();
        if (interfaces.Failures.Count > 0)
            throw new BatteryIoException(BatteryIoStep.Enumerate, interfaces.Failures[0].Win32Error ?? 13);
        var path = HyperXReceiverSelection.SelectSinglePath(interfaces.Paths);
        var inspected = metadata.ReadCapabilities(path); // Zero-access first; COL02/COL03 never opened in active mode.
        if (inspected.Failure is not null)
            throw new BatteryIoException(BatteryIoStep.Inspect, inspected.Failure.HidStatus ?? inspected.Failure.Win32Error ?? 13, inspected.Failure.HidStatus.HasValue);
        if (inspected.Capabilities is null || !HyperXReceiverSelection.HasExpectedCapabilities(inspected.Capabilities))
            throw new AttemptRefusedException(AttemptStatus.CapabilitiesMismatch);

        // READ is necessary for interrupt input. The output IOCTL is FILE_ANY_ACCESS,
        // so do not request GENERIC_WRITE or try broader permissions if denied.
        var file = PassiveNative.CreateFile(path, ActiveNativeMethods.GenericRead, 3, IntPtr.Zero,
            3, ActiveNativeMethods.FileFlagOverlapped, IntPtr.Zero);
        if (file.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            file.Dispose();
            throw new BatteryIoException(BatteryIoStep.OpenReadHandle, error);
        }
        try
        {
            // Recheck the actual read handle in case the interface changed after metadata enumeration.
            var acquired = PassiveNative.HidD_GetPreparsedData(file, out var data);
            var error = Marshal.GetLastWin32Error();
            using (data)
            {
                if (!acquired || data.IsInvalid) throw new BatteryIoException(BatteryIoStep.Inspect, error);
                var status = PassiveNative.HidP_GetCaps(data, out var caps);
                if (status != PassiveNative.HidpStatusSuccess) throw new BatteryIoException(BatteryIoStep.Inspect, status, true);
                if (caps.UsagePage != 0xFF13 || caps.Usage != 1 || caps.InputReportByteLength != 62 ||
                    caps.OutputReportByteLength != 62 || caps.FeatureReportByteLength != 0)
                    throw new AttemptRefusedException(AttemptStatus.CapabilitiesMismatch);
            }
            onValidatedOpen?.Invoke(inspected.Capabilities);
            return new WindowsBatterySession(file);
        }
        catch { file.Dispose(); throw; }
    }
}
