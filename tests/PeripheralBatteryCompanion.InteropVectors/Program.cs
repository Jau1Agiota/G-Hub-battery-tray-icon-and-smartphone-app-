using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PeripheralBatteryCompanion.Core;
using PeripheralBatteryCompanion.Core.ConnectionFoundations;
using PeripheralBatteryCompanion.Core.Snapshots;
using PeripheralBatteryCompanion.Core.SnapshotService;

// CONTROLLED MODEL INPUTS ONLY. synthetic=false exercises the wire publication path;
// none of these values/timestamps/UUIDs are real peripheral telemetry or authenticated identity.
if (args.Length != 2 || args[0] != "--output") throw new ArgumentException("Usage: --output <new-directory>");
var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Refusing to overwrite existing output.");
Directory.CreateDirectory(output);
var clock = new FixedClock();
var cases = new List<object>();
var hashes = new List<string>();
var publications = new List<(string Id, PublishedSnapshot Value)>();
var index = 1;
var checks = 0;
const string Label = "CONTROLLED TEST INPUT - NOT LIVE TELEMETRY";
const string Gateway = GHubWebSocketBatteryProvider.SourceName;
const string HyperXSource = "Supervised one-shot HID; response ownership ambiguous";
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, NewLine = "\n" };

void Assert(bool value, string code) { checks++; if (!value) throw new InvalidOperationException(code); }
BatteryDevice Logitech(string name, int? value, BatteryPrecision precision = BatteryPrecision.Percentage,
    BatteryAvailability availability = BatteryAvailability.Available, BatteryObservationState observation = BatteryObservationState.Unknown,
    DeviceConnection connection = DeviceConnection.Active, string source = Gateway, bool time = true) =>
    new(name, value, time ? clock.Receipt : null, availability, Label, observation,
        Source: source, Precision: precision, Connection: connection);
BatteryDevice Headset(int? value, bool stale = false) => new("HyperX Cloud III Wireless", value, clock.Receipt,
    value.HasValue && !stale ? BatteryAvailability.Available : BatteryAvailability.Unavailable, Label,
    value.HasValue ? stale ? BatteryObservationState.Stale : BatteryObservationState.CandidateAmbiguous : BatteryObservationState.Unavailable,
    ReceiverPresence.Detected, HyperXSource);
BatteryMonitoringState State(BatteryDevice keyboard, BatteryDevice mouse, BatteryDevice? cachedKeyboard = null, BatteryDevice? cachedMouse = null) =>
    new(new(keyboard, clock.Receipt, mouse), cachedKeyboard, clock.Receipt, cachedMouse);
Guid Instance(int number) => Guid.Parse($"aabbccdd-1234-4000-8000-{number:000000000000}");
BatterySnapshotV1 Project(BatteryMonitoringState state, BatteryDevice? headset, Guid id, LogitechSnapshotOrigin origin = LogitechSnapshotOrigin.Gateway) =>
    BatterySnapshotProjection.Build(state, headset, new(id, 0, null, Synthetic: false), origin);
