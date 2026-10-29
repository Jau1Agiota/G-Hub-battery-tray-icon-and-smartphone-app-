using System.Runtime.InteropServices;
using System.Text;

namespace PeripheralBatteryCompanion.G305Inspector;

public enum ReportIdResult { Available, NotEstablished, LimitExceeded, NativeFailure, Malformed }
public sealed record CapabilityReportIds(int Requested, int Returned, IReadOnlyList<byte> Ids,
    ReportIdResult Result, int? HidStatus = null);
public sealed record DirectionReportIds(CapabilityReportIds Buttons, CapabilityReportIds Values);
public sealed record PassiveReportIds(DirectionReportIds Input, DirectionReportIds Output);

// Descriptor metadata only. No report buffers, report retrieval, or transmission APIs.
internal static class PassiveCapabilityReader
{
    internal const int MaxCapabilities = 256;
    internal static CapabilityReportIds Extract(ushort requested,
        Func<ushort, (int Status, ushort Returned, byte[] Ids)> query)
    {
        if (requested > MaxCapabilities) return new(requested, 0, [], ReportIdResult.LimitExceeded);
        if (requested == 0) return new(0, 0, [], ReportIdResult.NotEstablished);
        var read = query(requested);
        if (read.Status != NativeMethods.HidpStatusSuccess)
            return new(requested, read.Returned, [], ReportIdResult.NativeFailure, read.Status);
        if (read.Returned != requested || read.Ids.Length != read.Returned)
            return new(requested, read.Returned, [], ReportIdResult.Malformed);
        return new(requested, read.Returned, read.Ids.Distinct().Order().ToArray(), ReportIdResult.Available);
    }

    internal static PassiveReportIds Read(SafePreparsedData data, NativeMethods.HidpCaps caps)
    {
        DirectionReportIds Direction(int direction, ushort buttons, ushort values) => new(
            Extract(buttons, count =>
            {
                var buffer = new PassiveMetadataNative.ButtonCaps[count];
                var returned = count;
                var status = PassiveMetadataNative.HidP_GetButtonCaps(direction, buffer, ref returned, data);
                return (status, returned, buffer.Take(Math.Min(returned, count)).Select(c => c.ReportId).ToArray());
            }),
            Extract(values, count =>
            {
                var buffer = new PassiveMetadataNative.ValueCaps[count];
                var returned = count;
                var status = PassiveMetadataNative.HidP_GetValueCaps(direction, buffer, ref returned, data);
                return (status, returned, buffer.Take(Math.Min(returned, count)).Select(c => c.ReportId).ToArray());
            }));
        return new(Direction(0, caps.NumberInputButtonCaps, caps.NumberInputValueCaps),
            Direction(1, caps.NumberOutputButtonCaps, caps.NumberOutputValueCaps));
    }
}

internal static class PassiveMetadataNative
{
    // Windows hidpi.h HIDP_BUTTON_CAPS and HIDP_VALUE_CAPS both occupy 72 bytes,
    // alignment 4, with ReportID at offset 2. Remaining fields/unions are opaque:
    // explicitly sized, zero-initialized native output structs, never decoded as reports.
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    internal struct ButtonCaps { [FieldOffset(2)] public byte ReportId; }
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    internal struct ValueCaps { [FieldOffset(2)] public byte ReportId; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceInfo
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public UIntPtr Reserved;
    }
    [DllImport("hid.dll", ExactSpelling = true)]
    internal static extern int HidP_GetButtonCaps(int reportType, [Out] ButtonCaps[] caps,
        ref ushort count, SafePreparsedData data);
    [DllImport("hid.dll", ExactSpelling = true)]
    internal static extern int HidP_GetValueCaps(int reportType, [Out] ValueCaps[] caps,
        ref ushort count, SafePreparsedData data);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DetailWithDevice(SafeDeviceInfoSet set, ref NativeMethods.DeviceInterfaceData data,
        IntPtr detail, uint detailSize, out uint requiredSize, ref DeviceInfo device);
    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    internal static extern uint CM_Get_Parent(out uint parent, uint child, uint flags);
    [DllImport("cfgmgr32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern uint CM_Get_Device_IDW(uint device, StringBuilder id, uint length, uint flags);
}

internal static class PassiveParentGrouping
{
    internal static bool IsTargetUsbInterface(string id)
    {
        var parts = id.Split('\\');
        if (parts.Length != 3 || !parts[0].Equals("USB", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(parts[2])) return false;
        var tokens = parts[1].Split('&');
        return tokens.Length == 3 && tokens.Contains("VID_046D", StringComparer.OrdinalIgnoreCase) &&
            tokens.Contains("PID_C53F", StringComparer.OrdinalIgnoreCase) && tokens.Contains("MI_02", StringComparer.OrdinalIgnoreCase);
    }

    // Bounded PnP tree metadata walk, not a USB descriptor request. Device IDs stay local.
    internal static uint? Find(uint child)
    {
        for (var depth = 0; depth < 8; depth++)
        {
            if (PassiveMetadataNative.CM_Get_Parent(out var parent, child, 0) != 0 || parent == child) return null;
            var id = new StringBuilder(512);
            if (PassiveMetadataNative.CM_Get_Device_IDW(parent, id, 512, 0) != 0) return null;
            if (IsTargetUsbInterface(id.ToString())) return parent;
            child = parent;
        }
        return null;
    }
}
