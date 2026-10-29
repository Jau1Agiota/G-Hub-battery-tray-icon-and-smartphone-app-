using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using PeripheralBatteryCompanion.Core;
using PeripheralBatteryCompanion.Core.Snapshots;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class BatterySnapshotTests
{
    private static readonly DateTimeOffset Receipt = DateTimeOffset.Parse("2026-10-01T12:00:00+02:00");
    private static readonly SnapshotMetadata Metadata = new(Guid.Parse("00000000-0000-4000-8000-000000000001"), 7, Receipt.AddMinutes(1));
    private static BatteryDevice Logitech(string name = "G915 Keyboard", int? value = 99, BatteryPrecision precision = BatteryPrecision.Percentage) =>
        new(name, value, Receipt, BatteryAvailability.Available, "synthetic only", Source: GHubWebSocketBatteryProvider.SourceName,
            Precision: precision, Connection: DeviceConnection.Active);
    private static BatteryDevice HyperX(int? value = 76) => new("HyperX Cloud III Wireless", value, Receipt, BatteryAvailability.Available,
        "synthetic only", BatteryObservationState.CandidateAmbiguous, ReceiverPresence.Detected,
        "Supervised one-shot HID; response ownership ambiguous");
    private static BatteryMonitoringState State(BatteryDevice? keyboard = null, BatteryDevice? mouse = null, BatteryDevice? cached = null) =>
        new(new(keyboard ?? Logitech(), null, mouse), cached, null);

    [Fact]
    public void ProjectsAllThreeWithStableIdsWithoutUsingDisplayNamesAsIdentity()
    {
        var snapshot = BatterySnapshotProjection.Build(State(Logitech("untrusted name"), Logitech("untrusted name", 90, BatteryPrecision.Approximate)), HyperX(), Metadata);
        Assert.Equal(new[] { SnapshotDeviceId.G915, SnapshotDeviceId.G305, SnapshotDeviceId.HyperX }, snapshot.Devices.Select(d => d.Id));
        Assert.Equal(new int?[] { 99, 90, 76 }, snapshot.Devices.Select(d => d.Current.BatteryPercent));
        Assert.Equal(SnapshotPrecision.APPROXIMATE, snapshot.Devices[1].Current.Precision);
        Assert.Equal(SnapshotPrecision.ESTIMATE, snapshot.Devices[2].Current.Precision);
        Assert.All(snapshot.Devices, d => Assert.Equal(SnapshotFreshness.UNKNOWN, d.Freshness));
    }
    [Theory]
    [InlineData(BatteryPrecision.Percentage, SnapshotPrecision.PRECISE, SnapshotSocSupport.SUPPORTED)]
    [InlineData(BatteryPrecision.Approximate, SnapshotPrecision.APPROXIMATE, SnapshotSocSupport.NOT_SUPPORTED)]
    [InlineData(BatteryPrecision.Unknown, SnapshotPrecision.UNKNOWN, SnapshotSocSupport.UNKNOWN)]
    public void G305AccuracyComesFromEachCachedResponse(BatteryPrecision input, SnapshotPrecision expected, SnapshotSocSupport soc)
    {
        var snapshot = BatterySnapshotProjection.Build(State(mouse: Logitech("G305 Mouse", 90, input)), null, Metadata);
        Assert.Equal(expected, snapshot.Devices[1].Current.Precision);
        Assert.Equal(soc, snapshot.Devices[1].Current.Source.SocSupport);
        Assert.Equal(90, snapshot.Devices[1].Current.BatteryPercent);
    }
    [Fact]
    public void MissingAllDevicesProducesNullsNotZeroOrDemo()
    {
        var snapshot = BatterySnapshotProjection.Build(null, null, Metadata);
        Assert.False(snapshot.Synthetic);
        Assert.All(snapshot.Devices, d => {
            Assert.Null(d.Current.BatteryPercent); Assert.Null(d.Current.ReadAt); Assert.Null(d.Current.ObservedAt);
            Assert.Null(d.LastKnown); Assert.Equal(SnapshotUnavailableReason.NO_READING, d.UnavailableReason);
            Assert.Equal(SnapshotDeviceConnection.UNKNOWN, d.DeviceConnection);
        });
    }
    [Theory]
    [InlineData(null, false)] [InlineData(0, true)] [InlineData(100, true)] [InlineData(-1, false)] [InlineData(101, false)]
    public void PercentagesAreValidatedWithoutTurningMissingOrInvalidIntoZero(int? value, bool available)
    {
        var device = BatterySnapshotProjection.Build(State(Logitech(value: value)), null, Metadata).Devices[0];
        Assert.Equal(available ? value : null, device.Current.BatteryPercent);
        Assert.Equal(available ? SnapshotAvailability.AVAILABLE : SnapshotAvailability.UNAVAILABLE, device.Availability);
    }
    [Fact]
    public void GatewayAndHyperXReceiptTimesAreNeverHardwareObservationTimes()
    {
        var snapshot = BatterySnapshotProjection.Build(State(), HyperX(), Metadata);
        Assert.Equal(Metadata.CapturedAt!.Value.UtcDateTime, snapshot.CapturedAt);
        foreach (var id in new[] { 0, 2 }) {
            Assert.Equal(Receipt.UtcDateTime, snapshot.Devices[id].Current.ReadAt);
            Assert.Null(snapshot.Devices[id].Current.ObservedAt);
        }
    }
    [Fact]
    public void LegacySqliteNeedsExplicitOriginAndKeepsObservationSeparateFromSelect()
    {
        var observed = Receipt.AddHours(-1);
        var stored = new BatteryDevice("G915 Keyboard", 50, observed, BatteryAvailability.Available, "synthetic stored report");
        var state = new BatteryMonitoringState(new(stored, Receipt), stored, Receipt);
        var snapshot = BatterySnapshotProjection.Build(state, null, Metadata, LogitechSnapshotOrigin.LegacySqlite);
        Assert.Equal(SnapshotSourceKind.GHUB_STORED, snapshot.Devices[0].Current.Source.Kind);
        Assert.Equal(Receipt.UtcDateTime, snapshot.Devices[0].Current.ReadAt);
        Assert.Equal(observed.UtcDateTime, snapshot.Devices[0].Current.ObservedAt);
        var unproven = BatterySnapshotProjection.Build(state, null, Metadata).Devices[0].Current;
        Assert.Equal(SnapshotSourceKind.UNKNOWN, unproven.Source.Kind); Assert.Null(unproven.ObservedAt);
    }
    [Fact]
    public void LegacyCacheDoesNotBorrowLatestSelectReceipt()
    {
        var cached = new BatteryDevice("G915 Keyboard", 50, Receipt.AddHours(-1), BatteryAvailability.Available, "stored");
        var failed = cached with { Percentage = null, Availability = BatteryAvailability.Unavailable };
        var state = new BatteryMonitoringState(new(failed, Receipt.AddMinutes(20)), cached, Receipt.AddMinutes(20));
        var device = BatterySnapshotProjection.Build(state, null, Metadata, LogitechSnapshotOrigin.LegacySqlite).Devices[0];
        Assert.Null(device.Current.BatteryPercent);
        Assert.NotNull(device.LastKnown); Assert.Null(device.LastKnown.ReadAt);
        Assert.Equal(cached.LastReported!.Value.UtcDateTime, device.LastKnown.ObservedAt);
    }
    [Fact]
    public void ErrorAndRecoveryKeepCurrentSeparateFromLastKnownAndNeverRetimestampCache()
    {
        var good = Logitech();
        var failed = good with { Percentage = null, Availability = BatteryAvailability.Unavailable, Status = "error, private details" };
        var device = BatterySnapshotProjection.Build(State(failed, cached: good), null, Metadata).Devices[0];
        Assert.Null(device.Current.BatteryPercent); Assert.Equal(SnapshotAvailability.STALE, device.Availability);
        Assert.Equal(SnapshotUnavailableReason.SOURCE_UNAVAILABLE, device.UnavailableReason);
        Assert.Equal(99, device.LastKnown!.BatteryPercent); Assert.Equal(Receipt.UtcDateTime, device.LastKnown.ReadAt);
        var recovered = BatterySnapshotProjection.Build(State(good), null, Metadata).Devices[0];
        Assert.Equal(SnapshotAvailability.AVAILABLE, recovered.Availability); Assert.Null(recovered.LastKnown);
    }
    [Fact]
    public void AbsentLogitechDeviceNeverUsesCachedValueAsCurrent()
    {
        var absent = Logitech() with { Connection = DeviceConnection.Absent };
        var device = BatterySnapshotProjection.Build(State(absent, cached: Logitech()), null, Metadata).Devices[0];
        Assert.Null(device.Current.BatteryPercent); Assert.Equal(SnapshotDeviceConnection.ABSENT_REPORTED, device.DeviceConnection);
        Assert.Equal(SnapshotUnavailableReason.DEVICE_ABSENT, device.UnavailableReason); Assert.NotNull(device.LastKnown);
    }
    [Fact]
    public void UnsupportedIsDistinctFromNoReading()
    {
        var device = BatterySnapshotProjection.Build(State(mouse: BatteryDevice.UnsupportedMouse), null, Metadata).Devices[1];
        Assert.Equal(SnapshotAvailability.NOT_SUPPORTED, device.Availability); Assert.Equal(SnapshotUnavailableReason.UNSUPPORTED, device.UnavailableReason);
    }
    [Fact]
    public void HyperXAlwaysRetainsAmbiguityDespiteDefaultPercentagePrecision()
    {
        var device = BatterySnapshotProjection.Build(null, HyperX(), Metadata).Devices[2];
        Assert.Equal(SnapshotPrecision.ESTIMATE, device.Current.Precision);
        Assert.Equal(SnapshotOwnership.AMBIGUOUS, device.Current.Source.Ownership);
        Assert.Equal(SnapshotDeviceConnection.UNKNOWN, device.DeviceConnection);
    }
    [Theory]
    [InlineData(ReceiverPresence.Absent, SnapshotUnavailableReason.SOURCE_UNAVAILABLE)]
    [InlineData(ReceiverPresence.Ambiguous, SnapshotUnavailableReason.AMBIGUOUS_TARGET)]
    public void HyperXRemovalAndAmbiguityOnlyExposeStaleLastKnown(ReceiverPresence receiver, SnapshotUnavailableReason reason)
    {
        var device = BatterySnapshotProjection.Build(null, HyperX() with { Receiver = receiver, ObservationState = BatteryObservationState.Stale, Availability = BatteryAvailability.Unavailable }, Metadata).Devices[2];
        Assert.Null(device.Current.BatteryPercent); Assert.Equal(SnapshotAvailability.STALE, device.Availability);
        Assert.Equal(reason, device.UnavailableReason); Assert.Equal(SnapshotDeviceConnection.UNKNOWN, device.DeviceConnection);
        Assert.Equal(SnapshotPrecision.ESTIMATE, device.LastKnown!.Precision); Assert.Equal(Receipt.UtcDateTime, device.LastKnown.ReadAt);
    }
    [Fact]
    public void UnknownHyperXSourceAndConfirmedEnumAreNotUpgradedToVerified()
    {
        var unknown = BatterySnapshotProjection.Build(null, HyperX() with { Source = "not proven", ObservationState = BatteryObservationState.ConfirmedFresh }, Metadata).Devices[2];
        Assert.Null(unknown.Current.BatteryPercent); Assert.Null(unknown.LastKnown);
        var confirmed = BatterySnapshotProjection.Build(null, HyperX() with { ObservationState = BatteryObservationState.ConfirmedFresh }, Metadata).Devices[2];
        Assert.Null(confirmed.Current.BatteryPercent); Assert.Equal(SnapshotPrecision.ESTIMATE, confirmed.LastKnown!.Precision);
    }
    [Fact]
    public void SerializationIsDeterministicBoundedAndCannotLeakFreeText()
    {
        var privateInput = Logitech("secret-name") with { Status = "C:/private/user token=secret", Source = "untrusted-source-secret" };
        var snapshot = BatterySnapshotProjection.Build(State(privateInput), HyperX(), Metadata);
        var bytes = BatterySnapshotJson.Serialize(snapshot);
        Assert.Equal(bytes, BatterySnapshotJson.Serialize(snapshot));
        Assert.InRange(bytes.Length, 1, BatterySnapshotJson.MaxPayloadBytes);
        var json = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("secret", json); Assert.DoesNotContain("C:/", json);
        using var doc = JsonDocument.Parse(bytes);
        Assert.Equal(new[] { "schemaVersion", "synthetic", "serverInstanceId", "revision", "capturedAt", "devices" }, doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("g915", doc.RootElement.GetProperty("devices")[0].GetProperty("id").GetString());
        Assert.EndsWith("Z", doc.RootElement.GetProperty("capturedAt").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("devices")[1].GetProperty("current").GetProperty("batteryPercent").ValueKind);
    }
    [Fact]
    public void SyntheticSnapshotsMustNotInventTimes()
    {
        Assert.Throws<ArgumentException>(() => BatterySnapshotProjection.Build(State(), HyperX(), Metadata with { Synthetic = true }));
        var snapshot = BatterySnapshotProjection.Build(null, null, Metadata with { Synthetic = true, CapturedAt = null });
        Assert.True(snapshot.Synthetic); Assert.Null(snapshot.CapturedAt);
    }
    [Fact]
    public void SerializerRejectsInvalidVersionIdentityOrderAndEnum()
    {
        var snapshot = BatterySnapshotProjection.Build(null, null, Metadata);
        Assert.Throws<ArgumentException>(() => BatterySnapshotJson.Serialize(snapshot with { SchemaVersion = 2 }));
        Assert.Throws<ArgumentException>(() => BatterySnapshotJson.Serialize(snapshot with { Devices = [snapshot.Devices[0], snapshot.Devices[0], snapshot.Devices[2]] }));
        Assert.Throws<ArgumentException>(() => BatterySnapshotJson.Serialize(snapshot with { Revision = -1 }));
        Assert.Throws<ArgumentException>(() => BatterySnapshotJson.Serialize(snapshot with { Devices = snapshot.Devices.SetItem(0, snapshot.Devices[0] with { Availability = (SnapshotAvailability)99 }) }));
    }
    [Fact]
    public void RejectsInvalidOutputPercentageAndFalseHyperXConfidence()
    {
        var snapshot = BatterySnapshotProjection.Build(null, HyperX(), Metadata);
        var hx = snapshot.Devices[2];
        Assert.Throws<ArgumentException>(() => BatterySnapshotJson.Serialize(snapshot with { Devices = snapshot.Devices.SetItem(2, hx with { Current = hx.Current with { BatteryPercent = 101 } }) }));
        Assert.Throws<ArgumentException>(() => BatterySnapshotJson.Serialize(snapshot with { Devices = snapshot.Devices.SetItem(2, hx with { Current = hx.Current with { Precision = SnapshotPrecision.PRECISE } }) }));
    }
    [Fact]
    public void SerializerRejectsInventedGatewayObservationAndUnknownHyperXSource()
    {
        var snapshot = BatterySnapshotProjection.Build(State(), HyperX(), Metadata);
        var keyboard = snapshot.Devices[0]; var headset = snapshot.Devices[2];
        Assert.Throws<ArgumentException>(() => BatterySnapshotJson.Serialize(snapshot with { Devices = snapshot.Devices.SetItem(0,
            keyboard with { Current = keyboard.Current with { ObservedAt = Receipt.UtcDateTime } }) }));
        Assert.Throws<ArgumentException>(() => BatterySnapshotJson.Serialize(snapshot with { Devices = snapshot.Devices.SetItem(2,
            headset with { Current = headset.Current with { Source = headset.Current.Source with { Kind = SnapshotSourceKind.UNKNOWN } } }) }));
    }
    [Fact]
    public void RepeatedProjectionHasNoProviderOrClockDependencyAndDoesNotMutateCachedState()
    {
        var state = State(); var hx = HyperX();
        var before = state;
        var first = BatterySnapshotProjection.Build(state, hx, Metadata);
        for (var i = 0; i < 100; i++) Assert.Equal(BatterySnapshotJson.Serialize(first), BatterySnapshotJson.Serialize(BatterySnapshotProjection.Build(state, hx, Metadata)));
        Assert.Equal(before, state); Assert.Equal(99, state.Current.Keyboard.Percentage);
        var parameters = typeof(BatterySnapshotProjection).GetMethod("Build")!.GetParameters().Select(p => p.ParameterType).ToArray();
        Assert.Equal(new[] { typeof(BatteryMonitoringState), typeof(BatteryDevice), typeof(SnapshotMetadata), typeof(LogitechSnapshotOrigin) }, parameters);
    }
}
