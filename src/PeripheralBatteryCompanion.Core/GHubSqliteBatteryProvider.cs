using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PeripheralBatteryCompanion.Core;

public sealed class GHubSqliteBatteryProvider : IBatteryProvider
{
    private readonly string _databasePath;
    public GHubSqliteBatteryProvider(string? databasePath = null) => _databasePath = databasePath ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LGHUB", "settings.db");

    // Microsoft.Data.Sqlite's async methods perform synchronous SQLite I/O. Explicitly
    // move the entire short-lived operation off the Windows message thread.
    public Task<BatteryReadResult> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Read(cancellationToken), cancellationToken);

    private BatteryReadResult Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_databasePath))
            return Unavailable("G HUB database missing or inaccessible");
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath, Mode = SqliteOpenMode.ReadOnly,
                Pooling = false, DefaultTimeout = 1
            }.ToString());
            connection.Open();
            cancellationToken.ThrowIfCancellationRequested();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT file FROM data ORDER BY _id DESC LIMIT 1";
            command.CommandTimeout = 1;
            var value = command.ExecuteScalar();
            cancellationToken.ThrowIfCancellationRequested();
            var accessed = DateTimeOffset.UtcNow;
            return value switch
            {
                byte[] bytes => Parse(bytes, accessed),
                string json => Parse(System.Text.Encoding.UTF8.GetBytes(json), accessed),
                _ => Unavailable("G HUB battery payload is empty", accessed)
            };
        }
        catch (SqliteException)
        {
            return Unavailable("G HUB database unavailable (busy, inaccessible, or incompatible schema)");
        }
        catch (IOException) { return Unavailable("G HUB database could not be read"); }
        catch (UnauthorizedAccessException) { return Unavailable("G HUB database access denied"); }
    }

    public static BatteryReadResult Parse(ReadOnlyMemory<byte> payload, DateTimeOffset accessedAt)
    {
        if (payload.IsEmpty) return Unavailable("G HUB battery payload is empty", accessedAt);
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("battery/g915/percentage", out var entry) ||
                entry.ValueKind != JsonValueKind.Object)
                return Unavailable("G915 battery record not found", accessedAt);
            if (!entry.TryGetProperty("percentage", out var percentage) ||
                percentage.ValueKind != JsonValueKind.Number || !percentage.TryGetInt32(out var number) ||
                number is < 0 or > 100)
                return Unavailable("G915 battery percentage is invalid", accessedAt);

            DateTimeOffset? timestamp = null;
            // Require an explicit timezone; do not silently interpret a zone-less value as local time.
            if (entry.TryGetProperty("time", out var time) && time.ValueKind == JsonValueKind.String)
            {
                var text = time.GetString()!;
                var separator = text.IndexOf('T');
                var hasZone = text.EndsWith('Z') || (separator >= 0 &&
                    (text.IndexOf('+', separator) >= 0 || text.IndexOf('-', separator) >= 0));
                if (hasZone && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed)) timestamp = parsed.ToUniversalTime();
            }
            return new(new("G915 Keyboard", number, timestamp, BatteryAvailability.Available,
                timestamp.HasValue ? "Last reported value; connectivity unknown" :
                "Last reported value; timestamp missing or invalid; connectivity unknown"), accessedAt);
        }
        catch (JsonException) { return Unavailable("G HUB battery JSON is malformed", accessedAt); }
    }

    private static BatteryReadResult Unavailable(string reason, DateTimeOffset? accessedAt = null) =>
        new(new("G915 Keyboard", null, null, BatteryAvailability.Unavailable, reason), accessedAt);
}
