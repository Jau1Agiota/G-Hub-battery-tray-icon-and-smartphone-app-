using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeripheralBatteryCompanion.Core.Snapshots;

public enum SnapshotDeviceId
{
    [JsonStringEnumMemberName("g915")] G915,
    [JsonStringEnumMemberName("g305")] G305,
    [JsonStringEnumMemberName("hyperx")] HyperX
}
public enum SnapshotPrecision { UNKNOWN, PRECISE, APPROXIMATE, ESTIMATE }
public enum SnapshotSourceKind { UNKNOWN, GHUB_GATEWAY, GHUB_STORED, HYPERX_SUPERVISED }
public enum SnapshotSocSupport { UNKNOWN, SUPPORTED, NOT_SUPPORTED, NOT_APPLICABLE }
public enum SnapshotOwnership { UNKNOWN, ATTRIBUTED, AMBIGUOUS }
public enum SnapshotAvailability { AVAILABLE, UNAVAILABLE, STALE, NOT_SUPPORTED }
public enum SnapshotFreshness { UNKNOWN, RECENT_READ, STALE }
public enum SnapshotDeviceConnection { UNKNOWN, ACTIVE_REPORTED, ABSENT_REPORTED }
public enum SnapshotUnavailableReason { NO_READING, SOURCE_UNAVAILABLE, DEVICE_ABSENT, STALE_CACHE, AMBIGUOUS_TARGET, SAFETY_STOP, MONITORING_OFF, UNSUPPORTED }

public sealed record SnapshotSource(SnapshotSourceKind Kind, SnapshotSocSupport SocSupport, SnapshotOwnership Ownership);
public sealed record SnapshotReading(int? BatteryPercent, SnapshotPrecision Precision, SnapshotSource Source,
    DateTime? ReadAt, DateTime? ObservedAt);
public sealed record SnapshotDevice(SnapshotDeviceId Id, SnapshotReading Current, SnapshotAvailability Availability,
    SnapshotFreshness Freshness, SnapshotDeviceConnection DeviceConnection,
    SnapshotUnavailableReason? UnavailableReason, SnapshotReading? LastKnown);
public sealed record BatterySnapshotV1(int SchemaVersion, bool Synthetic, Guid ServerInstanceId, long Revision,
    DateTime? CapturedAt, ImmutableArray<SnapshotDevice> Devices);

/// <summary>Callers supply existing values only; this metadata does not consult a clock or create an identity.</summary>
public enum LogitechSnapshotOrigin { Gateway, LegacySqlite }
public sealed record SnapshotMetadata(Guid ServerInstanceId, long Revision, DateTimeOffset? CapturedAt, bool Synthetic = false);

/// <summary>Pure projection: no provider, UI, scheduler, helper, clock, database or transport access.</summary>
public static class BatterySnapshotProjection
{
    private const string HyperXSource = "Supervised one-shot HID; response ownership ambiguous";
    private static readonly SnapshotSource UnknownSource = new(SnapshotSourceKind.UNKNOWN, SnapshotSocSupport.UNKNOWN, SnapshotOwnership.UNKNOWN);
    private static SnapshotReading Empty => new(null, SnapshotPrecision.UNKNOWN, UnknownSource, null, null);

