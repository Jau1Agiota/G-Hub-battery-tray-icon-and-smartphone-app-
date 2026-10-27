namespace PeripheralBatteryCompanion.Core.Presentation;

// Presentation only: never changes observations, preferences, consent or scheduling.
public sealed record HyperXCompactView(string BatteryText, int? Percentage, string UpdatedText,
    string MonitoringText, string StatusText, string EstimateNote, string AccessibleDescription, string TimestampDetail)
{
    public const string Uncertainty = "Estimate; response source not uniquely verified";
    public static HyperXCompactView From(HyperXBatteryProvider provider, HyperXMonitoringStatus monitoring,
        TimeZoneInfo? zone = null) => From(provider.Current, monitoring, provider.CanQuery, zone);

    public static HyperXCompactView From(BatteryDevice device, HyperXMonitoringStatus monitoring, bool canQuery,
        TimeZoneInfo? zone = null)
    {
        var percentage = device.Percentage is >= 0 and <= 100 ? device.Percentage : null;
        var stale = percentage.HasValue && (device.ObservationState == BatteryObservationState.Stale ||
            device.Receiver != ReceiverPresence.Detected || device.ObservationState == BatteryObservationState.Unavailable);
        var candidate = percentage.HasValue && device.ObservationState == BatteryObservationState.CandidateAmbiguous && !stale;
        var text = stale ? $"Last estimate: {percentage}% (stale)" : candidate ? $"Battery estimate: {percentage}%" : "Battery: Unknown";
        var localTime = device.LastReported is { } observed ? TimeZoneInfo.ConvertTime(observed, zone ?? TimeZoneInfo.Local) : (DateTimeOffset?)null;
        var updated = localTime.HasValue ? $"{(stale ? "Last update" : "Updated")}: {localTime:HH:mm}" : "Updated: Never";
        var active = monitoring.Enabled && !monitoring.Suspended && canQuery && (monitoring.NextCheck.HasValue || monitoring.Busy);
        var cadence = active ? $"Auto monitoring · every {monitoring.IntervalSeconds}s" : "";
        var status = device.Receiver == ReceiverPresence.Absent ? "Receiver not found"
            : device.Receiver is ReceiverPresence.Ambiguous or ReceiverPresence.InspectionFailed ? "Receiver unavailable"
            : monitoring.Suspended ? "Monitoring paused · open HyperX settings"
            : monitoring.ConsecutiveFailures > 0 ? "Battery temporarily unavailable"
            : monitoring.Enabled && !active ? "Monitoring paused · open HyperX settings"
            : !monitoring.Enabled ? "Automatic monitoring off" : "";
        var note = candidate || stale ? Uncertainty : "";
        return new(text, candidate ? percentage : null, updated, cadence, status, note,
            text + ". " + (note.Length == 0 ? "No validated estimate available." : note + ". Headset connectivity unknown."),
            localTime?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "Never");
    }
}
