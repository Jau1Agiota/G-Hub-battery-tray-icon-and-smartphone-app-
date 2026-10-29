namespace PeripheralBatteryCompanion.Core.Presentation;

public enum BatteryLevelColor { Gray, Red, Yellow, Green }

public sealed record BatteryPanelView(int? Percentage, string BatteryText,
    DateTimeOffset? LastReported, string StatusText, BatteryDevice? Mouse = null, BatteryDevice? CachedMouse = null, bool IsLiveGateway = false)
{
    public BatteryLevelColor Color => ColorFor(Percentage);
    public string MouseName => BatteryDevice.UnsupportedMouse.Name;
    public string MouseBatteryText => Mouse?.Availability == BatteryAvailability.Available && Mouse.Percentage is >= 0 and <= 100
        ? $"Battery: {(Mouse.Precision == BatteryPrecision.Percentage ? "" : "~")}{Mouse.Percentage}%{(Mouse.Precision == BatteryPrecision.Percentage ? "" : " (approx.)")}" : "Battery: Unknown";
    public int? MousePercentage => Mouse?.Availability == BatteryAvailability.Available && Mouse.Precision == BatteryPrecision.Percentage ? Mouse.Percentage : null;
    public string MouseStatusText => Mouse is null ? "Battery monitoring is not yet supported."
        : Mouse.Availability == BatteryAvailability.Available ? Mouse.Status
        : (CachedMouse?.Percentage is >= 0 and <= 100 ? $"Last: {(CachedMouse.Precision == BatteryPrecision.Percentage ? "" : "~")}{CachedMouse.Percentage}% (stale). " : "") + Mouse.Status;

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
        var available = current.Availability == BatteryAvailability.Available &&
            current.Percentage is >= 0 and <= 100;
        var live = current.Source == GHubWebSocketBatteryProvider.SourceName;
        if (available) return new(current.Precision == BatteryPrecision.Percentage ? current.Percentage : null,
            $"Battery: {(current.Precision == BatteryPrecision.Percentage ? "" : "~")}{current.Percentage}%{(current.Precision == BatteryPrecision.Percentage ? "" : " (approx.)")}",
            current.LastReported, live ? current.Status : "Stored G HUB report; freshness and connectivity unknown. " + current.Status,
            state.Current.Mouse, state.LastKnownMouse, live);

        var previous = state.LastKnownKeyboard;
        var cached = previous?.Percentage is >= 0 and <= 100
            ? $"Last known: {(previous.Precision == BatteryPrecision.Percentage ? "" : "~")}{previous.Percentage}% (cached). " : "";
        return new(null, "Battery: Unavailable", previous?.LastReported, cached + current.Status, state.Current.Mouse, state.LastKnownMouse, live);
    }
}
