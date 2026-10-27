using System.Runtime.InteropServices;
using PeripheralBatteryCompanion.G305Inspector;

namespace PeripheralBatteryCompanion.Windows;

// Hidden top-level window receives HID arrival/removal without timer-driven battery queries.
internal sealed class HyperXDeviceNotifications : NativeWindow, IDisposable
{
    private readonly Action<bool> _changed;
    private IntPtr _registration;
    public HyperXDeviceNotifications(Action<bool> changed)
    {
        _changed = changed;
        CreateHandle(new CreateParams { Caption = "PeripheralBatteryCompanionDeviceEvents" });
        var filter = new DeviceInterface { Size = Marshal.SizeOf<DeviceInterface>(), Type = 5,
            ClassGuid = new("4d1e55b2-f16f-11cf-88cb-001111000030") };
        _registration = RegisterDeviceNotificationW(Handle, ref filter, 0);
        // Registration failure leaves the bounded metadata fallback active; never enables a query.
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0219 && (m.WParam == 0x8000 || m.WParam == 0x8004) && m.LParam != IntPtr.Zero)
        {
            var size = Marshal.ReadInt32(m.LParam);
            if (size is >= 30 and <= 65536 && Marshal.ReadInt32(m.LParam, 4) == 5)
            {
                var path = Marshal.PtrToStringUni(IntPtr.Add(m.LParam, 28), (size - 28) / 2)?.TrimEnd('\0');
                if (path is not null && HyperXCollectionRules.TargetPaths([path]).Length == 1)
                    _changed(m.WParam == 0x8004);
            }
        }
        base.WndProc(ref m);
    }
    public void Dispose()
    {
        if (_registration != IntPtr.Zero) { UnregisterDeviceNotification(_registration); _registration = IntPtr.Zero; }
        DestroyHandle();
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterface { public int Size, Type, Reserved; public Guid ClassGuid; public short Name; }
    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr RegisterDeviceNotificationW(IntPtr recipient, ref DeviceInterface filter, uint flags);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);
}
