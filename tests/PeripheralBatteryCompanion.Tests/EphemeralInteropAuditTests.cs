using System.Buffers.Binary;
using System.Runtime.InteropServices;
using PeripheralBatteryCompanion.EphemeralCapability;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

// Managed fake handles only: no ncrypt.dll, native key, provider, certificate, crypto or OS cleanup call.
public sealed class EphemeralInteropAuditTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void TruncatedUiPolicyCannotPassEvenWithValidVersionAndNoFlags(int pointerSize)
    {
        int size = InteropContracts.UiPolicySize(pointerSize);
        for (int length = 0; length < size; length++) {
            var data = new byte[length];
            if (length >= 4) BinaryPrimitives.WriteUInt32LittleEndian(data, 1);
            Assert.False(InteropContracts.ValidUiPolicyShape(data, pointerSize));
            Assert.False(InteropContracts.NoUiFlags(data, pointerSize));
        }
        var full = new byte[size]; BinaryPrimitives.WriteUInt32LittleEndian(full, 1);
        Assert.True(InteropContracts.NoUiFlags(full, pointerSize));
        BinaryPrimitives.WriteUInt32LittleEndian(full.AsSpan(4), 1);
        Assert.True(InteropContracts.ValidUiPolicyShape(full, pointerSize));
        Assert.False(InteropContracts.NoUiFlags(full, pointerSize));
        BinaryPrimitives.WriteUInt32LittleEndian(full, 2);
        Assert.False(InteropContracts.ValidUiPolicyShape(full, pointerSize));
    }

    [Fact]
    public void ActualManagedLayoutMatchesNativeDwordAndPointerOffsets()
    {
        Assert.Equal(InteropContracts.UiPolicySize(IntPtr.Size), Marshal.SizeOf<NativeUiPolicy>());
        Assert.Equal(0, Marshal.OffsetOf<NativeUiPolicy>(nameof(NativeUiPolicy.Version)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<NativeUiPolicy>(nameof(NativeUiPolicy.Flags)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<NativeUiPolicy>(nameof(NativeUiPolicy.CreationTitle)).ToInt32());
        Assert.Equal(8 + IntPtr.Size, Marshal.OffsetOf<NativeUiPolicy>(nameof(NativeUiPolicy.FriendlyName)).ToInt32());
        Assert.Equal(8 + 2 * IntPtr.Size, Marshal.OffsetOf<NativeUiPolicy>(nameof(NativeUiPolicy.Description)).ToInt32());
    }

    [Theory]
    [InlineData(0, true, 0)]
    [InlineData(0, false, 3)]
    [InlineData(unchecked((int)0x80090029), true, 2)]
    [InlineData(unchecked((int)0x80090029), false, 1)]
    public void FailedStatusOrInvalidOutputCannotBeAccepted(int status, bool valid, int expected)
    {
        Assert.Equal((AcquisitionDecision)expected, InteropContracts.Acquisition(status, valid));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(unchecked((int)0x80090026), false)]
    [InlineData(0, true)]
    public void ReleaseSuccessFailureAndExceptionAreCapturedWithoutThrowOrRetry(int status, bool throws)
    {
        var handle = new FakeHandle(new IntPtr(7), status, throws);
        handle.Dispose();
        handle.Dispose();
        Assert.Equal(1, handle.Calls);
        Assert.True(handle.IsClosed);
        Assert.Equal(!throws && status == 0, handle.ReleaseSucceeded);
        Assert.Equal(throws ? (int?)null : status, handle.ReleaseHResult);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidHandlesNeverInventNativeReleaseSuccess(int value)
    {
        var handle = new FakeHandle(new IntPtr(value), 0, false);
        handle.Dispose();
        Assert.Equal(0, handle.Calls);
        Assert.Null(handle.ReleaseSucceeded);
        Assert.Null(handle.ReleaseHResult);
    }

    [Fact]
    public void SafeHandleCallReferenceDefersReleaseUntilLastReferenceAndDoesNotRetry()
    {
        var handle = new FakeHandle(new IntPtr(7), 0, false);
        bool acquired = false;
        try {
            handle.DangerousAddRef(ref acquired);
            handle.Dispose();
            Assert.Equal(0, handle.Calls);
            Assert.Null(handle.ReleaseSucceeded);
        } finally { if (acquired) handle.DangerousRelease(); }
        Assert.Equal(1, handle.Calls);
        Assert.True(handle.ReleaseSucceeded);
        handle.Dispose();
        Assert.Equal(1, handle.Calls);
    }

    private sealed class FakeHandle : CheckedReleaseHandle
    {
        private readonly int status;
        private readonly bool throws;
        internal int Calls;
        internal FakeHandle(IntPtr value, int status, bool throws) { SetHandle(value); this.status = status; this.throws = throws; }
        protected override int ReleaseNativeHandle(IntPtr value)
        {
            Calls++;
            if (throws) throw new Exception("SECRET path SID key inventory");
            return status;
        }
    }
}
