using PeripheralBatteryCompanion.Windows;
using Xunit;

namespace PeripheralBatteryCompanion.Windows.Tests;

public sealed class RecordProtectionBoundaryTests
{
    [Fact]
    public void CurrentUserAdapterDeniesBeforeSerializationAllocationOrNativeCalls()
    {
        var adapter = new WindowsCurrentUserRecordProtection();
        var protect = Assert.Throws<InvalidOperationException>(() => adapter.Protect(null!));
        var unprotect = Assert.Throws<InvalidOperationException>(() => adapter.Unprotect(null!, null!));
        Assert.Contains("GATE E BLOCKED", protect.Message);
        Assert.Contains("GATE E BLOCKED", unprotect.Message);
        // Deliberately no reflection into Transform, gate removal, DPAPI, key or certificate operation.
    }
}