    public static BatterySnapshotV1 Build(BatteryMonitoringState? logitech, BatteryDevice? hyperX, SnapshotMetadata metadata, LogitechSnapshotOrigin origin = LogitechSnapshotOrigin.Gateway)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!Enum.IsDefined(origin)) throw new ArgumentException("Unknown source origin.");
        var result = new BatterySnapshotV1(1, metadata.Synthetic, metadata.ServerInstanceId, metadata.Revision,
            metadata.CapturedAt?.UtcDateTime,
            [Logitech(SnapshotDeviceId.G915, logitech?.Current.Keyboard, logitech?.LastKnownKeyboard, logitech?.Current.DatabaseAccessedAt, origin),
             Logitech(SnapshotDeviceId.G305, logitech?.Current.Mouse, logitech?.LastKnownMouse, logitech?.Current.DatabaseAccessedAt, origin),
             HyperX(hyperX)]);
        BatterySnapshotJson.Validate(result);
        return result;
    }

    private static bool Valid(BatteryDevice? value) => value?.Percentage is >= 0 and <= 100;
    private static SnapshotDeviceConnection Connection(BatteryDevice? value) => value?.Connection switch
    {
        DeviceConnection.Active => SnapshotDeviceConnection.ACTIVE_REPORTED,
        DeviceConnection.Absent => SnapshotDeviceConnection.ABSENT_REPORTED,
        _ => SnapshotDeviceConnection.UNKNOWN
    };
    private static SnapshotReading LogitechReading(SnapshotDeviceId id, BatteryDevice value, DateTimeOffset? databaseRead, LogitechSnapshotOrigin origin)
    {
        var gateway = value.Source == GHubWebSocketBatteryProvider.SourceName;
        // Legacy source attribution is explicit caller context, never guessed from a device name/status.
        var stored = origin == LogitechSnapshotOrigin.LegacySqlite && id == SnapshotDeviceId.G915 && value.Source.Length == 0;
        var kind = gateway ? SnapshotSourceKind.GHUB_GATEWAY : stored ? SnapshotSourceKind.GHUB_STORED : SnapshotSourceKind.UNKNOWN;
        var precision = kind == SnapshotSourceKind.UNKNOWN ? SnapshotPrecision.UNKNOWN : value.Precision switch
        {
            BatteryPrecision.Percentage => SnapshotPrecision.PRECISE,
            BatteryPrecision.Approximate => SnapshotPrecision.APPROXIMATE,
            _ => SnapshotPrecision.UNKNOWN
        };
        var soc = gateway ? value.Precision switch
        {
            BatteryPrecision.Percentage => SnapshotSocSupport.SUPPORTED,
            BatteryPrecision.Approximate => SnapshotSocSupport.NOT_SUPPORTED,
            _ => SnapshotSocSupport.UNKNOWN
        } : SnapshotSocSupport.UNKNOWN;
        return new(value.Percentage, precision,
            new(kind, soc, gateway ? SnapshotOwnership.ATTRIBUTED : SnapshotOwnership.UNKNOWN),
            gateway ? value.LastReported?.UtcDateTime : stored ? databaseRead?.UtcDateTime : null,
            stored ? value.LastReported?.UtcDateTime : null);
    }
    private static SnapshotDevice Logitech(SnapshotDeviceId id, BatteryDevice? current, BatteryDevice? cached, DateTimeOffset? databaseRead, LogitechSnapshotOrigin origin)
    {
        var connection = Connection(current);
        var usable = Valid(current) && current!.Availability == BatteryAvailability.Available && current.Connection != DeviceConnection.Absent &&
            current.ObservationState is not (BatteryObservationState.Stale or BatteryObservationState.Unavailable);
        if (usable) return new(id, LogitechReading(id, current!, databaseRead, origin), SnapshotAvailability.AVAILABLE,
            SnapshotFreshness.UNKNOWN, connection, null, null);
        var retained = Valid(cached) ? cached : Valid(current) && current!.ObservationState == BatteryObservationState.Stale ? current : null;
        // No reliable receipt timestamp exists for a legacy stored last-known report. Do not borrow the latest successful SELECT time.
        var lastKnown = retained is null ? null : LogitechReading(id, retained, null, origin);
        var unsupported = current?.Availability == BatteryAvailability.NotSupported;
        var absent = connection == SnapshotDeviceConnection.ABSENT_REPORTED;
        var stale = !unsupported && !absent && lastKnown is not null;
        var reason = unsupported ? SnapshotUnavailableReason.UNSUPPORTED : absent ? SnapshotUnavailableReason.DEVICE_ABSENT
            : current?.ObservationState == BatteryObservationState.Stale ? SnapshotUnavailableReason.STALE_CACHE
            : current is null ? SnapshotUnavailableReason.NO_READING : SnapshotUnavailableReason.SOURCE_UNAVAILABLE;
        return new(id, Empty, unsupported ? SnapshotAvailability.NOT_SUPPORTED : stale ? SnapshotAvailability.STALE : SnapshotAvailability.UNAVAILABLE,
            lastKnown is null ? SnapshotFreshness.UNKNOWN : SnapshotFreshness.STALE, connection, reason, lastKnown);
    }
    private static SnapshotDevice HyperX(BatteryDevice? current)
    {
        var knownCandidate = Valid(current) && current!.Source == HyperXSource;
        SnapshotReading? reading = knownCandidate ? new(current!.Percentage, SnapshotPrecision.ESTIMATE,
            new(SnapshotSourceKind.HYPERX_SUPERVISED, SnapshotSocSupport.NOT_APPLICABLE, SnapshotOwnership.AMBIGUOUS),
            current.LastReported?.UtcDateTime, null) : null;
        var usable = reading is not null && current!.Availability == BatteryAvailability.Available &&
            current.ObservationState == BatteryObservationState.CandidateAmbiguous && current.Receiver == ReceiverPresence.Detected;
        if (usable) return new(SnapshotDeviceId.HyperX, reading!, SnapshotAvailability.AVAILABLE,
            SnapshotFreshness.UNKNOWN, SnapshotDeviceConnection.UNKNOWN, null, null);
        var reason = current?.Receiver switch
        {
            // Receiver absence cannot establish headset connectivity. v1 has no receiver-absence field.
            ReceiverPresence.Absent => SnapshotUnavailableReason.SOURCE_UNAVAILABLE,
            ReceiverPresence.Ambiguous => SnapshotUnavailableReason.AMBIGUOUS_TARGET,
            _ => current is null ? SnapshotUnavailableReason.NO_READING : current.ObservationState == BatteryObservationState.Stale
                ? SnapshotUnavailableReason.STALE_CACHE : SnapshotUnavailableReason.SOURCE_UNAVAILABLE
        };
        return new(SnapshotDeviceId.HyperX, Empty, reading is null ? SnapshotAvailability.UNAVAILABLE : SnapshotAvailability.STALE,
            reading is null ? SnapshotFreshness.UNKNOWN : SnapshotFreshness.STALE,
            SnapshotDeviceConnection.UNKNOWN, reason, reading);
    }
}

