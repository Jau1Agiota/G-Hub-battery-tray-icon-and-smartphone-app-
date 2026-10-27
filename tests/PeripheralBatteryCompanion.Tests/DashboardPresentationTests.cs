using PeripheralBatteryCompanion.Core;
using PeripheralBatteryCompanion.Core.Presentation;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class DashboardPresentationTests
{
    private static BatteryDevice Headset(int? value, BatteryObservationState state = BatteryObservationState.CandidateAmbiguous,
        ReceiverPresence receiver = ReceiverPresence.Detected) => new("untrusted long device name must not enter tooltip", value,
            DateTimeOffset.UtcNow, BatteryAvailability.Available, "private details never copied", state, receiver);
    private static BatteryMonitoringState Keyboard(int? value, int? cached = null) => new(
        new(new("G915", value, DateTimeOffset.UtcNow, value.HasValue ? BatteryAvailability.Available : BatteryAvailability.Unavailable, ""), null),
        cached.HasValue ? new("G915", cached, DateTimeOffset.UtcNow, BatteryAvailability.Available, "") : null, null);

    [Fact]
    public void TooltipStartsUnknownAndNeverUsesArbitraryNamesOrDetails()
    {
        Assert.Equal("G915: Unknown\nHyperX: Unknown\nG305: Unknown", TrayTooltipView.Build(null, null));
        var text = TrayTooltipView.Build(Keyboard(38), Headset(50));
        Assert.Equal("G915: 38%\nHyperX: ~50%\nG305: Unknown", text);
        Assert.DoesNotContain("private", text); Assert.DoesNotContain("untrusted", text);
    }

    [Theory]
    [InlineData(0)] [InlineData(38)] [InlineData(100)]
    public void TooltipSupportsFullRangeAndBothStaleMarkersWithinNativeBudget(int percentage)
    {
        var text = TrayTooltipView.Build(Keyboard(null, percentage), Headset(percentage, BatteryObservationState.Stale));
        Assert.Contains($"G915: {percentage}% stale", text);
        Assert.Contains($"HyperX: ~{percentage}% stale", text);
        Assert.Contains("G305: Unknown", text);
        Assert.InRange(text.Length, 1, TrayTooltipView.MaximumLength);
    }

    [Theory]
    [InlineData(ReceiverPresence.Absent)] [InlineData(ReceiverPresence.Ambiguous)] [InlineData(ReceiverPresence.InspectionFailed)]
    public void DisconnectCannotPresentACandidateAsCurrent(ReceiverPresence receiver)
    {
        Assert.Contains("HyperX: ~51% stale", TrayTooltipView.Build(Keyboard(38), Headset(51, receiver: receiver)));
    }

    [Theory]
    [InlineData(-1)] [InlineData(101)]
    public void InvalidReadingsAreUnknownRatherThanTruncatedOrClamped(int invalid)
    {
        Assert.Equal("G915: Unknown\nHyperX: Unknown\nG305: Unknown", TrayTooltipView.Build(Keyboard(invalid), Headset(invalid)));
    }

    [Fact]
    public void StateUpdatesChangeTooltipWithoutReadingDevicesOrStartingWork()
    {
        var keyboard = Keyboard(38);
        var headset = Headset(51);
        var original = TrayTooltipView.Build(keyboard, headset);
        var updated = TrayTooltipView.Build(Keyboard(37), headset with { Percentage = 50 });
        var stale = TrayTooltipView.Build(keyboard, headset with { ObservationState = BatteryObservationState.Stale });
        var reconnected = TrayTooltipView.Build(keyboard, headset with { ObservationState = BatteryObservationState.Stale, Receiver = ReceiverPresence.Detected });
        Assert.NotEqual(original, updated); Assert.NotEqual(original, stale); Assert.Equal(stale, reconnected);
        Assert.Equal(51, headset.Percentage); Assert.Equal(BatteryObservationState.CandidateAmbiguous, headset.ObservationState);
    }

    [Theory]
    [InlineData(BatteryObservationState.Unknown)] [InlineData(BatteryObservationState.ConfirmedFresh)]
    public void ReservedOrUnknownStatesNeverBecomeConfirmedReadings(BatteryObservationState state)
    {
        Assert.Contains("HyperX: Unknown", TrayTooltipView.Build(null, Headset(51, state)));
    }

    [Fact]
    public void SystemThemeUsesSystemPreferenceAndExplicitModesOverrideIt()
    {
        var dark = DashboardPalette.Resolve(DashboardThemeMode.Dark, false);
        var light = DashboardPalette.Resolve(DashboardThemeMode.Light, true);
        Assert.Equal(dark, DashboardPalette.Resolve(DashboardThemeMode.System, true));
        Assert.Equal(light, DashboardPalette.Resolve(DashboardThemeMode.System, false));
        Assert.True(Luminance(dark.Text) > Luminance(dark.Surface));
        Assert.True(Luminance(light.Text) < Luminance(light.Surface));
        Assert.True(Contrast(dark.Text, dark.Surface) >= 4.5);
        Assert.True(Contrast(dark.Muted, dark.Surface) >= 4.5);
        Assert.True(Contrast(light.Text, light.Surface) >= 4.5);
        Assert.True(Contrast(light.Muted, light.Surface) >= 4.5);
    }

    [Theory]
    [InlineData(96)] [InlineData(120)] [InlineData(144)] [InlineData(192)]
    public void DashboardPlacementFitsSmallAndNegativeCoordinateScreens(int dpi)
    {
        var screen = new System.Drawing.Rectangle(-1280, 0, 1280, 720);
        var area = new System.Drawing.Rectangle(-1280, 0, 1280, 680);
        var size = PopupPlacement.FitSize(new(PopupPlacement.ClientWidthForDpi(dpi), 900 * dpi / 96), area.Size);
        var point = PopupPlacement.NearTray(screen, area, size, new(-2, 678), 8);
        Assert.True(area.Contains(new System.Drawing.Rectangle(point, size)));
    }

    private static double Luminance(System.Drawing.Color c)
    {
        static double Channel(byte b) { var x = b / 255d; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
        return .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
    }
    private static double Contrast(System.Drawing.Color a, System.Drawing.Color b) =>
        (Math.Max(Luminance(a), Luminance(b)) + .05) / (Math.Min(Luminance(a), Luminance(b)) + .05);
}
