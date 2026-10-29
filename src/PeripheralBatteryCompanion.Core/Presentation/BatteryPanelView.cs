using System.Diagnostics.CodeAnalysis;

namespace PeripheralBatteryCompanion.Core.Presentation;

public enum BatteryLevelColor { Gray, Red, Yellow, Green }

public sealed record BatteryPanelView(int? Percentage, string BatteryText,
    DateTimeOffset? LastReported, string StatusText, BatteryDevice? Mouse = null, BatteryDevice? CachedMouse = null, bool IsLiveGateway = false)
{
    public BatteryPrecision KeyboardPrecision { get; init; } = BatteryPrecision.Percentage;
    public BatteryLevelColor Color => ColorFor(Percentage);
    public string MouseName => BatteryDevice.UnsupportedMouse.Name;
    public string MouseBatteryText => HasCurrentValue(Mouse)
        ? $"Battery: {(Mouse.Precision == BatteryPrecision.Percentage ? "" : "~")}{Mouse.Percentage}%{(Mouse.Precision == BatteryPrecision.Percentage ? "" : " (approx.)")}" : "Battery: Unknown";
    public int? MousePercentage => HasCurrentValue(Mouse) ? Mouse.Percentage : null;
    public string MouseStatusText => Mouse is null ? "Battery monitoring is not yet supported."
        : HasCurrentValue(Mouse) ? Mouse!.Status
        : (CachedMouse?.Percentage is >= 0 and <= 100 ? $"Last: {(CachedMouse.Precision == BatteryPrecision.Percentage ? "" : "~")}{CachedMouse.Percentage}% (stale). " : "") + Mouse.Status;

    public string KeyboardStatusText => !IsLiveGateway ? StatusText : Percentage.HasValue ? PrecisionStatus(KeyboardPrecision)
        : StatusText.Contains("(cached)") ? "Last reading stale · see details" : "G HUB battery unavailable";
    public string MouseCompactStatus => Mouse is null ? MouseStatusText : HasCurrentValue(Mouse)
        ? PrecisionStatus(Mouse.Precision) : Mouse.Connection == DeviceConnection.Absent ? "G HUB reports absent" : CachedMouse?.Percentage is >= 0 and <= 100 ? "Last reading stale · see details" : "G HUB battery unavailable";

    private static bool HasCurrentValue([NotNullWhen(true)] BatteryDevice? device) => device?.Availability == BatteryAvailability.Available &&
        device.Percentage is >= 0 and <= 100 && device.Connection != DeviceConnection.Absent &&
        device.ObservationState is not (BatteryObservationState.Stale or BatteryObservationState.Unavailable);

    private static string PrecisionStatus(BatteryPrecision precision) => precision switch
    {
        BatteryPrecision.Percentage => "G HUB · Precise reading",
        BatteryPrecision.Approximate => "G HUB · Approximate reading",
        _ => "G HUB · Precision unknown"
    };
    public static BatteryLevelColor ColorFor(int? percentage) => percentage switch
    {
        >= 51 and <= 100 => BatteryLevelColor.Green,
        >= 21 and <= 50 => BatteryLevelColor.Yellow,
        >= 0 and <= 20 => BatteryLevelColor.Red,
        _ => BatteryLevelColor.Gray
    };

    public static BatteryPanelView From(BatteryMonitoringState state)
    {
        var current = state.Current.Keyboard;
        var available = HasCurrentValue(current);
        var live = current.Source == GHubWebSocketBatteryProvider.SourceName;
        if (available) return new(current.Percentage,
            $"Battery: {(current.Precision == BatteryPrecision.Percentage ? "" : "~")}{current.Percentage}%{(current.Precision == BatteryPrecision.Percentage ? "" : " (approx.)")}",
            current.LastReported, live ? current.Status : "Stored G HUB report; freshness and connectivity unknown. " + current.Status,
            state.Current.Mouse, state.LastKnownMouse, live) { KeyboardPrecision = current.Precision };

        var previous = state.LastKnownKeyboard;
        var cached = previous?.Percentage is >= 0 and <= 100
            ? $"Last known: {(previous.Precision == BatteryPrecision.Percentage ? "" : "~")}{previous.Percentage}% (cached). " : "";
        return new(null, "Battery: Unavailable", previous?.LastReported, cached + current.Status, state.Current.Mouse, state.LastKnownMouse, live) { KeyboardPrecision = current.Precision };
    }
}
