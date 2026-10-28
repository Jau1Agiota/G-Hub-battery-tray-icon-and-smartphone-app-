using System.Text;
using System.Text.Json;

namespace PeripheralBatteryCompanion.G305GHubInspector;

public enum InspectionStatus { Success, MissingOrInaccessible, SqliteFailure, UnsupportedSchema, Empty, InvalidJson, LimitExceeded, InvalidPayload, UnexpectedFailure }
public sealed record FieldFinding(string Path, string Category, string Type);
public sealed record InspectionReport(InspectionStatus Status, bool Opened = false, bool SchemaSupported = false,
    bool Parsed = false, int References = 0, int? Percentage = null, string? Level = null,
    bool AssociatedBattery = false, bool ConflictingValues = false, bool Limited = false,
    IReadOnlyList<FieldFinding>? Findings = null, int? SqliteCode = null, string PayloadType = "not inspected",
    int RowsInspected = 0, bool AdditionalRows = false)
{
    public int ExitCode => Status == InspectionStatus.Success ? 0 : Status == InspectionStatus.MissingOrInaccessible ? 3 : 1;
    public string Format()
    {
        var output = new StringBuilder();
        output.AppendLine("G305 G HUB read-only inspection v1");
        output.AppendLine($"Result: {Status}; SQLite opened: {Opened}; supported data(_id,file): {SchemaSupported}");
        output.AppendLine($"Rows inspected: {RowsInspected} (latest only); payload type: {PayloadType}; latest JSON parsed: {Parsed}; other rows present: {AdditionalRows}");
        output.AppendLine($"Candidate G305 references: {References}; structurally associated battery field: {AssociatedBattery}");
        output.AppendLine($"Attributed percentage field: {(Percentage.HasValue ? Percentage + "% (cached candidate)" : "not established")}");
        output.AppendLine($"Attributed qualitative level: {Level ?? "not established"}");
        output.AppendLine($"Conflicting attributed values: {ConflictingValues}; inspection/output limited: {Limited}; SQLite error: {SqliteCode?.ToString() ?? "none"}");
        foreach (var finding in Findings ?? []) output.AppendLine($"{finding.Category}: {finding.Path} [{finding.Type}]");
        output.AppendLine("Paths contain allowlisted keys only; other keys/array indices are redacted. No arbitrary values or database dump.");
        output.AppendLine("Latest-row structural evidence only; other rows/storage may differ. Configuration/model references are not live telemetry.");
        output.AppendLine("No hardware access or HID reports. Battery accuracy, freshness and device connectivity are not established.");
        return output.ToString().TrimEnd();
    }
}

