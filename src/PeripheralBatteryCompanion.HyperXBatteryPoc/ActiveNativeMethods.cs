using System.Runtime.InteropServices;

namespace PeripheralBatteryCompanion.HyperXBatteryPoc;

internal static unsafe class ActiveNativeMethods
{
    // hidclass.h: HID_IN_CTL_CODE(101) = CTL_CODE(FILE_DEVICE_KEYBOARD=0xB,101,METHOD_IN_DIRECT=1,FILE_ANY_ACCESS=0).
    internal const uint SetOutputReport = 0x000B0195;
    internal const int ErrorIoPending = 997;
    internal const int ErrorOperationAborted = 995;
    internal const uint GenericRead = 0x80000000;
    internal const uint FileFlagOverlapped = 0x40000000;

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(IntPtr file, uint code, IntPtr input, uint inputSize,
        IntPtr output, uint outputSize, IntPtr bytesReturned, NativeOverlapped* overlapped);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadFile(IntPtr file, IntPtr buffer, uint size, IntPtr bytesRead, NativeOverlapped* overlapped);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CancelIoEx(IntPtr file, NativeOverlapped* overlapped);
}