object? Time(DateTime? value) => value?.ToString("O", CultureInfo.InvariantCulture);
object Reading(SnapshotReading r) => new {
    r.BatteryPercent, Precision = r.Precision.ToString(),
    Source = new { Kind = r.Source.Kind.ToString(), SocSupport = r.Source.SocSupport.ToString(), Ownership = r.Source.Ownership.ToString() },
    ReadAt = Time(r.ReadAt), ObservedAt = Time(r.ObservedAt)
};
object Expected(BatterySnapshotV1 s) => new {
    s.SchemaVersion, s.Synthetic, ServerInstanceId = s.ServerInstanceId.ToString("D"), s.Revision, CapturedAt = Time(s.CapturedAt),
    Devices = s.Devices.Select(d => new {
        Id = d.Id switch { SnapshotDeviceId.G915 => "g915", SnapshotDeviceId.G305 => "g305", _ => "hyperx" },
        Current = Reading(d.Current), Availability = d.Availability.ToString(), Freshness = d.Freshness.ToString(),
        DeviceConnection = d.DeviceConnection.ToString(), UnavailableReason = d.UnavailableReason?.ToString(),
        LastKnown = d.LastKnown is null ? null : Reading(d.LastKnown)
    }).ToArray()
};
void Write(string id, byte[] bytes, bool androidAccepts, bool windowsAccepts, object? expected, string group, string origin) {
    var file = id + ".json";
    File.WriteAllBytes(Path.Combine(output, file), bytes);
    hashes.Add($"{Convert.ToHexStringLower(SHA256.HashData(bytes))}  {file}");
    cases.Add(new { Id = id, File = file, AndroidAccepts = androidAccepts, WindowsAccepts = windowsAccepts, Expected = expected, Group = group, Origin = origin });
}
bool WindowsAccepts(byte[] bytes) {
    try { SnapshotWirePolicy.Read(bytes); return true; }
    catch (Exception e) when (e is FormatException or JsonException or DecoderFallbackException or OverflowException or InvalidOperationException) { return false; }
}
void Emit(string id, PublishedSnapshot value, string group = "independent-input") {
    var bytes = value.Json.ToArray();
    Assert(bytes.SequenceEqual(BatterySnapshotJson.Serialize(value.Snapshot)), id + ": serializer/store mismatch");
    Assert(!value.Snapshot.Synthetic && value.Snapshot.Revision > 0, id + ": wrong wire provenance");
    Assert(WindowsAccepts(bytes), id + ": strict Windows wire reader rejected producer output");
    publications.Add((id, value));
    Write(id, bytes, true, true, Expected(value.Snapshot), group, "Windows projection -> PublishedSnapshotStore -> BatterySnapshotJson.Serialize");
}
PublishedSnapshot Independent(string id, BatteryMonitoringState state, BatteryDevice? headset, LogitechSnapshotOrigin origin = LogitechSnapshotOrigin.Gateway) {
    var instance = Instance(index++);
    using var store = new PublishedSnapshotStore(instance, clock);
    var result = store.Publish(Project(state, headset, instance, origin));
    Assert(ReferenceEquals(result, store.ReadLatest()), id + ": immutable publication read identity changed");
    Emit(id, result);
    return result;
}

var precise = State(Logitech("G915 Keyboard", 99), Logitech("G305 Mouse", 47));
var baseline = Independent("precise-both-hyperx-estimate", precise, Headset(76));
Assert(baseline.Snapshot.Devices[0].Current.Precision == SnapshotPrecision.PRECISE && baseline.Snapshot.Devices[1].Current.Precision == SnapshotPrecision.PRECISE, "precise mapping");
Assert(baseline.Snapshot.Devices[2].Current is { Precision: SnapshotPrecision.ESTIMATE, Source.Ownership: SnapshotOwnership.AMBIGUOUS, ObservedAt: null }, "HyperX confidence");
var approximate = Independent("approximate-both", State(Logitech("G915 Keyboard", 80, BatteryPrecision.Approximate), Logitech("G305 Mouse", 60, BatteryPrecision.Approximate)), null);
Assert(approximate.Snapshot.Devices.Take(2).All(d => d.Current.Precision == SnapshotPrecision.APPROXIMATE && d.Current.Source.SocSupport == SnapshotSocSupport.NOT_SUPPORTED), "approximate/SOC distinction");
var unsupported = Independent("unsupported-both", State(Logitech("G915 Keyboard", null, availability: BatteryAvailability.NotSupported), Logitech("G305 Mouse", null, availability: BatteryAvailability.NotSupported)), null);
Assert(unsupported.Snapshot.Devices.Take(2).All(d => d.Availability == SnapshotAvailability.NOT_SUPPORTED && d.UnavailableReason == SnapshotUnavailableReason.UNSUPPORTED && d.Current.BatteryPercent is null), "unsupported distinction");
var zero = Independent("zero-all", State(Logitech("G915 Keyboard", 0), Logitech("G305 Mouse", 0)), Headset(0));
Assert(zero.Snapshot.Devices.All(d => d.Current.BatteryPercent == 0), "zero lost");
var missing = Independent("null-unavailable-all", State(Logitech("G915 Keyboard", null, availability: BatteryAvailability.Unavailable), Logitech("G305 Mouse", null, availability: BatteryAvailability.Unavailable)), null);
Assert(missing.Snapshot.Devices.All(d => d.Current.BatteryPercent is null && d.Availability == SnapshotAvailability.UNAVAILABLE), "null/unavailable distinction");
var staleState = State(Logitech("G915 Keyboard", null, availability: BatteryAvailability.Unavailable, observation: BatteryObservationState.Stale),
    Logitech("G305 Mouse", null, availability: BatteryAvailability.Unavailable, observation: BatteryObservationState.Stale),
    Logitech("G915 Keyboard", 91), Logitech("G305 Mouse", 39, BatteryPrecision.Approximate));