public static class BatterySnapshotJson
{
    public const int MaxPayloadBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never, MaxDepth = 16 };
        options.Converters.Add(new JsonStringEnumConverter<SnapshotDeviceId>(allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<SnapshotPrecision>(allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<SnapshotSourceKind>(allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<SnapshotSocSupport>(allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<SnapshotOwnership>(allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<SnapshotAvailability>(allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<SnapshotFreshness>(allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<SnapshotDeviceConnection>(allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<SnapshotUnavailableReason>(allowIntegerValues: false));
        return options;
    }
    public static byte[] Serialize(BatterySnapshotV1 snapshot)
    {
        Validate(snapshot);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, Options);
        if (bytes.Length > MaxPayloadBytes) throw new InvalidDataException("Snapshot exceeds payload limit.");
        return bytes;
    }
    public static void Validate(BatterySnapshotV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != 1 || snapshot.ServerInstanceId == Guid.Empty || snapshot.Revision < 0 ||
            snapshot.Devices.IsDefault || snapshot.Devices.Length != 3 ||
            !snapshot.Devices.Select(d => d.Id).SequenceEqual(new[] { SnapshotDeviceId.G915, SnapshotDeviceId.G305, SnapshotDeviceId.HyperX }))
            throw new ArgumentException("Invalid snapshot identity, version, revision or device order.");
        Time(snapshot.CapturedAt, snapshot.Synthetic);
        foreach (var device in snapshot.Devices)
        {
            Defined(device.Availability); Defined(device.Freshness); Defined(device.DeviceConnection);
            if (device.UnavailableReason is { } reason) Defined(reason);
            Reading(device.Current, snapshot.Synthetic);
            if (device.LastKnown is { } cached) Reading(cached, snapshot.Synthetic);
            if (device.Id == SnapshotDeviceId.HyperX) {
                foreach (var value in new[] { device.Current, device.LastKnown })
                    if (value?.BatteryPercent is not null && value.Source.Kind != SnapshotSourceKind.HYPERX_SUPERVISED)
                        throw new ArgumentException("HyperX percentage requires known ambiguous source evidence.");
            }
            if (device.Availability == SnapshotAvailability.AVAILABLE ? device.Current.BatteryPercent is null || device.UnavailableReason is not null : device.Current.BatteryPercent is not null)
                throw new ArgumentException("Invalid current availability/value.");
        }
    }
    private static void Reading(SnapshotReading reading, bool synthetic)
    {
        if (reading.BatteryPercent is < 0 or > 100) throw new ArgumentException("Invalid percentage.");
        Defined(reading.Precision); Defined(reading.Source.Kind); Defined(reading.Source.SocSupport); Defined(reading.Source.Ownership);
        if (reading.Source.Kind == SnapshotSourceKind.HYPERX_SUPERVISED &&
            (reading.Precision != SnapshotPrecision.ESTIMATE || reading.Source.Ownership != SnapshotOwnership.AMBIGUOUS || reading.ObservedAt is not null))
            throw new ArgumentException("Invalid HyperX confidence or observation claim.");
        if (reading.Source.Kind == SnapshotSourceKind.GHUB_GATEWAY && reading.ObservedAt is not null)
            throw new ArgumentException("Gateway receipt is not a hardware observation timestamp.");
        Time(reading.ReadAt, synthetic); Time(reading.ObservedAt, synthetic);
    }
    private static void Time(DateTime? time, bool synthetic)
    {
        if (time is { } value && (synthetic || value.Kind != DateTimeKind.Utc)) throw new ArgumentException("Timestamp must be real UTC evidence, absent for synthetic fixtures.");
    }
    private static void Defined<T>(T value) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new ArgumentException("Unknown snapshot enum.");
    }
}
