namespace PeripheralBatteryCompanion.Core;

public enum BatteryAvailability { Available, Unavailable, NotSupported }

public sealed record BatteryDevice(
    string Name, int? Percentage, DateTimeOffset? LastReported,
    BatteryAvailability Availability, string Status)
{
    // Stored battery data cannot independently establish connectivity.
    public string Connectivity => "Unknown";
    public static BatteryDevice UnsupportedMouse { get; } = new(
        "G305 Mouse", null, null, BatteryAvailability.NotSupported, "Not yet supported");
}

public sealed record BatteryReadResult(BatteryDevice Keyboard, DateTimeOffset? DatabaseAccessedAt);

public sealed record BatteryMonitoringState(BatteryReadResult Current, BatteryDevice? LastKnownKeyboard,
    DateTimeOffset? LastSuccessfulDatabaseAccess);

public interface IBatteryProvider
{
    Task<BatteryReadResult> ReadAsync(CancellationToken cancellationToken);
}
