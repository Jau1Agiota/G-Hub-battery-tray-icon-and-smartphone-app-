using System.Text;
using Microsoft.Data.Sqlite;
using PeripheralBatteryCompanion.Core;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class ProviderTests
{
    private static BatteryReadResult Parse(string json) =>
        GHubSqliteBatteryProvider.Parse(Encoding.UTF8.GetBytes(json), DateTimeOffset.UtcNow);

    [Fact]
    public void PrimaryPercentageWinsOverWarning()
    {
        var result = Parse("""{"battery/g915/percentage":{"percentage":45,"time":"2026-10-26T13:41:42Z"},"battery/g915/warning":{"percentage":48}}""");
        Assert.Equal(45, result.Keyboard.Percentage);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 13, 41, 42, TimeSpan.Zero), result.Keyboard.LastReported);
        Assert.Equal("Unknown", result.Keyboard.Connectivity);
        Assert.NotNull(result.DatabaseAccessedAt);
    }

    [Theory]
    [InlineData(0)] [InlineData(20)] [InlineData(21)] [InlineData(50)] [InlineData(51)] [InlineData(100)]
    public void ValidPercentages(int value) => Assert.Equal(value,
        Parse($"{{\"battery/g915/percentage\":{{\"percentage\":{value}}}}}").Keyboard.Percentage);

    [Theory]
    [InlineData("")] [InlineData("{")] [InlineData("null")] [InlineData("[]")]
    [InlineData("{}")] [InlineData("{\"battery/g915/percentage\":null}")]
    [InlineData("{\"battery/g915/warning\":{\"percentage\":48}}")]
    [InlineData("{\"battery/g915/percentage\":{}}")]
    [InlineData("{\"battery/g915/percentage\":{\"percentage\":-1}}")]
    [InlineData("{\"battery/g915/percentage\":{\"percentage\":101}}")]
    [InlineData("{\"battery/g915/percentage\":{\"percentage\":45.5}}")]
    [InlineData("{\"battery/g915/percentage\":{\"percentage\":\"45\"}}")]
    [InlineData("{\"battery/g915/percentage\":{\"percentage\":null}}")]
    public void InvalidOrMissingDataIsUnavailable(string json)
    {
        var device = Parse(json).Keyboard;
        Assert.Null(device.Percentage);
        Assert.Equal(BatteryAvailability.Unavailable, device.Availability);
    }

    [Theory]
    [InlineData("2026-10-26T10:41:42-03:00", true)]
    [InlineData("2026-10-26T13:41:42Z", true)]
    [InlineData("2026-10-26T13:41:42", false)]
    [InlineData("invalid", false)]
    public void TimestampRequiresZoneAndNormalizesUtc(string time, bool valid)
    {
        var device = Parse($"{{\"battery/g915/percentage\":{{\"percentage\":45,\"time\":\"{time}\"}}}}").Keyboard;
        Assert.Equal(45, device.Percentage);
        if (valid)
        {
            Assert.Equal(TimeSpan.Zero, device.LastReported!.Value.Offset);
            Assert.Equal(13, device.LastReported.Value.Hour);
        }
        else Assert.Null(device.LastReported);
    }

    [Fact]
    public void UnsupportedMouseNeverInventsBatteryOrConnectivity()
    {
        Assert.Null(BatteryDevice.UnsupportedMouse.Percentage);
        Assert.Equal(BatteryAvailability.NotSupported, BatteryDevice.UnsupportedMouse.Availability);
        Assert.Equal("Unknown", BatteryDevice.UnsupportedMouse.Connectivity);
    }

    [Fact]
    public async Task MissingDatabaseIsNotCreated()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        var result = await new GHubSqliteBatteryProvider(path).ReadAsync(default);
        Assert.Equal(BatteryAvailability.Unavailable, result.Keyboard.Availability);
        Assert.Null(result.DatabaseAccessedAt);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ReadsCommittedWalWhileWriterRemainsOpenWithoutChangingData()
    {
        using var db = new TemporaryDatabase();
        using var writer = db.Open();
        Execute(writer, "PRAGMA journal_mode=WAL; CREATE TABLE data (_id INTEGER PRIMARY KEY, file BLOB);");
        using (var insert = writer.CreateCommand())
        {
            insert.CommandText = "INSERT INTO data VALUES (1, $file)";
            insert.Parameters.AddWithValue("$file", Encoding.UTF8.GetBytes("""{"battery/g915/percentage":{"percentage":45,"time":"2026-10-26T13:41:42Z"}}"""));
            insert.ExecuteNonQuery();
        }
        Assert.True(File.Exists(db.Path + "-wal"));
        // Only this fixture's writer exists, and it is idle during these snapshots.
        // File.ReadAllBytes uses FileShare.Read, incompatible with SQLite's writable
        // handle on Windows. Permit normal writer access; do not change SQLite locks.
        var before = ReadSharedSnapshot(db.Path + "-wal");
        var databaseBefore = ReadSharedSnapshot(db.Path);
        var provider = new GHubSqliteBatteryProvider(db.Path);
        var first = await provider.ReadAsync(default);
        var second = await provider.ReadAsync(default);
        Assert.Equal(45, first.Keyboard.Percentage);
        Assert.Equal(first.Keyboard, second.Keyboard);
        Assert.Equal(before, ReadSharedSnapshot(db.Path + "-wal"));
        Assert.Equal(databaseBefore, ReadSharedSnapshot(db.Path));
        using var count = writer.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM data";
        Assert.Equal(1L, count.ExecuteScalar());

        // A subsequent commit must still work through the original writer connection.
        Execute(writer, """UPDATE data SET file = CAST('{"battery/g915/percentage":{"percentage":62,"time":"2026-10-26T13:42:42Z"}}' AS BLOB) WHERE _id = 1""");
        Assert.Equal(62, (await provider.ReadAsync(default)).Keyboard.Percentage);
    }

    [Fact]
    public void SnapshotPermitsAnExistingWritableHandleAndReleasesItsOwnHandle()
    {
        using var db = new TemporaryDatabase();
        var path = db.Path + "-wal";
        var expected = Encoding.UTF8.GetBytes("synthetic sharing-mode fixture");
        using (var writer = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            writer.Write(expected);
            writer.Flush();
            // Reproduce the original helper's Windows sharing violation explicitly.
            if (OperatingSystem.IsWindows()) Assert.Throws<IOException>(() => File.ReadAllBytes(path));
            Assert.Equal(expected, ReadSharedSnapshot(path));
            Assert.Equal(expected.Length, writer.Position);
            writer.WriteByte(42); // Snapshot did not close/reposition/block the writer.
            writer.Flush();
            Assert.Equal(expected.Append((byte)42).ToArray(), ReadSharedSnapshot(path));
        }
        // Both snapshot handles have closed: exclusive reopen and normal cleanup work.
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(expected.Length + 1, exclusive.Length);
    }

    private static byte[] ReadSharedSnapshot(string path)
    {
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var snapshot = new MemoryStream();
        source.CopyTo(snapshot);
        return snapshot.ToArray();
    }

    [Theory]
    [InlineData("CREATE TABLE wrong (x TEXT)")]
    [InlineData("CREATE TABLE data (_id INTEGER, file BLOB)")]
    [InlineData("CREATE TABLE data (_id INTEGER, file BLOB); INSERT INTO data VALUES(1, X'')")]
    public async Task SchemaAndEmptyPayloadErrorsAreHandled(string sql)
    {
        using var db = new TemporaryDatabase();
        using (var connection = db.Open()) Execute(connection, sql);
        var result = await new GHubSqliteBatteryProvider(db.Path).ReadAsync(default);
        Assert.Equal(BatteryAvailability.Unavailable, result.Keyboard.Availability);
    }

    [Fact]
    public async Task LockedDatabaseIsHandled()
    {
        using var db = new TemporaryDatabase();
        using var writer = db.Open();
        Execute(writer, "CREATE TABLE data (_id INTEGER, file BLOB); BEGIN EXCLUSIVE;");
        var result = await new GHubSqliteBatteryProvider(db.Path).ReadAsync(default);
        Assert.Null(result.DatabaseAccessedAt);
        Assert.Equal(BatteryAvailability.Unavailable, result.Keyboard.Availability);
        Execute(writer, "ROLLBACK");
    }

    [Fact]
    public async Task CorruptDatabaseIsHandled()
    {
        using var db = new TemporaryDatabase();
        File.WriteAllText(db.Path, "not a database");
        var result = await new GHubSqliteBatteryProvider(db.Path).ReadAsync(default);
        Assert.Equal(BatteryAvailability.Unavailable, result.Keyboard.Availability);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pbc-" + Guid.NewGuid());
        public string Path => System.IO.Path.Combine(_directory, "settings.db");
        public TemporaryDatabase() => Directory.CreateDirectory(_directory);
        public SqliteConnection Open()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false }.ToString());
            connection.Open();
            return connection;
        }
        public void Dispose() => Directory.Delete(_directory, true);
    }
}
