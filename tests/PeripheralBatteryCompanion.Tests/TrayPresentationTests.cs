using System.Drawing;
using PeripheralBatteryCompanion.Core;
using PeripheralBatteryCompanion.Core.Presentation;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

// These tests cover presentation/state logic, not Windows message ordering or real tray interaction.
public sealed class TrayPresentationTests
{
    private static BatteryMonitoringState State(int? percentage, BatteryAvailability availability = BatteryAvailability.Available) =>
        new(new(new("G915 Keyboard", percentage, DateTimeOffset.Parse("2026-10-26T13:41:42Z"),
            availability, "G HUB data unavailable"), DateTimeOffset.UtcNow), null, DateTimeOffset.UtcNow);

    [Fact]
    public void LeftClickTogglesWithoutRequestingExit()
    {
        var state = new TrayPanelState();
        state.TogglePanel();
        Assert.True(state.IsPanelVisible);
        state.TogglePanel();
        Assert.False(state.IsPanelVisible);
        Assert.False(state.IsExitRequested);
    }

    [Fact]
    public void DismissalAndContextMenuNeverRequestExit()
    {
        var state = new TrayPanelState();
        for (var i = 0; i < 100; i++)
        {
            state.OpenPanel();
            state.OpenPanel(); // Open is idempotent; the native adapter retains one form.
            Assert.True(state.IsPanelVisible);
            state.DismissPanel(); // Escape, deactivation and Close route here.
            Assert.False(state.IsPanelVisible);
            state.OpenPanel();
            state.OpenContextMenu();
            Assert.False(state.IsPanelVisible);
            Assert.False(state.IsExitRequested);
        }
    }

    [Fact]
    public void OnlyExplicitExitPreventsFurtherOpening()
    {
        var state = new TrayPanelState();
        state.OpenPanel();
        state.RequestExit();
        state.TogglePanel();
        state.OpenPanel();
        state.OpenContextMenu();
        state.RequestExit();
        Assert.True(state.IsExitRequested);
        Assert.False(state.IsPanelVisible);
    }

    [Theory]
    [InlineData(null, BatteryLevelColor.Gray)]
    [InlineData(-1, BatteryLevelColor.Gray)]
    [InlineData(0, BatteryLevelColor.Red)]
    [InlineData(20, BatteryLevelColor.Red)]
    [InlineData(21, BatteryLevelColor.Yellow)]
    [InlineData(50, BatteryLevelColor.Yellow)]
    [InlineData(51, BatteryLevelColor.Green)]
    [InlineData(100, BatteryLevelColor.Green)]
    [InlineData(101, BatteryLevelColor.Gray)]
    public void ColorBoundariesAreSharedByPopupAndTray(int? percentage, BatteryLevelColor expected) =>
        Assert.Equal(expected, BatteryPanelView.ColorFor(percentage));

    [Fact]
    public void ChangedPercentagesAndUnchangedReportTimeAreReflected()
    {
        var first = BatteryPanelView.From(State(45));
        var second = BatteryPanelView.From(State(70));
        Assert.Equal("Battery: 45%", first.BatteryText);
        Assert.Equal("Battery: 70%", second.BatteryText);
        Assert.Equal(70, second.Percentage);
        Assert.Equal(BatteryLevelColor.Green, second.Color);
        Assert.Equal(first.LastReported, second.LastReported);
        Assert.Empty(second.StatusText);
        Assert.Equal("Battery: Unknown", second.MouseBatteryText);
        Assert.Contains("not yet supported", second.MouseStatusText);
    }

    [Fact]
    public void CachedFailureIsUnavailableAndNeverFillsCurrentIndicator()
    {
        var good = State(45);
        var bad = State(null, BatteryAvailability.Unavailable) with { LastKnownKeyboard = good.Current.Keyboard };
        var view = BatteryPanelView.From(bad);
        Assert.Null(view.Percentage);
        Assert.Equal(BatteryLevelColor.Gray, view.Color);
        Assert.Equal("Battery: Unavailable", view.BatteryText);
        Assert.Contains("45% (cached)", view.StatusText);
        Assert.Equal(good.Current.Keyboard.LastReported, view.LastReported);
    }

