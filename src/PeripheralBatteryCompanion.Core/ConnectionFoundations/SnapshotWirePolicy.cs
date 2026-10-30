using System.Text.Json;

namespace PeripheralBatteryCompanion.Core.ConnectionFoundations;

// Strict metadata reader only. It does not connect, authenticate or adapt data to production models.
public static class SnapshotWirePolicy
{
    public const int MaxBytes = 65536;
    public static JsonElement Read(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxBytes) throw new FormatException();
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        var root = doc.RootElement;
        Shape(root, "schemaVersion synthetic serverInstanceId revision capturedAt devices");
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            !Guid.TryParseExact(Text(root, "serverInstanceId"), "D", out var id) || id == Guid.Empty || id.ToString("D") != Text(root, "serverInstanceId") ||
            root.GetProperty("revision").GetInt64() < 0) throw new FormatException();
        var synthetic = root.GetProperty("synthetic").GetBoolean();
        Time(root.GetProperty("capturedAt"), synthetic);
        var devices = root.GetProperty("devices");
        if (devices.ValueKind != JsonValueKind.Array || devices.GetArrayLength() != 3) throw new FormatException();
        var ids = new[] { "g915", "g305", "hyperx" };
        for (int i = 0; i < 3; i++) {
            var d = devices[i]; Shape(d, "id current availability freshness deviceConnection unavailableReason lastKnown");
            if (Text(d, "id") != ids[i]) throw new FormatException();
            One(d, "availability", "AVAILABLE UNAVAILABLE STALE NOT_SUPPORTED");
            One(d, "freshness", "RECENT_READ STALE UNKNOWN");
            One(d, "deviceConnection", "ACTIVE_REPORTED ABSENT_REPORTED UNKNOWN");
            if (d.GetProperty("unavailableReason").ValueKind != JsonValueKind.Null)
                One(d, "unavailableReason", "NO_READING SOURCE_UNAVAILABLE DEVICE_ABSENT STALE_CACHE AMBIGUOUS_TARGET SAFETY_STOP MONITORING_OFF UNSUPPORTED");
            Reading(d.GetProperty("current"), synthetic, i == 2);
            var number = d.GetProperty("current").GetProperty("batteryPercent").ValueKind != JsonValueKind.Null;
            if ((Text(d, "availability") == "AVAILABLE") != number ||
                number && d.GetProperty("unavailableReason").ValueKind != JsonValueKind.Null) throw new FormatException();
            if (d.GetProperty("lastKnown").ValueKind != JsonValueKind.Null) Reading(d.GetProperty("lastKnown"), synthetic, i == 2);
        }
        return root.Clone(); // preserves all source/confidence/receipt/observation/lastKnown fields
    }
    private static void Reading(JsonElement r, bool synthetic, bool hyperx)
    {
        Shape(r, "batteryPercent precision source readAt observedAt");
        if (r.GetProperty("batteryPercent").ValueKind != JsonValueKind.Null &&
            r.GetProperty("batteryPercent").GetInt32() is < 0 or > 100) throw new FormatException();
        One(r, "precision", "PRECISE APPROXIMATE ESTIMATE UNKNOWN");
        var s = r.GetProperty("source"); Shape(s, "kind socSupport ownership");
        One(s, "kind", "GHUB_GATEWAY HYPERX_SUPERVISED GHUB_STORED UNKNOWN");
        One(s, "socSupport", "SUPPORTED NOT_SUPPORTED UNKNOWN NOT_APPLICABLE");
        One(s, "ownership", "ATTRIBUTED AMBIGUOUS UNKNOWN");
        var kind = Text(s, "kind");
        if (hyperx && r.GetProperty("batteryPercent").ValueKind != JsonValueKind.Null && kind != "HYPERX_SUPERVISED") throw new FormatException();
        if (kind == "HYPERX_SUPERVISED" && (Text(r, "precision") != "ESTIMATE" || Text(s, "ownership") != "AMBIGUOUS")) throw new FormatException();
        if (kind is "GHUB_GATEWAY" or "HYPERX_SUPERVISED" && r.GetProperty("observedAt").ValueKind != JsonValueKind.Null) throw new FormatException();
        Time(r.GetProperty("readAt"), synthetic); Time(r.GetProperty("observedAt"), synthetic);
    }
    private static void Shape(JsonElement x, string fields)
    {
        if (x.ValueKind != JsonValueKind.Object) throw new FormatException();
        var actual = x.EnumerateObject().Select(p => p.Name).Order().ToArray();
        if (!actual.SequenceEqual(fields.Split(' ').Order())) throw new FormatException();
    }
    private static string Text(JsonElement x, string key) => x.GetProperty(key).GetString() ?? throw new FormatException();
    private static void One(JsonElement x, string key, string values) { if (!values.Split(' ').Contains(Text(x,key))) throw new FormatException(); }
    private static void Time(JsonElement x, bool synthetic)
    {
        if (x.ValueKind == JsonValueKind.Null) return;
        var text = x.GetString();
        if (synthetic || text is null || !text.EndsWith('Z') || !DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _)) throw new FormatException();
    }
}