var stale = Independent("stale-lastknown-all", staleState, Headset(71, stale: true));
Assert(stale.Snapshot.Devices.All(d => d.Current.BatteryPercent is null && d.LastKnown?.BatteryPercent is not null && d.Availability == SnapshotAvailability.STALE), "cached promoted to current");
var absent = Independent("absent-with-lastknown", State(Logitech("G915 Keyboard", null, availability: BatteryAvailability.Unavailable, connection: DeviceConnection.Absent),
    Logitech("G305 Mouse", null, availability: BatteryAvailability.Unavailable, connection: DeviceConnection.Absent), Logitech("G915 Keyboard", 85), Logitech("G305 Mouse", 30)), null);
Assert(absent.Snapshot.Devices.Take(2).All(d => d.Availability == SnapshotAvailability.UNAVAILABLE && d.DeviceConnection == SnapshotDeviceConnection.ABSENT_REPORTED && d.LastKnown is not null), "absence/cache mapping");
var stored = Independent("legacy-g915-observation", State(Logitech("G915 Keyboard", 66, source: ""), Logitech("G305 Mouse", 55, BatteryPrecision.Approximate)), null, LogitechSnapshotOrigin.LegacySqlite);
Assert(stored.Snapshot.Devices[0].Current.Source.Kind == SnapshotSourceKind.GHUB_STORED && stored.Snapshot.Devices[0].Current.ObservedAt is not null, "stored timestamp mapping");
var unknown = Independent("unknown-confidence-both", State(Logitech("G915 Keyboard", 40, BatteryPrecision.Unknown), Logitech("G305 Mouse", 20, BatteryPrecision.Unknown)), null);
Assert(unknown.Snapshot.Devices.Take(2).All(d => d.Current.Precision == SnapshotPrecision.UNKNOWN), "confidence upgraded");
Independent("null-receipt-times", State(Logitech("G915 Keyboard", 30, time: false), Logitech("G305 Mouse", 10, time: false)), null);

var sequenceId = Instance(100);
using (var store = new PublishedSnapshotStore(sequenceId, clock)) {
    var first = store.Publish(Project(precise, Headset(76), sequenceId));
    Emit("sequence-first", first, "publication-sequence");
    Emit("sequence-repeat", store.ReadLatest()!, "publication-sequence");
    Assert(ReferenceEquals(first, store.ReadLatest()), "repeated read changed publication");
    clock.Advance();
    var second = store.Publish(Project(precise, Headset(76), sequenceId));
    Assert(second.Snapshot.Revision == first.Snapshot.Revision + 1, "revision did not advance");
    Assert(first.Snapshot.Revision == 1 && first.Json.SequenceEqual(BatterySnapshotJson.Serialize(first.Snapshot)), "prior publication mutated");
    Emit("sequence-second", second, "publication-sequence");
}
using (var restarted = new PublishedSnapshotStore(Instance(101), clock)) {
    var value = restarted.Publish(Project(precise, Headset(76), Instance(101)));
    Assert(value.Snapshot.Revision == 1 && value.Snapshot.ServerInstanceId != sequenceId, "publisher restart scope");
    Emit("sequence-restarted", value, "publication-sequence");
}