public static class JsonInspection
{
    public const int MaxBytes = 4 * 1024 * 1024, MaxDepth = 24, MaxNodes = 20000, MaxFindings = 40, MaxPath = 120;
    private static readonly HashSet<string> SafeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "devices", "device", "battery", "batteryStatus", "batteryLevel", "battery_level", "batteryPercentage", "battery_percentage",
        "percentage", "level", "status", "telemetry", "state", "modelId", "baseModelId", "signature", "slotId", "model",
        "profiles", "profile", "settings", "configuration", "config", "name", "type", "value", "time", "timestamp",
        "low_battery_notifications_enabled", "battery/g305/percentage", "battery/g305/level", "battery/g305/status",
        "battery/g915/percentage", "battery/g915/warning", "g305", "g915"
    };
    private static readonly HashSet<string> Levels = new(StringComparer.OrdinalIgnoreCase) { "critical", "low", "medium", "high", "full", "unknown" };
    private static bool ContainsG305(string text) => text.Contains("g305", StringComparison.OrdinalIgnoreCase);
    private static bool BatteryKey(string key) => key.Contains("battery", StringComparison.OrdinalIgnoreCase) || key.Equals("percentage", StringComparison.OrdinalIgnoreCase);
    private static bool IsConfig(string key) => key.Equals("profiles", StringComparison.OrdinalIgnoreCase) || key.Equals("profile", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("settings", StringComparison.OrdinalIgnoreCase) || key.Equals("configuration", StringComparison.OrdinalIgnoreCase) || key.Equals("config", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("low_battery_notifications_enabled", StringComparison.OrdinalIgnoreCase);
    private static string Safe(string key) => SafeKeys.Contains(key) ? key.ToLowerInvariant().Replace("~", "~0").Replace("/", "~1") : BatteryKey(key) ? "<battery-key>" : "<key>";
    private static bool ExactModel(JsonElement value) => value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), "g305", StringComparison.OrdinalIgnoreCase);
    public static InspectionReport Inspect(ReadOnlyMemory<byte> json)
    {
        if (json.Length > MaxBytes) return new(InspectionStatus.LimitExceeded, Limited: true);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth });
            if (document.RootElement.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array) return new(InspectionStatus.InvalidJson);
            var findings = new List<FieldFinding>(); var dedupe = new HashSet<FieldFinding>();
            var percentages = new HashSet<int>(); var levels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var findingRequests = 0; var nodes = 0; var references = 0; var associated = false; var limited = false;
            void Add(string path, string category, JsonValueKind kind)
            {
                if (++findingRequests > MaxFindings) { limited = true; return; }
                if (path.Length > MaxPath) limited = true;
                var item = new FieldFinding(path.Length <= MaxPath ? path : "<path-limit>", category, kind is JsonValueKind.True or JsonValueKind.False ? "Boolean" : kind.ToString());
                if (!dedupe.Add(item)) return;
                if (findings.Count < MaxFindings) findings.Add(item); else limited = true;
            }
            void Walk(JsonElement node, string path, bool device, bool battery, bool config)
            {
                if (++nodes > MaxNodes) { limited = true; return; }
                if (node.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in node.EnumerateArray()) { if (nodes >= MaxNodes) { limited = true; break; } Walk(item, path + "[*]", device, battery, config); }
                    return;
                }
                if (node.ValueKind != JsonValueKind.Object) return;
                // Attribution uses direct identity fields, never profile names, signatures or an arbitrary G305 string.
                var identified = false; var hasIdentity = false; var conflictingIdentity = false; var identityBudget = 0;
                foreach (var identity in node.EnumerateObject())
                {
                    if (++identityBudget > MaxNodes - nodes) { limited = true; return; }
                    if (identity.Name.Equals("modelId", StringComparison.OrdinalIgnoreCase) || identity.Name.Equals("baseModelId", StringComparison.OrdinalIgnoreCase))
                    {
                        hasIdentity = true;
                        if (ExactModel(identity.Value)) identified = true; else conflictingIdentity = true;
                    }
                }
                var localDevice = identified && !conflictingIdentity || device && !hasIdentity;
                foreach (var property in node.EnumerateObject())
                {
                    if (++nodes > MaxNodes) { limited = true; break; }
                    var key = property.Name; var value = property.Value; var childPath = path + "/" + Safe(key);
                    var keyReference = ContainsG305(key);
                    var valueReference = value.ValueKind == JsonValueKind.String && ContainsG305(value.GetString()!);
                    if (keyReference || valueReference)
                    {
                        references++;
                        Add(childPath, keyReference && valueReference ? "Model/configuration key+value reference" :
                            keyReference ? "Model/configuration key reference" : "Model/configuration value reference", value.ValueKind);
                    }
                    var childConfig = config || IsConfig(key);
                    var explicitEntry = key.Equals("battery/g305/percentage", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("battery/g305/level", StringComparison.OrdinalIgnoreCase) || key.Equals("battery/g305/status", StringComparison.OrdinalIgnoreCase);
                    var batteryBranch = key.Equals("battery", StringComparison.OrdinalIgnoreCase) || key.Equals("batteryStatus", StringComparison.OrdinalIgnoreCase);
                    var isBattery = BatteryKey(key) || battery && (key.Equals("level", StringComparison.OrdinalIgnoreCase) || key.Equals("status", StringComparison.OrdinalIgnoreCase));
                    var attributed = !childConfig && (localDevice || explicitEntry);
                    if (isBattery)
                    {
                        Add(childPath, childConfig ? "Configuration field" : attributed ? "G305 possible battery/status" : "Unattributed battery/status", value.ValueKind);
                        if (attributed) associated = true;
                    }
                    var numericKey = key.Equals("batteryPercentage", StringComparison.OrdinalIgnoreCase) || key.Equals("battery_percentage", StringComparison.OrdinalIgnoreCase) ||
                        battery && key.Equals("percentage", StringComparison.OrdinalIgnoreCase);
                    var levelKey = key.Equals("batteryLevel", StringComparison.OrdinalIgnoreCase) || key.Equals("battery_level", StringComparison.OrdinalIgnoreCase) ||
                        battery && (key.Equals("level", StringComparison.OrdinalIgnoreCase) || key.Equals("status", StringComparison.OrdinalIgnoreCase));
                    if (attributed && numericKey && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number is >= 0 and <= 100) percentages.Add(number);
                    if (attributed && levelKey && value.ValueKind == JsonValueKind.String && Levels.Contains(value.GetString()!)) levels.Add(value.GetString()!.ToLowerInvariant());
                    // Device scope follows only recognized telemetry branches, not arbitrary siblings or another device object.
                    var carry = attributed && (batteryBranch || key.Equals("telemetry", StringComparison.OrdinalIgnoreCase) || key.Equals("state", StringComparison.OrdinalIgnoreCase) || explicitEntry);
                    Walk(value, childPath, carry, carry && (battery || batteryBranch || explicitEntry), childConfig);
                }
            }
            Walk(document.RootElement, "$", false, false, false);
            var conflict = percentages.Count > 1 || levels.Count > 1;
            // Partial scans cannot establish uniqueness; never report a value from an incomplete traversal.
            return new(InspectionStatus.Success, Parsed: true, References: references, AssociatedBattery: associated,
                Percentage: !limited && !conflict && percentages.Count == 1 ? percentages.Single() : null,
                Level: !limited && !conflict && levels.Count == 1 ? levels.Single() : null,
                ConflictingValues: conflict, Limited: limited, Findings: findings);
        }
        catch (JsonException) { return new(InspectionStatus.InvalidJson); }
    }
}
