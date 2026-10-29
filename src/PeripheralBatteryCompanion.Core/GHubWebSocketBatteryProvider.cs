using System.Net.WebSockets;
using System.Text.Json;

namespace PeripheralBatteryCompanion.Core;

// Undocumented local G HUB gateway, authorized GET endpoints only. No SQLite fallback or HID.
public sealed class GHubWebSocketBatteryProvider(IGHubGatewayConnectionFactory? factory = null,
    TimeProvider? clock = null) : IBatteryProvider
{
    public const string SourceName = "G HUB local gateway";
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan PollDeadline = TimeSpan.FromSeconds(12);
    public const int MaxMessageBytes = 256 * 1024;
    private readonly IGHubGatewayConnectionFactory _factory = factory ?? new GHubGatewayConnectionFactory();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _flight = new(1, 1);

    public async Task<BatteryReadResult> ReadAsync(CancellationToken token)
    {
        if (!await _flight.WaitAsync(0, token).ConfigureAwait(false)) return Failed("Gateway refresh already in progress");
        try
        {
            using var deadline = new CancellationTokenSource(PollDeadline, _clock);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
            var bounded = stop.Token;
            await using var connection = _factory.Create();
            await connection.ConnectAsync(bounded).ConfigureAwait(false);
            using var devices = await GetAsync(connection, "/devices/list", bounded).ConfigureAwait(false);
            var keyboard = Select(devices.RootElement, "g915");
            var mouse = Select(devices.RootElement, "g305");
            if (keyboard.Id is not null && keyboard.Id == mouse.Id) throw new InvalidDataException("Conflicting runtime identity");
            var k = await ReadDeviceAsync(connection, keyboard, "G915 Keyboard", bounded).ConfigureAwait(false);
            var m = await ReadDeviceAsync(connection, mouse, "G305 Mouse", bounded).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return new(k, null, m);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return Failed("G HUB gateway timed out"); }
        catch (Exception error) when (error is WebSocketException or IOException or InvalidDataException or JsonException or InvalidOperationException or ArgumentException)
        { return Failed("G HUB gateway unavailable or response rejected"); }
        finally { _flight.Release(); }
    }

    private sealed record Selection(string? Id, bool? RealSoc, DeviceConnection Connection, string Status);
    private static Selection Select(JsonElement root, string model)
    {
        var payload = Object(root, "payload");
        if (!payload.TryGetProperty("deviceInfos", out var infos) || infos.ValueKind != JsonValueKind.Array || infos.GetArrayLength() > 256)
            throw new InvalidDataException("Invalid device list");
        var active = new List<JsonElement>();
        var matchedAbsent = false;
        foreach (var info in infos.EnumerateArray())
        {
            if (info.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid device entry");
            var actual = Text(info, "deviceModel"); var baseModel = Text(info, "deviceBaseModel");
            if (!string.Equals(actual, model, StringComparison.OrdinalIgnoreCase) && !string.Equals(baseModel, model, StringComparison.OrdinalIgnoreCase)) continue;
            // A contradictory family identity must never bridge keyboard and mouse.
            if ((model == "g915" && string.Equals(baseModel, "g305", StringComparison.OrdinalIgnoreCase)) || (model == "g305" && string.Equals(baseModel, "g915", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Conflicting model identity");
            var state = Text(info, "state");
            if (state == "ABSENT") { matchedAbsent = true; continue; }
            if (state == "ACTIVE") active.Add(info);
        }
        if (active.Count != 1) return new(null, null,
            active.Count == 0 && matchedAbsent ? DeviceConnection.Absent : DeviceConnection.Unknown,
            active.Count > 1 ? "Multiple active matches; battery unknown" : matchedAbsent ? "G HUB reports device absent" : "No active supported device");
        var selected = active[0];
        var caps = Object(selected, "capabilities");
        if (!caps.TryGetProperty("hasBatteryStatus", out var has) || has.ValueKind != JsonValueKind.True)
            return new(null, null, DeviceConnection.Active, "G HUB reports no supported battery status");
        var id = Text(selected, "id");
        if (id is null || id.Length is < 1 or > 128 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            throw new InvalidDataException("Invalid runtime ID");
        bool? realSoc = null;
        if (caps.TryGetProperty("useRealSoc", out var soc) && soc.ValueKind != JsonValueKind.Null)
        {
            if (soc.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException("Invalid SOC capability");
            realSoc = soc.GetBoolean();
        }
        return new(id, realSoc, DeviceConnection.Active, "G HUB reports active");
    }

    private async Task<BatteryDevice> ReadDeviceAsync(IGHubGatewayConnection connection, Selection selection, string name, CancellationToken token)
    {
        if (selection.Id is null) return Unknown(name, selection.Status, selection.Connection);
        try
        {
            var path = $"/battery/{selection.Id}/state";
            using var reply = await GetAsync(connection, path, token).ConfigureAwait(false);
            var payload = Object(reply.RootElement, "payload");
            if (Text(payload, "deviceId") != selection.Id) throw new InvalidDataException("Wrong battery identity");
            if (!payload.TryGetProperty("percentage", out var percent) || percent.ValueKind != JsonValueKind.Number || !percent.TryGetInt32(out var value) || value is < 0 or > 100)
                throw new InvalidDataException("Invalid percentage");
            var support = Text(payload, "batteryStateOfChargeSupport");
            // Optional fields are validated, never used to fabricate endurance or finer granularity.
            ValidateBoolean(payload, "charging"); ValidateBoolean(payload, "criticalLevel");
            ValidateEstimate(payload, "mileage"); ValidateEstimate(payload, "maxLifeSpan");
            // Battery response is authoritative per device; discovery useRealSoc is only a hint.
            var precision = support switch
            {
                "STATE_OF_CHARGE_SUPPORTED" => BatteryPrecision.Percentage,
                "STATE_OF_CHARGE_NOT_SUPPORTED" => BatteryPrecision.Approximate,
                _ => BatteryPrecision.Unknown
            };
            return new(name, value, _clock.GetUtcNow(), BatteryAvailability.Available,
                precision switch
                {
                    BatteryPrecision.Percentage => "G HUB active · SOC-supported response",
                    BatteryPrecision.Approximate => "G HUB active · approximate battery status; precise SOC unsupported",
                    _ => "G HUB active · valid battery value; SOC precision unknown"
                },
                Source: SourceName, Precision: precision,
                Connection: selection.Connection);
        }
        catch (GatewayResultException) { return Unknown(name, "Battery request rejected by G HUB", selection.Connection); }
    }

    private static async Task<JsonDocument> GetAsync(IGHubGatewayConnection connection, string path, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString("N");
        await connection.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { msgId = id, verb = "GET", path }), token).ConfigureAwait(false);
        for (var ignored = 0; ignored < 32; ignored++)
        {
            using var message = await ReceiveAsync(connection, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            UniqueMembers(message.RootElement);
            var root = message.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid envelope");
            if (Text(root, "msgId") != id) continue; // Greeting, unsolicited or out-of-order messages are never results.
            if (Text(root, "path") != path) throw new InvalidDataException("Wrong result path");
            if (Text(Object(root, "result"), "code") != "SUCCESS") throw new GatewayResultException();
            return JsonDocument.Parse(root.GetRawText(), new JsonDocumentOptions { MaxDepth = 32 });
        }
        throw new InvalidDataException("Too many unrelated gateway messages");
    }
    private static async Task<JsonDocument> ReceiveAsync(IGHubGatewayConnection connection, CancellationToken token)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        for (var chunks = 0; chunks < 1024; chunks++)
        {
            var part = await connection.ReceiveAsync(buffer, token).ConfigureAwait(false);
            if (part.Type != WebSocketMessageType.Text || part.Count < 0 || part.Count > buffer.Length || bytes.Length + part.Count > MaxMessageBytes)
                throw new InvalidDataException("Gateway closed or invalid/oversized response");
            bytes.Write(buffer, 0, part.Count);
            if (part.EndOfMessage) return JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        }
        throw new InvalidDataException("Too many gateway fragments");
    }
    private static void UniqueMembers(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate gateway member");
                UniqueMembers(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) UniqueMembers(item);
    }
    private static JsonElement Object(JsonElement value, string name) => value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Object
        ? field : throw new InvalidDataException("Missing gateway object");
    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
    private static void ValidateBoolean(JsonElement value, string name)
    {
        if (value.TryGetProperty(name, out var field) && field.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException("Invalid boolean");
    }
    private static void ValidateEstimate(JsonElement value, string name)
    {
        if (value.TryGetProperty(name, out var field) && (field.ValueKind != JsonValueKind.Number || !field.TryGetDouble(out var number) || !double.IsFinite(number) || number < 0))
            throw new InvalidDataException("Invalid runtime estimate");
    }
    private static BatteryDevice Unknown(string name, string status, DeviceConnection connection = DeviceConnection.Unknown) =>
        new(name, null, null, BatteryAvailability.Unavailable, status, Source: SourceName, Precision: BatteryPrecision.Unknown, Connection: connection);
    private static BatteryReadResult Failed(string reason) => new(Unknown("G915 Keyboard", reason), null, Unknown("G305 Mouse", reason));
    private sealed class GatewayResultException : IOException;
}
