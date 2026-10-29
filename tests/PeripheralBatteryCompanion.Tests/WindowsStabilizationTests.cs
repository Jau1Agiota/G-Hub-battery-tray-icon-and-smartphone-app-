using System.Text;
using Microsoft.Data.Sqlite;
using PeripheralBatteryCompanion.Core;
using PeripheralBatteryCompanion.Core.Presentation;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class WindowsStabilizationTests
{
    private static readonly DateTimeOffset Access = DateTimeOffset.Parse("2026-10-28T12:00:00Z");

    [Theory]
    [InlineData("2026-10-28T12:00:01Z", false)]
    [InlineData("2026-10-28T12:00:00Z", true)]
    [InlineData("2020-01-01T00:00:00Z", true)]
    [InlineData("2026-10-28T09:00:00-03:00", true)]
    public void SourceClockCannotBecomeFreshnessProof(string time, bool timestampUsable)
    {
        var result = GHubSqliteBatteryProvider.Parse(Encoding.UTF8.GetBytes(
            """{"battery/g915/percentage":{"percentage":45,"time":"TIME"}}""".Replace("TIME", time)), Access);
        Assert.Equal(45, result.Keyboard.Percentage);
        Assert.Equal(timestampUsable, result.Keyboard.LastReported.HasValue);
        var view = BatteryPanelView.From(new(result, null, Access));
        Assert.Contains("freshness and connectivity unknown", view.StatusText);
        Assert.Equal("Battery: Unknown", view.MouseBatteryText);
        Assert.Equal("Unknown", result.Keyboard.Connectivity);
    }

    [Fact]
    public void OversizedJsonIsUnavailableWithoutInventedPercentage()
    {
        var payload = new byte[GHubSqliteBatteryProvider.MaxPayloadBytes + 1];
        var result = GHubSqliteBatteryProvider.Parse(payload, Access);
        Assert.Null(result.Keyboard.Percentage);
        Assert.Contains("limit", result.Keyboard.Status);
    }

    [Fact]
    public async Task OversizedSqliteRowIsBoundedAndWriterStillWorks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pbc-stage4-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "synthetic.db");
        try
        {
            using var writer = new SqliteConnection($"Data Source={path};Pooling=False");
            writer.Open();
            using var command = writer.CreateCommand();
            command.CommandText = "CREATE TABLE data (_id INTEGER PRIMARY KEY, file BLOB); INSERT INTO data VALUES(1, zeroblob(5000000));";
            command.ExecuteNonQuery();
            var provider = new GHubSqliteBatteryProvider(path);
            Assert.Null((await provider.ReadAsync(default)).Keyboard.Percentage);
            command.CommandText = """UPDATE data SET file=CAST('{"battery/g915/percentage":{"percentage":62}}' AS BLOB)""";
            command.ExecuteNonQuery();
            Assert.Equal(62, (await provider.ReadAsync(default)).Keyboard.Percentage);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task KnownFailureRecoveryRetainsCacheSeparatelyAndG305AlwaysUnknown()
    {
        var good = new BatteryReadResult(new("G915 Keyboard", 45, Access.AddYears(-1), BatteryAvailability.Available, "Stored report"), Access);
        var failed = new BatteryReadResult(good.Keyboard with { Percentage = null, Availability = BatteryAvailability.Unavailable, Status = "Synthetic lock failure" }, null);
        var source = new QueueProvider([good, failed, good with { Keyboard = good.Keyboard with { Percentage = 62 } }]);
        await using var monitor = new BatteryMonitoringService(source);
        var states = new List<BatteryMonitoringState>();
        monitor.Updated += states.Add;
        for (var i = 0; i < 3; i++) await monitor.RefreshAsync();
        Assert.Equal(45, BatteryPanelView.From(states[0]).Percentage);
        var missing = BatteryPanelView.From(states[1]);
        Assert.Null(missing.Percentage);
        Assert.Contains("45% (cached)", missing.StatusText);
        Assert.Equal(BatteryLevelColor.Gray, missing.Color);
        Assert.Equal(62, BatteryPanelView.From(states[2]).Percentage);
        Assert.All(states, s => Assert.Equal("Battery: Unknown", BatteryPanelView.From(s).MouseBatteryText));
    }

    [Theory]
    [InlineData(96)] [InlineData(120)] [InlineData(144)]
    public void ThreeCardsAndFooterFitAtRequestedDpiAndNegativeScreenEdges(int dpi)
    {
        var size = DashboardLayout.NominalSize(dpi);
        var work = new System.Drawing.Rectangle(-1920, -1080, 1920, 1000);
        var fitted = PopupPlacement.FitSize(size, work.Size);
        foreach (var anchor in new[] { work.Location, new System.Drawing.Point(work.Right - 1, work.Bottom - 1) })
        {
            var location = PopupPlacement.NearTray(new(-1920, -1080, 1920, 1080), work, fitted, anchor, 8);
            Assert.True(work.Contains(new System.Drawing.Rectangle(location, fitted)));
        }
        Assert.Equal(DashboardLayout.Scale(520, dpi), fitted.Width);
        Assert.True(DashboardLayout.Footer(dpi, size.Width).Bottom <= size.Height);
    }

    private sealed class QueueProvider(IEnumerable<BatteryReadResult> results) : IBatteryProvider
    {
        private readonly Queue<BatteryReadResult> _results = new(results);
        public Task<BatteryReadResult> ReadAsync(CancellationToken token) => Task.FromResult(_results.Dequeue());
    }
}
