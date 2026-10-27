using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PeripheralBatteryCompanion.G305Inspector;

internal static class NativeMethods
{
    internal const int HidpStatusSuccess = 0x00110000;
    internal const int ErrorInsufficientBuffer = 122;
    internal const int ErrorNoMoreItems = 259;
    internal const int ErrorInvalidData = 13;
    internal static int DetailHeaderSize => IntPtr.Size == 8 ? 8 : 6;
    internal const int DetailPathOffset = 4; // DWORD then WCHAR[]; cbSize is 8 on x64, not the path offset.

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceInterfaceData
    {
        public uint Size;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HidpCaps
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[]? Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll", ExactSpelling = true)]
    internal static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeDeviceInfoSet SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInterfaces(SafeDeviceInfoSet set, IntPtr device, ref Guid guid, uint index, ref DeviceInterfaceData data);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInterfaceDetail(SafeDeviceInfoSet set, ref DeviceInterfaceData data,
        IntPtr detail, uint detailSize, out uint requiredSize, IntPtr deviceInfo);

    [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFile(string path, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint disposition, uint flags, IntPtr template);

    [DllImport("hid.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool HidD_GetPreparsedData(SafeFileHandle file, out SafePreparsedData data);

    [DllImport("hid.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool HidD_FreePreparsedData(IntPtr data);

    [DllImport("hid.dll", ExactSpelling = true)]
    internal static extern int HidP_GetCaps(SafePreparsedData data, out HidpCaps caps);
}

internal sealed class SafeDeviceInfoSet : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeDeviceInfoSet() : base(true) { }
    protected override bool ReleaseHandle() => NativeMethods.SetupDiDestroyDeviceInfoList(handle);
}

internal sealed class SafePreparsedData : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafePreparsedData() : base(true) { }
    protected override bool ReleaseHandle() => NativeMethods.HidD_FreePreparsedData(handle);
}
