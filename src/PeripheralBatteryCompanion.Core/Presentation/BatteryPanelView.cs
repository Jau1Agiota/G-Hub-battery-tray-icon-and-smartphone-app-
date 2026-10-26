namespace PeripheralBatteryCompanion.Core.Presentation;

public enum BatteryLevelColor { Gray, Red, Yellow, Green }

public sealed record BatteryPanelView(int? Percentage, string BatteryText,
    DateTimeOffset? LastReported, string StatusText)
{
    public BatteryLevelColor Color => ColorFor(Percentage);
    public string MouseName => BatteryDevice.UnsupportedMouse.Name;
    public string MouseBatteryText => "Battery: Unknown";
    public string MouseStatusText => "Battery monitoring is not yet supported.";

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
        if (available) return new(current.Percentage, $"Battery: {current.Percentage}%",
            current.LastReported, "");

        var previous = state.LastKnownKeyboard;
        var cached = previous?.Percentage is >= 0 and <= 100
            ? $"Last known: {previous.Percentage}% (cached). " : "";
        return new(null, "Battery: Unavailable", previous?.LastReported, cached + current.Status);
    }
}
