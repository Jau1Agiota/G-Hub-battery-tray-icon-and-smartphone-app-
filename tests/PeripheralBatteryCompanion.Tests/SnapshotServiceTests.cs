using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PeripheralBatteryCompanion.Core;
using PeripheralBatteryCompanion.Core.Snapshots;
using PeripheralBatteryCompanion.Core.SnapshotService;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class SnapshotServiceTests
{
    private static readonly Guid Instance = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static readonly Guid Phone = Guid.Parse("00000000-0000-4000-8000-000000000002");
    // Deterministic SYNTHETIC test credentials only. Production issuance is not implemented.
    private static readonly byte[] Token = Enumerable.Repeat((byte)0x42, 32).ToArray();
    private static string Header(byte[]? token = null) => "Bearer " + Convert.ToBase64String(token ?? Token).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static SnapshotRequest Request(string? header = null, bool tls = true, string method = "GET", string? path = null, long body = 0) =>
        new(method, path ?? SnapshotRequestCore.SnapshotPath, header ?? Header(), tls, body);
    private static BatterySnapshotV1 Snapshot(int? value = 50) => BatterySnapshotProjection.Build(
        new(new(new("G915 Keyboard", value, null, value.HasValue ? BatteryAvailability.Available : BatteryAvailability.Unavailable,
            "synthetic fixture", Source: GHubWebSocketBatteryProvider.SourceName), null), null, null),
        null, new(Instance, 0, null));
    private static ApprovedPhoneRegistry Registry() { var phones = new ApprovedPhoneRegistry(); phones.ApproveVerifier(Phone, SHA256.HashData(Token)); return phones; }
    private static string Body(SnapshotResponse response) => Encoding.UTF8.GetString(response.Body.AsSpan());

    [Fact]
    public void DefaultOffAndTerminalStopCannotReadPublishedData()
    {
        var source = new CountingSource(); using var core = new SnapshotRequestCore(source, Registry());
        Assert.Equal(503, core.Handle(Request()).StatusCode); Assert.Equal(0, source.Reads);
        Assert.True(core.EnableByLocalOwner()); core.Dispose();
        Assert.False(core.EnableByLocalOwner()); Assert.Equal(503, core.Handle(Request()).StatusCode); Assert.Equal(0, source.Reads);
    }
    [Fact]
    public void ExplicitDisableDoesNotStopPublicationOrReactivateOnCacheUpdate()
    {
        using var store = new PublishedSnapshotStore(Instance); using var core = new SnapshotRequestCore(store, Registry());
        core.EnableByLocalOwner(); store.Publish(Snapshot()); Assert.Equal(200, core.Handle(Request()).StatusCode);
        core.DisableByLocalOwner(); store.Publish(Snapshot(0)); Assert.Equal(503, core.Handle(Request()).StatusCode);
        Assert.True(core.EnableByLocalOwner()); Assert.Equal(200, core.Handle(Request()).StatusCode);
    }
    [Theory]
    [InlineData("")] [InlineData("Bearer short")] [InlineData("Basic invalid")] [InlineData("Bearer !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public void MalformedCredentialsNeverReadCache(string header)
    {
        var source = new CountingSource(); using var core = new SnapshotRequestCore(source, Registry()); core.EnableByLocalOwner();
        Assert.Equal(401, core.Handle(Request(header)).StatusCode); Assert.Equal(0, source.Reads);
    }
    [Fact]
    public void MissingWrongRevokedAndRotatedCredentialsAreDenied()
    {
        using var store = new PublishedSnapshotStore(Instance); store.Publish(Snapshot()); var registry = Registry();
        using var core = new SnapshotRequestCore(store, registry); core.EnableByLocalOwner();
        Assert.Equal(401, core.Handle(new("GET", SnapshotRequestCore.SnapshotPath, null, true)).StatusCode);
        Assert.Equal(401, core.Handle(Request(Header(new byte[32]))).StatusCode);
        Assert.Equal(200, core.Handle(Request()).StatusCode);
        Assert.True(registry.Revoke(Phone)); Assert.Equal(401, core.Handle(Request()).StatusCode);
        var replacement = Enumerable.Repeat((byte)0x43, 32).ToArray(); registry.ApproveVerifier(Phone, SHA256.HashData(replacement));
        Assert.Equal(401, core.Handle(Request()).StatusCode); Assert.Equal(200, core.Handle(Request(Header(replacement))).StatusCode);
    }
    [Fact]
    public void PlaintextAdapterInputIsDeniedWithoutReadingData()
    {
        var source = new CountingSource(); using var core = new SnapshotRequestCore(source, Registry()); core.EnableByLocalOwner();
        Assert.Equal(403, core.Handle(Request(tls: false)).StatusCode); Assert.Equal(0, source.Reads);
    }
    [Theory]
    [InlineData("POST", "/v1/battery-snapshot", 0, 405)]
    [InlineData("GET", "/refresh", 0, 404)]
    [InlineData("GET", "/v1/battery-snapshot?force=true", 0, 404)]
    [InlineData("GET", "/v1/battery-snapshot", 1, 400)]
    [InlineData("GET", "/v1/battery-snapshot", -1, 400)]
    public void NoCommandsQueriesOrBodiesAreAccepted(string method, string path, long body, int status)
    {
        var source = new CountingSource(); using var core = new SnapshotRequestCore(source, Registry()); core.EnableByLocalOwner();
        Assert.Equal(status, core.Handle(Request(method: method, path: path, body: body)).StatusCode); Assert.Equal(0, source.Reads);
    }
    [Fact]
    public void OversizedPathHeaderAndMethodAreBoundedAndNeverEchoed()
    {
        var source = new CountingSource(); using var core = new SnapshotRequestCore(source, Registry()); core.EnableByLocalOwner();
        foreach (var request in new[] { Request(path: new string('x', 65)), Request(method: new string('x', 9)), Request(header: new string('x', 10000)) }) {
            var response = core.Handle(request); Assert.NotEqual(200, response.StatusCode); Assert.DoesNotContain("xxxxx", Body(response));
        }
        Assert.Equal(0, source.Reads); Assert.DoesNotContain(Header(), Request().ToString());
    }
    [Fact]
    public void NoncanonicalBase64AliasesAreRejected()
    {
        var zero = new byte[32]; var registry = new ApprovedPhoneRegistry(); registry.ApproveVerifier(Phone, SHA256.HashData(zero));
        using var store = new PublishedSnapshotStore(Instance); store.Publish(Snapshot());
        using var core = new SnapshotRequestCore(store, registry); core.EnableByLocalOwner();
        var valid = Header(zero); var alias = valid[..^1] + "B";
        Assert.Equal(200, core.Handle(Request(valid)).StatusCode); Assert.Equal(401, core.Handle(Request(alias)).StatusCode);
    }
    [Fact]
    public void VerifiersAreDefensivelyCopiedAndEnrollmentIsBounded()
    {
        var registry = new ApprovedPhoneRegistry(); var digest = SHA256.HashData(Token); registry.ApproveVerifier(Phone, digest); digest[0] ^= 0xff;
        using var store = new PublishedSnapshotStore(Instance); store.Publish(Snapshot()); using var core = new SnapshotRequestCore(store, registry); core.EnableByLocalOwner();
        Assert.Equal(200, core.Handle(Request()).StatusCode);
        Assert.Throws<ArgumentException>(() => registry.ApproveVerifier(Guid.Empty, new byte[32]));
        Assert.Throws<ArgumentException>(() => registry.ApproveVerifier(Guid.NewGuid(), new byte[31]));
        Assert.Throws<ArgumentException>(() => registry.ApproveVerifier(Guid.NewGuid(), SHA256.HashData(Token)));
        for (var i = 1; i < ApprovedPhoneRegistry.MaxPhones; i++) registry.ApproveVerifier(Guid.NewGuid(), SHA256.HashData(new[] { (byte)i }));
        Assert.Throws<InvalidOperationException>(() => registry.ApproveVerifier(Guid.NewGuid(), SHA256.HashData(new byte[] { 99 })));
    }
    [Fact]
    public void PhoneAndGlobalQuotasAreBoundedAndRecoverUsingMonotonicTime()
    {
        var clock = new Clock(); using var store = new PublishedSnapshotStore(Instance, clock); store.Publish(Snapshot());
        using var core = new SnapshotRequestCore(store, Registry(), clock); core.EnableByLocalOwner();
        for (var i = 0; i < SnapshotRequestCore.PhoneRequestsPerMinute; i++) Assert.Equal(200, core.Handle(Request()).StatusCode);
        Assert.Equal(429, core.Handle(Request()).StatusCode);
        for (var i = SnapshotRequestCore.PhoneRequestsPerMinute + 1; i < SnapshotRequestCore.GlobalRequestsPerMinute; i++) core.Handle(Request("invalid"));
        Assert.Equal(429, core.Handle(Request("invalid")).StatusCode);
        clock.Advance(60); Assert.Equal(200, core.Handle(Request()).StatusCode);
    }
    [Fact]
    public void SuccessfulReadsReuseExactPublishedBytesAndDoNotPollOrRevise()
    {
        var clock = new Clock(); using var store = new PublishedSnapshotStore(Instance, clock); var published = store.Publish(Snapshot());
        using var core = new SnapshotRequestCore(store, Registry(), clock); core.EnableByLocalOwner();
        var clockReads = clock.UtcReads;
        for (var i = 0; i < 100; i++) {
            if (i % 10 == 0) clock.Advance(60);
            var response = core.Handle(Request()); Assert.Equal(200, response.StatusCode);
            Assert.True(response.Body.Equals(published.Json)); Assert.Equal("no-store", response.CacheControl);
        }
        Assert.Same(published, store.ReadLatest()); Assert.Equal(1, published.Snapshot.Revision); Assert.Equal(clockReads, clock.UtcReads);
        Assert.InRange(published.Json.Length, 1, BatterySnapshotJson.MaxPayloadBytes);
    }
    [Fact]
    public void RevisionsAndCaptureTimeOnlyChangeOnPublicationNotReads()
    {
        var clock = new Clock(); using var store = new PublishedSnapshotStore(Instance, clock);
        var input = Snapshot(); var first = store.Publish(input); clock.Advance(10);
        Assert.Same(first, store.ReadLatest()); var second = store.Publish(input);
        Assert.Equal(1, first.Snapshot.Revision); Assert.Equal(2, second.Snapshot.Revision);
        Assert.True(second.Snapshot.CapturedAt > first.Snapshot.CapturedAt); Assert.Null(input.CapturedAt);
        Assert.Null(second.Snapshot.Devices[0].Current.ObservedAt); Assert.Null(second.Snapshot.Devices[0].Current.ReadAt);
    }
    [Fact]
    public void InvalidAndSyntheticPublicationNeverReplaceLastGoodSnapshot()
    {
        using var store = new PublishedSnapshotStore(Instance); var first = store.Publish(Snapshot());
        Assert.Throws<ArgumentException>(() => store.Publish(Snapshot() with { Synthetic = true }));
        Assert.Throws<ArgumentException>(() => store.Publish(Snapshot() with { SchemaVersion = 2 }));
        Assert.Same(first, store.ReadLatest()); Assert.Equal(2, store.Publish(Snapshot()).Snapshot.Revision);
    }
    [Fact]
    public void NoPublicationOrStoppedStoreNeverFabricatesLiveData()
    {
        using var store = new PublishedSnapshotStore(Instance); using var core = new SnapshotRequestCore(store, Registry()); core.EnableByLocalOwner();
        Assert.Equal(503, core.Handle(Request()).StatusCode); store.Publish(Snapshot()); store.Dispose();
        Assert.Null(store.ReadLatest()); Assert.Equal(503, core.Handle(Request()).StatusCode);
        Assert.Throws<ObjectDisposedException>(() => store.Publish(Snapshot()));
    }
    [Fact]
    public void CancelledRequestCannotReadCache()
    {
        var source = new CountingSource(); using var core = new SnapshotRequestCore(source, Registry()); core.EnableByLocalOwner();
        Assert.Equal(503, core.Handle(Request(), new CancellationToken(true)).StatusCode); Assert.Equal(0, source.Reads);
    }
    [Fact]
    public void StaleNullAndUncertainReadingsArePreservedInServedJson()
    {
        var baseSnapshot = Snapshot(); var old = baseSnapshot.Devices[0].Current;
        var stale = baseSnapshot with { Devices = baseSnapshot.Devices.SetItem(0, baseSnapshot.Devices[0] with {
            Current = old with { BatteryPercent = null }, Availability = SnapshotAvailability.STALE,
            Freshness = SnapshotFreshness.STALE, UnavailableReason = SnapshotUnavailableReason.STALE_CACHE, LastKnown = old }) };
        using var store = new PublishedSnapshotStore(Instance); store.Publish(stale);
        using var core = new SnapshotRequestCore(store, Registry()); core.EnableByLocalOwner();
        using var json = JsonDocument.Parse(core.Handle(Request()).Body.ToArray());
        var keyboard = json.RootElement.GetProperty("devices")[0];
        Assert.Equal(JsonValueKind.Null, keyboard.GetProperty("current").GetProperty("batteryPercent").ValueKind);
        Assert.Equal(50, keyboard.GetProperty("lastKnown").GetProperty("batteryPercent").GetInt32());
        Assert.Equal("STALE", keyboard.GetProperty("freshness").GetString());
    }
    [Fact]
    public async Task ConcurrentPublicationAndReadsNeverMixPayloadAndRevision()
    {
        using var store = new PublishedSnapshotStore(Instance);
        var writer = Task.Run(() => { for (var i = 0; i < 100; i++) store.Publish(Snapshot(i % 101)); });
        var readers = Enumerable.Range(0, 3).Select(_ => Task.Run(() => {
            for (var i = 0; i < 200; i++) {
                var published = store.ReadLatest(); if (published is null) continue;
                using var json = JsonDocument.Parse(published.Json.ToArray());
                Assert.Equal(published.Snapshot.Revision, json.RootElement.GetProperty("revision").GetInt64());
                Assert.Equal(published.Snapshot.Devices[0].Current.BatteryPercent, json.RootElement.GetProperty("devices")[0].GetProperty("current").GetProperty("batteryPercent").GetInt32());
            }
        })).ToArray();
        await Task.WhenAll(readers.Append(writer)); Assert.Equal(100, store.ReadLatest()!.Snapshot.Revision);
    }
    [Fact]
    public void CacheUpdateSeamPublishesWithoutHardwareAndRetainsOtherSource()
    {
        using var store = new PublishedSnapshotStore(Instance); using var publisher = new CachedSnapshotPublisher(store);
        var keyboard = new BatteryDevice("G915 Keyboard", 0, null, BatteryAvailability.Available,"synthetic",Source:GHubWebSocketBatteryProvider.SourceName);
        publisher.OnLogitechCacheChanged(new(new(keyboard,null),null,null));
        var hx = new BatteryDevice("HyperX",76,null,BatteryAvailability.Available,"synthetic",BatteryObservationState.CandidateAmbiguous,ReceiverPresence.Detected,"Supervised one-shot HID; response ownership ambiguous");
        publisher.OnHyperXCacheChanged(hx);
        var snapshot = store.ReadLatest()!.Snapshot;
        Assert.Equal(2, snapshot.Revision); Assert.Equal(0, snapshot.Devices[0].Current.BatteryPercent);
        Assert.Equal(SnapshotPrecision.ESTIMATE, snapshot.Devices[2].Current.Precision);
        Assert.Equal(SnapshotOwnership.AMBIGUOUS, snapshot.Devices[2].Current.Source.Ownership);
        publisher.Dispose(); Assert.Throws<ObjectDisposedException>(() => publisher.OnHyperXCacheChanged(hx));
    }
    [Fact]
    public async Task RevocationWaitsForAuthorizedConstructionThenDeniesFurtherReads()
    {
        using var store = new PublishedSnapshotStore(Instance); store.Publish(Snapshot());
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var blocking = new BlockingSource(store, entered, release); var registry = Registry();
        using var core = new SnapshotRequestCore(blocking, registry); core.EnableByLocalOwner();
        var read = Task.Run(() => core.Handle(Request()));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var revoke = Task.Run(() => registry.Revoke(Phone));
        try { Assert.False(revoke.IsCompleted); } finally { release.Set(); }
        Assert.Equal(200, (await read).StatusCode); Assert.True(await revoke);
        Assert.Equal(401, core.Handle(Request()).StatusCode);
    }
    [Fact]
    public void CacheFailureReturnsSafeErrorWithoutPrivateDetails()
    {
        using var core = new SnapshotRequestCore(new ThrowingSource(), Registry()); core.EnableByLocalOwner();
        var response = core.Handle(Request()); Assert.Equal(503, response.StatusCode);
        Assert.Equal("{\"error\":\"snapshot_unavailable\"}", Body(response));
        Assert.DoesNotContain("private", Body(response));
    }
    [Fact]
    public async Task ShutdownDrainsAuthorizedConstructionAndPreventsFurtherAccess()
    {
        using var store = new PublishedSnapshotStore(Instance); store.Publish(Snapshot());
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var core = new SnapshotRequestCore(new BlockingSource(store, entered, release), Registry()); core.EnableByLocalOwner();
        var read = Task.Run(() => core.Handle(Request())); Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var stop = Task.Run(core.Dispose);
        try { Assert.False(stop.IsCompleted); } finally { release.Set(); }
        Assert.Equal(200, (await read).StatusCode); await stop;
        Assert.False(core.EnableByLocalOwner()); Assert.Equal(503, core.Handle(Request()).StatusCode);
        Assert.NotNull(store.ReadLatest()); // Closing the request gate does not stop the independent publisher/monitor.
    }
    private sealed class ThrowingSource : IReadOnlyPublishedSnapshot
    {
        public PublishedSnapshot? ReadLatest() => throw new InvalidOperationException("private path or credential details");
    }
    private sealed class CountingSource : IReadOnlyPublishedSnapshot
    {
        public int Reads { get; private set; }
        public PublishedSnapshot? ReadLatest() { Reads++; return null; }
    }
    private sealed class BlockingSource(IReadOnlyPublishedSnapshot source, ManualResetEventSlim entered, ManualResetEventSlim release) : IReadOnlyPublishedSnapshot
    {
        public PublishedSnapshot? ReadLatest() { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); return source.ReadLatest(); }
    }
    private sealed class Clock : TimeProvider
    {
        private long _seconds;
        public int UtcReads { get; private set; }
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => _seconds;
        public override DateTimeOffset GetUtcNow() { UtcReads++; return new DateTimeOffset(2026,10,1,0,0,0,TimeSpan.Zero).AddSeconds(_seconds); }
        public void Advance(int seconds) => _seconds += seconds;
    }
}