var original = baseline.Json.ToArray();
void Mutant(string id, Action<JsonObject> edit, bool androidAccepts = false, bool windowsAccepts = false) {
    var node = JsonNode.Parse(original)!.AsObject(); edit(node);
    var bytes = JsonSerializer.SerializeToUtf8Bytes(node);
    Assert(WindowsAccepts(bytes) == windowsAccepts, id + ": unexpected Windows policy result");
    Write(id, bytes, androidAccepts, windowsAccepts, null, androidAccepts != windowsAccepts ? "known-policy-difference" : "negative-mutation", "Controlled mutation of exact Windows-emitted bytes");
}
JsonObject Current(JsonObject node, int i) => node["devices"]![i]!["current"]!.AsObject();
Mutant("reject-unknown-enum", n => Current(n, 0)["precision"] = "CERTAIN");
Mutant("reject-invalid-timestamp", n => n["capturedAt"] = "2026-02-30T12:00:00Z");
Mutant("reject-nonutc-timestamp", n => n["capturedAt"] = "2026-10-31T12:00:00+00:00");
Mutant("reject-hyperx-precise", n => Current(n, 2)["precision"] = "PRECISE");
Mutant("reject-hyperx-attributed", n => Current(n, 2)["source"]!["ownership"] = "ATTRIBUTED");
Mutant("reject-hyperx-gateway", n => Current(n, 2)["source"]!["kind"] = "GHUB_GATEWAY");
Mutant("reject-gateway-observation", n => Current(n, 0)["observedAt"] = "2026-10-31T12:00:00Z");
Mutant("reject-null-available", n => Current(n, 0)["batteryPercent"] = null);
Mutant("policy-uppercase-uuid", n => n["serverInstanceId"] = baseline.Snapshot.ServerInstanceId.ToString("D").ToUpperInvariant(), androidAccepts: true);
void Raw(string id, byte[] bytes, bool androidAccepts = false, bool windowsAccepts = false) {
    Assert(WindowsAccepts(bytes) == windowsAccepts, id + ": unexpected Windows raw-reader result");
    Write(id, bytes, androidAccepts, windowsAccepts, null, androidAccepts != windowsAccepts ? "known-policy-difference" : "negative-mutation", "Controlled byte mutation of Windows-emitted baseline");
}
Raw("reject-malformed-utf8", [0xc3, 0x28]);
var text = Encoding.UTF8.GetString(original);
Assert(text.Contains("\"schemaVersion\":1,", StringComparison.Ordinal), "duplicate mutation seam not found");
Raw("reject-duplicate-key", Encoding.UTF8.GetBytes(text.Replace("\"schemaVersion\":1,", "\"schemaVersion\":1,\"schemaVersion\":1,", StringComparison.Ordinal)));
Raw("reject-oversized", Enumerable.Repeat((byte)' ', BatterySnapshotJson.MaxPayloadBytes + 1).Concat(original).ToArray());
Assert(text.Contains("\"revision\":1", StringComparison.Ordinal), "revision mutation seam not found");
Raw("policy-negative-zero", Encoding.UTF8.GetBytes(text.Replace("\"revision\":1", "\"revision\":-0", StringComparison.Ordinal)), androidAccepts: true);

var manifest = new {
    Suite = "E2-1", InputKind = Label, WireSynthetic = false,
    WindowsBaseCommit = "ac78aa3eccc7c16c870d0d09f565f631f4a04dd5",
    Producer = "Actual Core ProjectReference; no provider, socket, native security or authenticated session",
    Cases = cases
};
var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, json);
Assert(json.NewLine == "\n", "manifest newline policy must be explicit LF");
Assert(manifestBytes.Contains((byte)'\n'), "manifest must contain LF line breaks");
Assert(!Encoding.UTF8.GetString(manifestBytes).Contains("\r\n", StringComparison.Ordinal), "manifest must not contain CRLF sequences");
File.WriteAllBytes(Path.Combine(output, "manifest.json"), manifestBytes);
File.WriteAllText(Path.Combine(output, "SHA256SUMS"), string.Join('\n', hashes) + "\n", new UTF8Encoding(false));
Console.WriteLine($"E2 portable producer: {publications.Count} Windows-emitted publications; {cases.Count - publications.Count} controlled mutations; {checks} assertions passed.");
Console.WriteLine("CONTROLLED INPUTS ONLY; no live telemetry, connectivity or native security validation.");

sealed class FixedClock : TimeProvider {
    private DateTimeOffset now = DateTimeOffset.Parse("2026-10-31T12:00:00.7654321Z", CultureInfo.InvariantCulture);
    public DateTimeOffset Receipt => DateTimeOffset.Parse("2026-10-31T11:59:00.1234567Z", CultureInfo.InvariantCulture);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance() => now = now.AddTicks(1);
}
