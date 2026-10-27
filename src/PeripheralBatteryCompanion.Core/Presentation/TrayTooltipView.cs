namespace PeripheralBatteryCompanion.Core.Presentation;

public static class TrayTooltipView
{
    public const int MaximumLength = 63;
    // Fixed labels and bounded numeric values; no arbitrary names/status strings can exhaust the native budget.
    public static string Build(BatteryMonitoringState? keyboard, BatteryDevice? hyperX)
    {
        var current = keyboard?.Current.Keyboard;
        var g915 = current?.Availability == BatteryAvailability.Available && Valid(current.Percentage)
            ? $"{current.Percentage}%" : Valid(keyboard?.LastKnownKeyboard?.Percentage)
            ? $"{keyboard!.LastKnownKeyboard!.Percentage}% stale" : "Unknown";
        var known = hyperX is not null && Valid(hyperX.Percentage);
        var candidate = known && hyperX!.ObservationState == BatteryObservationState.CandidateAmbiguous &&
            hyperX.Receiver == ReceiverPresence.Detected;
        var stale = known && (hyperX!.ObservationState is BatteryObservationState.Stale or BatteryObservationState.Unavailable ||
            hyperX.Receiver != ReceiverPresence.Detected);
        var headset = candidate ? $"~{hyperX!.Percentage}%" : stale ? $"~{hyperX!.Percentage}% stale" : "Unknown";
        var text = $"G915: {g915}\nHyperX: {headset}\nG305: Unknown";
        // Defensive runtime guard: never substring a percentage, estimate marker or stale qualifier.
        return text.Length <= MaximumLength ? text : "G915: Unknown\nHyperX: Unknown\nG305: Unknown";
    }
    private static bool Valid(int? percentage) => percentage is >= 0 and <= 100;
}
