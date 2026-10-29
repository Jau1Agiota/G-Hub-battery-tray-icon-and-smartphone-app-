namespace PeripheralBatteryCompanion.Core;

public enum BatteryPrecision { Unknown, Percentage, Approximate }
public enum DeviceConnection { Unknown, Active, Absent }

public enum BatteryAvailability { Available, Unavailable, NotSupported }

public sealed record BatteryDevice(
    string Name, int? Percentage, DateTimeOffset? LastReported,
    BatteryAvailability Availability, string Status,
    BatteryObservationState ObservationState = BatteryObservationState.Unknown,
    ReceiverPresence Receiver = ReceiverPresence.Unknown, string Source = "",
    BatteryPrecision Precision = BatteryPrecision.Percentage, DeviceConnection Connection = DeviceConnection.Unknown)
{
    // Stored battery data cannot independently establish connectivity.
    public string Connectivity => Connection switch { DeviceConnection.Active => "Active (G HUB)", DeviceConnection.Absent => "Absent (G HUB)", _ => "Unknown" };
    public static BatteryDevice UnsupportedMouse { get; } = new(
        "G305 Mouse", null, null, BatteryAvailability.NotSupported, "Not yet supported");
}

public sealed record BatteryReadResult(BatteryDevice Keyboard, DateTimeOffset? DatabaseAccessedAt, BatteryDevice? Mouse = null);

public sealed record BatteryMonitoringState(BatteryReadResult Current, BatteryDevice? LastKnownKeyboard,
    DateTimeOffset? LastSuccessfulDatabaseAccess, BatteryDevice? LastKnownMouse = null);

public interface IBatteryProvider
{
    Task<BatteryReadResult> ReadAsync(CancellationToken cancellationToken);
}
