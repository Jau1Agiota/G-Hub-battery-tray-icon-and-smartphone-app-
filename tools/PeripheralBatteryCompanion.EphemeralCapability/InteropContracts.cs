using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PeripheralBatteryCompanion.EphemeralCapability;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeUiPolicy
{
    public uint Version, Flags;
    public IntPtr CreationTitle, FriendlyName, Description;
}
internal enum AcquisitionDecision { AcceptNewHandle, RejectWithoutHandle, ReleaseThenReject, RejectInvalidSuccess }
internal static class InteropContracts
{
    internal static AcquisitionDecision Acquisition(int status, bool validHandle) => (status == 0, validHandle) switch {
        (true, true) => AcquisitionDecision.AcceptNewHandle,
        (true, false) => AcquisitionDecision.RejectInvalidSuccess,
        (false, true) => AcquisitionDecision.ReleaseThenReject,
        _ => AcquisitionDecision.RejectWithoutHandle
    };
    internal static int UiPolicySize(int pointerSize) => pointerSize switch {
        4 => 20, 8 => 32, _ => throw new ArgumentOutOfRangeException(nameof(pointerSize))
    };
    // Require full native layout, not only version/flags prefix. Extra public string storage may follow.
    internal static bool ValidUiPolicyShape(ReadOnlySpan<byte> value, int pointerSize) =>
        value.Length >= UiPolicySize(pointerSize) && BinaryPrimitives.ReadUInt32LittleEndian(value) == 1;
    internal static bool NoUiFlags(ReadOnlySpan<byte> value, int pointerSize) =>
        ValidUiPolicyShape(value, pointerSize) && BinaryPrimitives.ReadUInt32LittleEndian(value[4..]) == 0;
}

// No OS calls here. Windows subclass supplies one NCryptFreeObject call; tests supply a managed fake.
internal abstract class CheckedReleaseHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    protected CheckedReleaseHandle() : base(ownsHandle: true) { }
    internal bool? ReleaseSucceeded { get; private set; }
    internal int? ReleaseHResult { get; private set; }
    protected abstract int ReleaseNativeHandle(IntPtr value);
    protected sealed override bool ReleaseHandle()
    {
        try {
            ReleaseHResult = ReleaseNativeHandle(handle);
            ReleaseSucceeded = ReleaseHResult == 0;
            return ReleaseSucceeded.Value;
        } catch {
            // SafeHandle/finalizer cleanup must not throw or log exception contents. No automatic retry.
            ReleaseHResult = null;
            ReleaseSucceeded = false;
            return false;
        }
    }
}