    [Fact]
    public void InvalidOrMissingReadingHasNoFabricatedValue()
    {
        foreach (var state in new[] { State(101), State(null, BatteryAvailability.Unavailable) })
        {
            var view = BatteryPanelView.From(state);
            Assert.Null(view.Percentage);
            Assert.Equal("Battery: Unavailable", view.BatteryText);
            Assert.Equal(BatteryLevelColor.Gray, view.Color);
            Assert.Equal("Battery: Unknown", view.MouseBatteryText);
        }
    }

    [Theory]
    // Screen/work-area rectangles, anchor, expected position; popup is 300 x 200, gap 8.
    [InlineData(0, 0, 1920, 1080, 0, 0, 1920, 1040, 1900, 1060, 1620, 832)]
    [InlineData(0, 0, 1920, 1080, 0, 40, 1920, 1040, 1900, 20, 1620, 48)]
    [InlineData(0, 0, 1920, 1080, 40, 0, 1880, 1080, 20, 1050, 48, 880)]
    [InlineData(0, 0, 1920, 1080, 0, 0, 1880, 1080, 1900, 1050, 1572, 880)]
    [InlineData(-1920, 0, 1920, 1080, -1920, 0, 1920, 1040, -20, 1060, -300, 832)]
    [InlineData(0, -1080, 1920, 1080, 0, -1080, 1920, 1040, 1900, -20, 1620, -248)]
    [InlineData(0, 0, 1920, 1080, 0, 0, 1920, 1080, 1900, 1070, 1620, 862)]
    [InlineData(0, 0, 1920, 1080, 0, 0, 1920, 1080, 20, 10, 0, 18)]
    public void PlacementRespectsTaskbarAndMonitor(int sx, int sy, int sw, int sh,
        int wx, int wy, int ww, int wh, int ax, int ay, int expectedX, int expectedY)
    {
        var work = new Rectangle(wx, wy, ww, wh);
        var popup = new Size(300, 200);
        var location = PopupPlacement.NearTray(new(sx, sy, sw, sh), work, popup, new(ax, ay), 8);
        Assert.Equal(new Point(expectedX, expectedY), location);
        Assert.True(work.Contains(new Rectangle(location, popup)));
    }

    [Fact]
    public void ScaledPopupRemainsInsideWorkingArea()
    {
        var work = new Rectangle(0, 0, 1920, 1000);
        var size = new Size(600, 400);
        var point = PopupPlacement.NearTray(new(0, 0, 1920, 1080), work, size, new(1900, 1040), 16);
        Assert.True(work.Contains(new Rectangle(point, size)));
        Assert.Equal(584, point.Y);
    }

    [Fact]
    public async Task RefreshUpdatesPresentationWhilePanelIsHiddenAndAfterMissingData()
    {
        var count = 0;
        var provider = new FakeProvider(() => ++count == 1
            ? State(null, BatteryAvailability.Unavailable).Current : State(62).Current);
        await using var monitor = new BatteryMonitoringService(provider);
        var interaction = new TrayPanelState();
        BatteryPanelView? view = null;
        monitor.Updated += state => view = BatteryPanelView.From(state);
        interaction.OpenPanel();
        interaction.DismissPanel();
        await monitor.RefreshAsync();
        Assert.Equal("Battery: Unavailable", view!.BatteryText);
        await monitor.RefreshAsync();
        Assert.Equal(62, view.Percentage);
        Assert.False(interaction.IsPanelVisible);
        Assert.False(interaction.IsExitRequested);
        interaction.OpenPanel();
        Assert.Equal("Battery: 62%", view.BatteryText);
    }

    private sealed class FakeProvider(Func<BatteryReadResult> read) : IBatteryProvider
    {
        public Task<BatteryReadResult> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(read());
    }
}
