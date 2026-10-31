using PeripheralBatteryCompanion.EphemeralCapability;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

// Synthetic buffers and managed callbacks only; no CNG, real handles/files/journals or preview entrypoint.
public sealed class Option2StaticAuditTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EveryOwnedByteIsClearedBeforeAnyFallibleReporting(bool completed, bool attempted)
    {
        var buffer = Enumerable.Repeat((byte)0x55, 104).ToArray();
        Assert.Throws<InvalidOperationException>((Action)(() => {
            var observation = Option2PrivateBuffer.Clear(buffer, completed, attempted);
            Assert.Equal(completed || attempted, observation.OutputObserved);
            Assert.True(observation.Cleared);
            Assert.All(buffer, b => Assert.Equal(0, b));
            throw new InvalidOperationException("synthetic evidence allocation/report failure AFTER clear");
        }));
        Assert.All(buffer, b => Assert.Equal(0, b));
    }

    [Fact]
    public void EmptyOrZeroOutputCannotInventPrivateDataButCompletedFillStillFailsClaim()
    {
        Assert.Equal(new O2ClearedBuffer(false, true), Option2PrivateBuffer.Clear(new byte[104], false, true));
        Assert.Equal(new O2ClearedBuffer(true, true), Option2PrivateBuffer.Clear(new byte[104], true, true));
        Assert.Equal(new O2ClearedBuffer(false, true), Option2PrivateBuffer.Clear([], false, false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(unchecked((int)0x80090010))]
    [InlineData(unchecked((int)0x80090026))]
    public void ExactProductionOwnershipLedgerConsumesOrReleasesOnlyOnce(int status)
    {
        var ledger = new Option2KeyOwnership(); int calls = 0, invalidations = 0, frees = 0;
        var result = ledger.Delete(() => { calls++; return status; }, () => invalidations++);
        Assert.Equal(status, result.Code);
        Assert.Equal(status == 0 ? O2HandleState.ConsumedByDelete : O2HandleState.Owned, result.State);
        Assert.Equal(status == 0 ? 1 : 0, invalidations);
        Assert.Throws<InvalidOperationException>(() => ledger.Delete(() => { calls++; return 0; }, () => invalidations++));
        if (ledger.BeginRelease()) frees++;
        if (ledger.BeginRelease()) frees++;
        Assert.Equal(status == 0 ? 0 : 1, frees);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void NativeExceptionAbandonsUnknownOwnershipWithoutSecondDeleteOrFree()
    {
        var ledger = new Option2KeyOwnership(); int calls = 0, invalidations = 0;
        var result = ledger.Delete(() => { calls++; throw new Exception("SECRET path SID"); }, () => invalidations++);
        Assert.Null(result.Code); Assert.Equal(O2HandleState.OwnershipUnknown, result.State);
        Assert.False(ledger.BeginRelease()); Assert.Equal(1, invalidations);
        Assert.Throws<InvalidOperationException>(() => ledger.Delete(() => { calls++; return 0; }, () => invalidations++));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void DownstreamReportingFailureCannotEraseConfirmedDeleteConsumption()
    {
        var ledger = new Option2KeyOwnership(); int invalidations = 0;
        Assert.Throws<InvalidOperationException>((Action)(() => {
            var result = ledger.Delete(() => 0, () => invalidations++);
            Assert.Equal(O2HandleState.ConsumedByDelete, result.State);
            throw new InvalidOperationException("synthetic result/report failure");
        }));
        Assert.Equal(O2HandleState.ConsumedByDelete, ledger.State);
        Assert.False(ledger.BeginRelease()); Assert.Equal(1, invalidations);
    }

    [Fact]
    public void ThrowingHandleReleaseIsNotRetriedAndPreviewWarnsOfNormalPreBindingFailure()
    {
        var ledger = new Option2KeyOwnership();
        Assert.Throws<InvalidOperationException>((Action)(() => {
            Assert.True(ledger.BeginRelease());
            throw new InvalidOperationException("synthetic release exception");
        }));
        Assert.False(ledger.BeginRelease());
        Assert.Equal(O2HandleState.Released, ledger.State); // attempted managed release, not native success evidence
        const string id = "1234567812344234a2341234567890ab";
        var text = O2Presentation.Preview(id); // pure formatting only; no ID generation
        Assert.Contains("BEFORE durable binding", text);
        Assert.Contains("even after handle release", text);
        Assert.Contains("no automatic deletion without binding", text);
    }
}
