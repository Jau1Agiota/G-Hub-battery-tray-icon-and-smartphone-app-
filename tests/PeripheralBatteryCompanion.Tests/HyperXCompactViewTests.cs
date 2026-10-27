using PeripheralBatteryCompanion.Core;
using PeripheralBatteryCompanion.Core.Presentation;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class HyperXCompactViewTests
{
    private static readonly DateTimeOffset Observed = new(2026, 10, 27, 20, 43, 48, TimeSpan.Zero);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("TestUTCMinus3", TimeSpan.FromHours(-3), "Test", "Test");
    private static BatteryDevice Device => new("HyperX Cloud III Wireless", 51, Observed, BatteryAvailability.Available,
        "Technical details stay in Advanced", BatteryObservationState.CandidateAmbiguous, ReceiverPresence.Detected);
    private static HyperXMonitoringStatus Monitoring => new(true, 60, false, false, Observed, Observed.AddSeconds(60),
        HyperXCheckOrigin.Scheduled, 0, "Candidate observed (ambiguous)");

    [Theory]
    [InlineData(0)] [InlineData(51)] [InlineData(100)]
    public void CandidateUsesFriendlyEstimateAndAccessibleUncertaintyAcrossFullRange(int percentage)
    {
        var view = HyperXCompactView.From(Device with { Percentage = percentage }, Monitoring, true, Zone);
        Assert.Equal($"Battery estimate: {percentage}%", view.BatteryText);
        Assert.Equal(percentage, view.Percentage);
        Assert.Equal("Updated: 17:43", view.UpdatedText);
        Assert.Equal("2026-10-27 17:43:48 -03:00", view.TimestampDetail);
        Assert.Equal(HyperXCompactView.Uncertainty, view.EstimateNote);
        Assert.Contains("Headset connectivity unknown", view.AccessibleDescription);
        Assert.DoesNotContain("ConfirmedFresh", view.AccessibleDescription);
        Assert.Empty(view.StatusText);
        Assert.Equal("Auto monitoring · every 60s", view.MonitoringText);
    }

    [Theory]
    [InlineData(BatteryObservationState.Unknown)] [InlineData(BatteryObservationState.Unavailable)]
    [InlineData(BatteryObservationState.ConfirmedFresh)]
    public void NoValidatedCandidateHasNoColoredFillOrInventedCertainty(BatteryObservationState state)
    {
        var view = HyperXCompactView.From(Device with { Percentage = null, LastReported = null, ObservationState = state }, Monitoring, true, Zone);
        Assert.Equal("Battery: Unknown", view.BatteryText); Assert.Null(view.Percentage);
        Assert.Equal("Updated: Never", view.UpdatedText); Assert.Equal("Never", view.TimestampDetail);
        Assert.Empty(view.EstimateNote);
    }

    [Theory]
    [InlineData(BatteryObservationState.Stale, ReceiverPresence.Detected)]
    [InlineData(BatteryObservationState.CandidateAmbiguous, ReceiverPresence.Absent)]
    [InlineData(BatteryObservationState.Unavailable, ReceiverPresence.Detected)]
    public void StaleOrRemovedCandidateRetainsTimeButNeverFillsBar(BatteryObservationState state, ReceiverPresence receiver)
    {
        var view = HyperXCompactView.From(Device with { ObservationState = state, Receiver = receiver }, Monitoring, false, Zone);
        Assert.Equal("Last estimate: 51% (stale)", view.BatteryText); Assert.Null(view.Percentage);
        Assert.Equal("Last update: 17:43", view.UpdatedText);
        Assert.Equal("2026-10-27 17:43:48 -03:00", view.TimestampDetail);
        Assert.Contains("not uniquely verified", view.AccessibleDescription);
        Assert.DoesNotContain("connected", view.AccessibleDescription);
    }

    [Theory]
    [InlineData(60)] [InlineData(120)] [InlineData(300)]
    public void PresentationPreservesChosenIntervalAndSchedule(int interval)
    {
        var status = Monitoring with { IntervalSeconds = interval };
        var device = Device;
        var view = HyperXCompactView.From(device, status, true, Zone);
        Assert.Equal($"Auto monitoring · every {interval}s", view.MonitoringText);
        Assert.Equal(Observed.AddSeconds(60), status.NextCheck); Assert.True(status.Enabled);
        Assert.Equal(BatteryObservationState.CandidateAmbiguous, device.ObservationState);
        Assert.DoesNotContain("Next check", view.UpdatedText);
    }

    [Fact]
    public void GatedPausedAndDisabledModesNeverPretendToBeRunning()
    {
        var gated = HyperXCompactView.From(Device, Monitoring, false);
        var paused = HyperXCompactView.From(Device, Monitoring with { Suspended = true }, true);
        var disabled = HyperXCompactView.From(Device, Monitoring with { Enabled = false }, true);
        var unscheduled = HyperXCompactView.From(Device, Monitoring with { NextCheck = null }, true);
        foreach (var view in new[] { gated, paused, disabled, unscheduled }) Assert.Empty(view.MonitoringText);
        Assert.Contains("Monitoring paused", gated.StatusText); Assert.Contains("Monitoring paused", paused.StatusText);
        Assert.Equal("Automatic monitoring off", disabled.StatusText);
        Assert.Contains("Monitoring paused", unscheduled.StatusText);
    }

    [Fact]
    public void ErrorAndReceiverMessagesRemainCompactWithoutDeveloperDiagnostics()
    {
        var error = HyperXCompactView.From(Device, Monitoring with { ConsecutiveFailures = 1 }, true);
        var absent = HyperXCompactView.From(Device with { Receiver = ReceiverPresence.Absent }, Monitoring, false);
        var ambiguous = HyperXCompactView.From(Device with { Receiver = ReceiverPresence.Ambiguous }, Monitoring, false);
        Assert.Equal("Battery temporarily unavailable", error.StatusText);
        Assert.Equal("Receiver not found", absent.StatusText);
        Assert.Equal("Receiver unavailable", ambiguous.StatusText);
        Assert.DoesNotContain("Technical details", error.StatusText);
    }

    [Theory]
    [InlineData(-1)] [InlineData(101)]
    public void InvalidPercentageNeverReachesProgressBar(int percentage)
    {
        var view = HyperXCompactView.From(Device with { Percentage = percentage }, Monitoring, true);
        Assert.Null(view.Percentage); Assert.Equal("Battery: Unknown", view.BatteryText);
    }

    [Fact]
    public async Task FakeProviderPresentationCannotEnableConsentOrChangeMonitoringPreferences()
    {
        await using var provider = new HyperXBatteryProvider(new Discovery(), new Processes());
        await provider.ReadDeviceAsync();
        var status = Monitoring with { IntervalSeconds = 300 };
        var view = HyperXCompactView.From(provider, status, Zone);
        Assert.False(provider.ExperimentalEnabled); Assert.False(provider.CanQuery);
        Assert.Null(provider.Current.Percentage); Assert.Equal(300, status.IntervalSeconds);
        Assert.Equal(Observed.AddSeconds(60), status.NextCheck); Assert.Contains("paused", view.StatusText);
    }
    private sealed class Discovery : IHyperXReceiverDiscovery
    {
        public Task<HyperXDiscoveryResult> DiscoverAsync(CancellationToken token) => Task.FromResult(new HyperXDiscoveryResult(ReceiverPresence.Detected));
    }
    private sealed class Processes : INgenuityPresenceProbe
    {
        public Task<NgenuityPresence> InspectAsync(CancellationToken token) => throw new InvalidOperationException("Presentation must not query process state");
    }
}
