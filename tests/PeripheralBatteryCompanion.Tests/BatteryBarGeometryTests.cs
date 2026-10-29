using PeripheralBatteryCompanion.Core.Presentation;
using PeripheralBatteryCompanion.Core;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class BatteryBarGeometryTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(50)] [InlineData(90)] [InlineData(99)] [InlineData(100)]
    public void FillIsProportionalAtFirstWidthAndAfterResize(int percentage)
    {
        foreach (var width in new[] { 4, 104, 272, 480 })
        {
            var fill = BatteryBarGeometry.FillWidth(width, percentage);
            Assert.Equal((width - 4) * percentage / 100f, fill);
            Assert.InRange(fill, 0, width - 4);
        }
    }

    [Theory]
    [InlineData(null)] [InlineData(-1)] [InlineData(101)]
    public void NoFillForUnknownOrInvalidValues(int? percentage) => Assert.Equal(0, BatteryBarGeometry.FillWidth(272, percentage));

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void TinyControlWidthNeverProducesNegativeFill(int width) => Assert.Equal(0, BatteryBarGeometry.FillWidth(width, 99));
    [Theory]
    [InlineData(BatteryAvailability.Unavailable, BatteryObservationState.Unknown, DeviceConnection.Unknown)]
    [InlineData(BatteryAvailability.NotSupported, BatteryObservationState.Unknown, DeviceConnection.Unknown)]
    [InlineData(BatteryAvailability.Available, BatteryObservationState.Stale, DeviceConnection.Active)]
    [InlineData(BatteryAvailability.Available, BatteryObservationState.Unknown, DeviceConnection.Absent)]
    public void NoncurrentDeviceNeverReachesKeyboardOrMouseBar(BatteryAvailability availability, BatteryObservationState observation, DeviceConnection connection)
    {
        var device = new BatteryDevice("Synthetic", 90, null, availability, "Synthetic state", observation,
            Source: GHubWebSocketBatteryProvider.SourceName, Connection: connection);
        var view = BatteryPanelView.From(new(new(device, null, device), device, null, device));
        Assert.Null(view.Percentage); Assert.Null(view.MousePercentage);
        Assert.Equal("Battery: Unknown", view.MouseBatteryText);
    }
}
