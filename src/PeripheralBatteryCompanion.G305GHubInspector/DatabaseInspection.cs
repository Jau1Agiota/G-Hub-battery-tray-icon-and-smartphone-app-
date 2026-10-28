using System.Text;
using Microsoft.Data.Sqlite;

namespace PeripheralBatteryCompanion.G305GHubInspector;

public static class DatabaseInspection
{
    public static InspectionReport Read(string databasePath)
    {
        if (!File.Exists(databasePath)) return new(InspectionStatus.MissingOrInaccessible);
        var opened = false; var schema = false;
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 1 }.ToString());
            connection.Open(); opened = true;
            // Connection-local SQLite allocation bound, including TEXT casts/row materialization; no database setting is changed.
            SQLitePCL.raw.sqlite3_limit(connection.Handle, SQLitePCL.raw.SQLITE_LIMIT_LENGTH, JsonInspection.MaxBytes + 1024);
            // One live SQLite snapshot, not a raw DB/WAL copy. Deferred BEGIN performs no write operation.
            using var snapshot = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand(); command.Transaction = snapshot; command.CommandTimeout = 1;
            command.CommandText = "SELECT 1 FROM sqlite_schema WHERE type='table' AND name='data' LIMIT 1";
            if (command.ExecuteScalar() is null) return new(InspectionStatus.UnsupportedSchema, Opened: true);
            command.CommandText = "PRAGMA table_info(data)";
            var file = false; var id = false; var columns = 0;
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (++columns > 64) return new(InspectionStatus.LimitExceeded, Opened: true, Limited: true);
                    var name = reader.GetString(1);
                    file |= name.Equals("file", StringComparison.OrdinalIgnoreCase);
                    id |= name.Equals("_id", StringComparison.OrdinalIgnoreCase);
                }
            }
            if (!file || !id) return new(InspectionStatus.UnsupportedSchema, Opened: true);
            schema = true;
            // CAST length measures UTF-8 bytes for TEXT before GetValue can materialize an oversized payload.
            command.CommandText = "SELECT typeof(file), length(CAST(file AS BLOB)) FROM data ORDER BY _id DESC LIMIT 1";
            string type; long length;
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read()) return new(InspectionStatus.Empty, Opened: true, SchemaSupported: true);
                type = reader.GetString(0); length = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
            }
            if (length > JsonInspection.MaxBytes) return new(InspectionStatus.LimitExceeded, true, true, Limited: true, PayloadType: type);
            if (length == 0) return new(InspectionStatus.Empty, true, true, PayloadType: type);
            if (type is not "blob" and not "text") return new(InspectionStatus.InvalidPayload, true, true, PayloadType: type);
            command.CommandText = "SELECT file FROM data ORDER BY _id DESC LIMIT 1";
            var payload = command.ExecuteScalar() switch { byte[] bytes => bytes, string json => Encoding.UTF8.GetBytes(json), _ => [] };
            var result = JsonInspection.Inspect(payload);
            command.CommandText = "SELECT EXISTS(SELECT 1 FROM data LIMIT 1 OFFSET 1)";
            var additional = Convert.ToInt64(command.ExecuteScalar()) != 0;
            return result with { Opened = true, SchemaSupported = true, PayloadType = type, RowsInspected = 1, AdditionalRows = additional };
        }
        catch (SqliteException error)
        {
            return new(error.SqliteErrorCode == 18 ? InspectionStatus.LimitExceeded : InspectionStatus.SqliteFailure,
                opened, schema, Limited: error.SqliteErrorCode == 18, SqliteCode: error.SqliteErrorCode);
        }
        catch (IOException) { return new(InspectionStatus.MissingOrInaccessible, opened, schema); }
        catch (UnauthorizedAccessException) { return new(InspectionStatus.MissingOrInaccessible, opened, schema); }
    }
}
