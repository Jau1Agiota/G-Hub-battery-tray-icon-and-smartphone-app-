using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PeripheralBatteryCompanion.G305Inspector;

[SupportedOSPlatform("windows")]
public sealed class WindowsHidMetadataBackend : IHidMetadataBackend
{
    public InterfaceEnumeration EnumerateInterfaces()
    {
        var paths = new List<string>();
        var failures = new List<InspectionFailure>();
        NativeMethods.HidD_GetHidGuid(out var guid);
        // DIGCF_PRESENT | DIGCF_DEVICEINTERFACE; no registry/device property changes.
        using var set = NativeMethods.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
        if (set.IsInvalid) return new(paths, [new(InspectionStep.EnumerateInterfaces, Marshal.GetLastWin32Error())]);
        for (uint index = 0; ; index++)
        {
            var data = new NativeMethods.DeviceInterfaceData { Size = (uint)Marshal.SizeOf<NativeMethods.DeviceInterfaceData>() };
            if (!NativeMethods.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != NativeMethods.ErrorNoMoreItems) failures.Add(new(InspectionStep.EnumerateInterfaces, error));
                break;
            }
            var queried = NativeMethods.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
            var queryError = Marshal.GetLastWin32Error();
            if (!queried && queryError != NativeMethods.ErrorInsufficientBuffer)
            {
                failures.Add(new(InspectionStep.InterfaceDetail, queryError));
                continue;
            }
            if (size < NativeMethods.DetailHeaderSize || size > int.MaxValue)
            {
                failures.Add(new(InspectionStep.InterfaceDetail, NativeMethods.ErrorInvalidData));
                continue;
            }
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                Marshal.WriteInt32(buffer, NativeMethods.DetailHeaderSize);
                if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, size, out _, IntPtr.Zero))
                {
                    failures.Add(new(InspectionStep.InterfaceDetail, Marshal.GetLastWin32Error()));
                    continue;
                }
                // Bound the UTF-16 read to the allocated detail buffer, including its terminator.
                var text = Marshal.PtrToStringUni(IntPtr.Add(buffer, NativeMethods.DetailPathOffset),
                    ((int)size - NativeMethods.DetailPathOffset) / 2);
                var terminator = text?.IndexOf('\0') ?? -1;
                if (terminator <= 0) failures.Add(new(InspectionStep.InterfaceDetail, NativeMethods.ErrorInvalidData));
                else paths.Add(text![..terminator]);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return new(paths, failures);
    }

    public CapabilityRead ReadCapabilities(string interfacePath)
    {
        // Enforce scope even for direct callers, before opening any device.
        if (!ReceiverFilter.TryMatch(interfacePath, out _))
            return new(null, new(InspectionStep.OpenInterface, NativeMethods.ErrorInvalidData));

        // Exactly zero desired access. FILE_SHARE_READ | FILE_SHARE_WRITE, OPEN_EXISTING.
        // Do not fall back to GENERIC_READ/WRITE or change permissions on failure.
        using var file = NativeMethods.CreateFile(interfacePath, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (file.IsInvalid) return new(null, new(InspectionStep.OpenInterface, Marshal.GetLastWin32Error()));
        var acquired = NativeMethods.HidD_GetPreparsedData(file, out var preparsed);
        var error = Marshal.GetLastWin32Error();
        using (preparsed)
        {
            if (!acquired || preparsed.IsInvalid) return new(null, new(InspectionStep.GetPreparsedData, error));
            var status = NativeMethods.HidP_GetCaps(preparsed, out var caps);
            if (status != NativeMethods.HidpStatusSuccess) return new(null, new(InspectionStep.GetCapabilities, HidStatus: status));
            return new(new(caps.UsagePage, caps.Usage, caps.InputReportByteLength,
                caps.OutputReportByteLength, caps.FeatureReportByteLength, caps.NumberLinkCollectionNodes,
                new(caps.NumberInputButtonCaps, caps.NumberInputValueCaps, caps.NumberInputDataIndices),
                new(caps.NumberOutputButtonCaps, caps.NumberOutputValueCaps, caps.NumberOutputDataIndices),
                new(caps.NumberFeatureButtonCaps, caps.NumberFeatureValueCaps, caps.NumberFeatureDataIndices)), null);
        }
    }
}
