using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PeripheralBatteryCompanion.Core;
using PeripheralBatteryCompanion.Core.Presentation;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class GHubGatewayTests
{
    private static object Device(string id, string model, string? baseModel = null, string state = "ACTIVE", bool battery = true, bool? realSoc = null) =>
        new { id, deviceModel = model, deviceBaseModel = baseModel, state, capabilities = new { hasBatteryStatus = battery, useRealSoc = realSoc } };
    private static object[] Devices => [Device("keyboard-A", "g915"), Device("old-mouse", "g305", state: "ABSENT"), Device("mouse-A", "g305_ext2", "g305", realSoc: false)];
    private static byte[] Frame(string id, string path, object payload, string code = "SUCCESS") => JsonSerializer.SerializeToUtf8Bytes(new { msgId = id, path, result = new { code }, payload });
    private static object Battery(string id, string support = "STATE_OF_CHARGE_SUPPORTED", int percentage = 99) => new
    {
        deviceId = id, percentage, batteryStateOfChargeSupport = support,
        charging = false, criticalLevel = false, mileage = 0.0, maxLifeSpan = 137.064682,
        batteryMileageSupport = "MILEAGE_NOT_SUPPORTED"
    };
    private static Socket Good(object[]? devices = null) => new((id, path) =>
        path == "/devices/list" ? Frame(id, path, new { deviceInfos = devices ?? Devices })
        : Frame(id, path, path.Contains("mouse") ? Battery(path.Split('/')[2], "STATE_OF_CHARGE_NOT_SUPPORTED", 90) : Battery(path.Split('/')[2])));

    [Fact]
    public async Task ActiveDiscoveryUsesDynamicIdsAndOnlyKnownGetsWithFragmentationAndGreeting()
    {
        var socket = Good(); socket.FragmentSize = 3;
        socket.Prefix.Add(Encoding.UTF8.GetBytes("""{"msgId":"","verb":"OPTIONS","path":"/","origin":"backend"}"""));
        socket.Prefix.Add(Frame("someone-else", "/battery/unrelated/state", Battery("unrelated")));
        var result = await new GHubWebSocketBatteryProvider(new Factory(socket)).ReadAsync(default);
        Assert.Equal(99, result.Keyboard.Percentage);
        Assert.Equal(BatteryPrecision.Percentage, result.Keyboard.Precision);
        Assert.Equal(90, result.Mouse!.Percentage);
        Assert.Equal(BatteryPrecision.Approximate, result.Mouse.Precision);
        Assert.Null(result.DatabaseAccessedAt);
        Assert.Equal(new[] { "/devices/list", "/battery/keyboard-A/state", "/battery/mouse-A/state" }, socket.Paths);
        Assert.Equal(3, socket.MsgIds.Distinct().Count());
        Assert.All(socket.Verbs, verb => Assert.Equal("GET", verb));
        Assert.True(socket.Disposed);
    }

    [Theory]
    [InlineData("ABSENT", true)] [InlineData("ACTIVE", false)] [InlineData("UNKNOWN", true)]
    public async Task IneligibleMouseNeverRequestsBattery(string state, bool battery)
    {
        var socket = Good([Device("mouse-B", "g305_ext2", "g305", state, battery)]);
        var result = await new GHubWebSocketBatteryProvider(new Factory(socket)).ReadAsync(default);
        Assert.Null(result.Mouse!.Percentage);
        Assert.Equal(new[] { "/devices/list" }, socket.Paths);
        Assert.Equal(state == "ABSENT" ? DeviceConnection.Absent : state == "ACTIVE" ? DeviceConnection.Active : DeviceConnection.Unknown, result.Mouse.Connection);
    }

    [Fact]
    public async Task DuplicateActiveMatchesAreAmbiguousAndNeverPickedFirst()
    {
        var socket = Good([Device("mouse-1", "g305"), Device("mouse-2", "g305_ext2", "g305")]);
        var result = await new GHubWebSocketBatteryProvider(new Factory(socket)).ReadAsync(default);
        Assert.Contains("Multiple", result.Mouse!.Status);
        Assert.Single(socket.Paths);
    }

    [Fact]
    public async Task ReconnectRediscoversChangedIdsAndDoesNotReadSqliteFallback()
    {
        var first = Good(); var offline = Good(); offline.FailConnect = true;
        var changed = Good([Device("keyboard-new", "g915"), Device("mouse-new", "g305_ext2", "g305", realSoc: false)]);
        var provider = new GHubWebSocketBatteryProvider(new Factory(first, offline, changed));
        Assert.Equal(99, (await provider.ReadAsync(default)).Keyboard.Percentage);
        var unavailable = await provider.ReadAsync(default);
        Assert.Null(unavailable.Keyboard.Percentage); Assert.Null(unavailable.Mouse!.Percentage);
        var next = await provider.ReadAsync(default);
        Assert.Equal(90, next.Mouse!.Percentage);
        Assert.Contains("/battery/mouse-new/state", changed.Paths);
        Assert.DoesNotContain("/battery/mouse-A/state", changed.Paths);
        Assert.All(new[] { first, offline, changed }, c => Assert.True(c.Disposed));
    }

    [Theory]
    [InlineData("wrong-path")] [InlineData("wrong-device")] [InlineData("malformed")]
    [InlineData("too-large")] [InlineData("binary")] [InlineData("close")]
    [InlineData("string-percent")] [InlineData("range")] [InlineData("fraction")]
    [InlineData("bad-charging")] [InlineData("bad-mileage")]
    [InlineData("bad-list")] [InlineData("unsafe-id")] [InlineData("bad-real-soc")] [InlineData("id-collision")] [InlineData("duplicate-id")]
    public async Task InvalidRepliesNeverBecomeMeasurements(string defect)
    {
        var socket = Good(); var original = socket.Respond;
        socket.Respond = (id, path) =>
        {
            if (defect == "malformed") return Encoding.UTF8.GetBytes("{");
            if (defect == "duplicate-id") return Encoding.UTF8.GetBytes("{\"msgId\":\"foreign\"," + Encoding.UTF8.GetString(original(id, path))[1..]);
            if (defect == "too-large") return new byte[GHubWebSocketBatteryProvider.MaxMessageBytes + 1];
            var json = JsonNode.Parse(original(id, path))!;
            if (path == "/devices/list")
            {
                if (defect == "wrong-path") json["path"] = "/devices/private";
                if (defect == "bad-list") json["payload"]!["deviceInfos"] = "not-an-array";
                if (defect == "unsafe-id") json["payload"]!["deviceInfos"]![0]!["id"] = "../private/other";
                if (defect == "bad-real-soc") json["payload"]!["deviceInfos"]![0]!["capabilities"]!["useRealSoc"] = "false";
                if (defect == "id-collision") json["payload"]!["deviceInfos"]![2]!["id"] = "keyboard-A";
            }
            else
            {
                var payload = json["payload"]!;
                switch (defect)
                {
                    case "wrong-device": payload["deviceId"] = "another-device"; break;
                    case "string-percent": payload["percentage"] = "99"; break;
                    case "range": payload["percentage"] = 101; break;
                    case "fraction": payload["percentage"] = 99.5; break;
                    case "bad-charging": payload["charging"] = "false"; break;
                    case "bad-mileage": payload["mileage"] = -1; break;
                }
            }
            return Encoding.UTF8.GetBytes(json.ToJsonString());
        };
        if (defect == "binary") socket.MessageType = WebSocketMessageType.Binary;
        if (defect == "close") socket.MessageType = WebSocketMessageType.Close;
        var result = await new GHubWebSocketBatteryProvider(new Factory(socket)).ReadAsync(default);
        Assert.Null(result.Keyboard.Percentage); Assert.Null(result.Mouse!.Percentage);
        Assert.DoesNotContain("private", result.Keyboard.Status);
        Assert.True(socket.Disposed);
    }

    [Fact]
    public async Task RejectedBatteryIsNotFreshAndDoesNotPreventOtherDeviceRead()
    {
        var socket = Good(); var original = socket.Respond;
        socket.Respond = (id, path) => path.Contains("keyboard") ? Frame(id, path, new { }, "ERROR") : original(id, path);
        var result = await new GHubWebSocketBatteryProvider(new Factory(socket)).ReadAsync(default);
        Assert.Null(result.Keyboard.Percentage); Assert.Equal(90, result.Mouse!.Percentage);
    }

    [Theory]
    [InlineData("STATE_OF_CHARGE_NOT_SUPPORTED", true, BatteryPrecision.Approximate)]
    [InlineData("STATE_OF_CHARGE_SUPPORTED", false, BatteryPrecision.Percentage)]
    [InlineData("STATE_OF_CHARGE_SUPPORTED", true, BatteryPrecision.Percentage)]
    [InlineData("UNRECOGNIZED", true, BatteryPrecision.Unknown)]
    public async Task SocAndDeviceCapabilityGovernPrecision(string support, bool realSoc, BatteryPrecision expected)
    {
        var socket = Good([Device("mouse-X", "g305_ext2", "g305", realSoc: realSoc)]);
        socket.Respond = (id, path) => path == "/devices/list" ? Frame(id, path, new { deviceInfos = new[] { Device("mouse-X", "g305_ext2", "g305", realSoc: realSoc) } }) : Frame(id, path, Battery("mouse-X", support, 90));
        var result = await new GHubWebSocketBatteryProvider(new Factory(socket)).ReadAsync(default);
        Assert.Equal(expected, result.Mouse!.Precision);
        Assert.Equal(90, result.Mouse.Percentage);
        if (expected == BatteryPrecision.Unknown) Assert.Contains("precision unknown", result.Mouse.Status);
        var view = BatteryPanelView.From(new(result, null, null));
        if (expected == BatteryPrecision.Approximate)
        {
            Assert.Contains("~90%", view.MouseBatteryText); Assert.Contains("approx.", view.MouseBatteryText);
            Assert.Equal(90, view.MousePercentage); Assert.DoesNotContain("Good", view.MouseStatusText);
        }
    }

    [Fact]
    public async Task ContinuousUnrelatedMessagesAreBoundedWithoutRetryOrUnobservedReader()
    {
        var socket = new Socket((_, path) => Frame("foreign", path, new { })); socket.Repeat = true;
        var result = await new GHubWebSocketBatteryProvider(new Factory(socket)).ReadAsync(default);
        Assert.Null(result.Keyboard.Percentage); Assert.Equal(32, socket.CompletedMessages); Assert.Single(socket.Paths); Assert.True(socket.Disposed);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancellationOrDeadlineDisposesConnectionWithoutRetry(bool timeout)
    {
        var clock = new ManualClock();
        var socket = Good(); socket.Hold = true;
        var factory = new Factory(socket);
        var provider = new GHubWebSocketBatteryProvider(factory, clock);
        using var cancel = new CancellationTokenSource();
        var read = provider.ReadAsync(cancel.Token);
        await socket.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var overlap = await provider.ReadAsync(default); Assert.Null(overlap.Keyboard.Percentage); Assert.Equal(1, factory.Creates);
        if (timeout)
        {
            clock.Advance(12);
            var result = await read.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Contains("timed out", result.Keyboard.Status);
        }
        else { cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read); }
        Assert.True(socket.Disposed); Assert.Equal(1, factory.Creates);
    }

    [Fact]
    public async Task PollsImmediatelyThen60SecondsAndManualRefreshDoesNotChangeCadence()
    {
        var clock = new ManualClock(); var provider = new CountingProvider();
        await using var service = new BatteryMonitoringService(provider, GHubWebSocketBatteryProvider.PollInterval, clock);
        service.Start();
        await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, provider.Reads);
        clock.Advance(59); Assert.Equal(1, provider.Reads);
        await service.RefreshAsync(); Assert.Equal(2, provider.Reads);
        clock.Advance(1); await provider.Third.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(3, provider.Reads);
    }

    [Fact]
    public async Task FailureRetainsBothCachesWithApproximateStaleMouseAndTooltipBudget()
    {
        var factory = new Factory(Good(), new Socket((_, _) => throw new WebSocketException()));
        await using var monitor = new BatteryMonitoringService(new GHubWebSocketBatteryProvider(factory));
        BatteryMonitoringState? state = null; monitor.Updated += s => state = s;
        await monitor.RefreshAsync();
        var good = BatteryPanelView.From(state!); Assert.Equal("Battery: 99%", good.BatteryText); Assert.True(good.IsLiveGateway);
        Assert.DoesNotContain("Stored", good.StatusText);
        await monitor.RefreshAsync();
        var stale = BatteryPanelView.From(state!);
        Assert.Null(stale.Percentage); Assert.Null(stale.MousePercentage);
        Assert.Contains("~90% (stale)", stale.MouseStatusText);
        var text = TrayTooltipView.Build(state, new("HyperX", 100, null, BatteryAvailability.Unavailable, "", BatteryObservationState.Stale));
        Assert.Contains("G305: ~90% stale", text); Assert.Contains("G915: 99% stale", text); Assert.Contains("HyperX: ~100% stale", text);
        Assert.True(text.Length <= TrayTooltipView.MaximumLength);
    }

    [Theory]
    [InlineData("connect")] [InlineData("send")] [InlineData("receive")]
    public async Task DisposalDuringAnyStageWaitsForCancellationAndPreventsFurtherReads(string stage)
    {
        var socket = Good(); socket.HoldStage = stage;
        var factory = new Factory(socket);
        await using var monitor = new BatteryMonitoringService(new GHubWebSocketBatteryProvider(factory));
        var updates = 0; monitor.Updated += _ => updates++;
        var read = monitor.RefreshAsync();
        await socket.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await monitor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.True(socket.Disposed); Assert.Equal(0, updates);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => monitor.RefreshAsync());
        Assert.Equal(1, factory.Creates);
    }

    [Fact]
    public async Task MouseAbsentThenActiveNeverReusesOldIdOrStaleNumberAsCurrent()
    {
        var a = Good(); var absent = Good([Device("keyboard-A", "g915"), Device("mouse-A", "g305_ext2", "g305", "ABSENT")]);
        var returned = Good([Device("mouse-returned", "g305_ext2", "g305", realSoc: false)]);
        await using var monitor = new BatteryMonitoringService(new GHubWebSocketBatteryProvider(new Factory(a, absent, returned)));
        BatteryMonitoringState? state = null; monitor.Updated += s => state = s;
        await monitor.RefreshAsync(); await monitor.RefreshAsync();
        Assert.Equal(DeviceConnection.Absent, state!.Current.Mouse!.Connection);
        Assert.Equal("Battery: Unknown", BatteryPanelView.From(state).MouseBatteryText);
        Assert.Contains("stale", BatteryPanelView.From(state).MouseStatusText);
        Assert.DoesNotContain("/battery/mouse-A/state", absent.Paths);
        await monitor.RefreshAsync();
        Assert.Contains("/battery/mouse-returned/state", returned.Paths);
        Assert.DoesNotContain("stale", BatteryPanelView.From(state!).MouseStatusText);
    }

    [Fact]
    public async Task WorstCaseThreeDeviceTooltipPreservesApproximateAndStaleMarkers()
    {
        var result = await new GHubWebSocketBatteryProvider(new Factory(Good())).ReadAsync(default);
        var previous = result.Mouse! with { Percentage = 100 };
        var state = new BatteryMonitoringState(result with { Keyboard = result.Keyboard with { Percentage = null, Availability = BatteryAvailability.Unavailable }, Mouse = previous with { Percentage = null, Availability = BatteryAvailability.Unavailable } },
            result.Keyboard with { Percentage = 100 }, null, previous);
        var text = TrayTooltipView.Build(state, new("HyperX", 100, null, BatteryAvailability.Unavailable, "", BatteryObservationState.Stale));
        Assert.Contains("G915: 100% stale", text); Assert.Contains("G305: ~100% stale", text); Assert.Contains("HyperX: ~100% stale", text);
        Assert.InRange(text.Length, 1, 63);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PerDeviceResponseOverridesHintsRegardlessOfDiscoveryOrder(bool reverse)
    {
        object[] devices = [Device("keyboard-precise", "g915", realSoc: false), Device("mouse-approx", "g305_ext2", "g305", realSoc: true)];
        var socket = Good(reverse ? devices.Reverse().ToArray() : devices);
        var result = await new GHubWebSocketBatteryProvider(new Factory(socket)).ReadAsync(default);
        var view = BatteryPanelView.From(new(result, null, null));
        Assert.Equal(BatteryPrecision.Percentage, result.Keyboard.Precision);
        Assert.Equal("Battery: 99%", view.BatteryText); Assert.Equal(99, view.Percentage);
        Assert.Equal("G HUB · Precise reading", view.KeyboardStatusText);
        Assert.Equal("Battery: ~90% (approx.)", view.MouseBatteryText); Assert.Equal(90, view.MousePercentage);
        Assert.Equal("G HUB · Approximate reading", view.MouseCompactStatus);
    }

    [Fact]
    public async Task MissingSocRetainsAvailableValueWithoutClaimingPrecision()
    {
        var socket = Good(); var original = socket.Respond;
        socket.Respond = (id, path) =>
        {
            var json = JsonNode.Parse(original(id, path))!;
            if (path != "/devices/list") json["payload"]!.AsObject().Remove("batteryStateOfChargeSupport");
            return Encoding.UTF8.GetBytes(json.ToJsonString());
        };
        var result = await new GHubWebSocketBatteryProvider(new Factory(socket)).ReadAsync(default);
        Assert.Equal(BatteryAvailability.Available, result.Keyboard.Availability);
        Assert.Equal(BatteryPrecision.Unknown, result.Keyboard.Precision);
        var view = BatteryPanelView.From(new(result, null, null));
        Assert.Equal(99, view.Percentage); Assert.Contains("~99%", view.BatteryText);
        Assert.Equal("G HUB · Precision unknown", view.KeyboardStatusText);
        Assert.Equal("G HUB · Precision unknown", view.MouseCompactStatus);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task StartupAttemptPrecedesTimerEvenWhenSlowOrInitiallyUnavailable(bool firstFailure)
    {
        var clock = new ManualClock(); var provider = new StartupProvider();
        await using var monitor = new BatteryMonitoringService(provider, GHubWebSocketBatteryProvider.PollInterval, clock);
        var states = new List<BatteryMonitoringState>();
        var third = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Updated += state => { states.Add(state); if (states.Count == 3) third.TrySetResult(); };
        monitor.Start(); // Must return without waiting on the deliberately blocked first attempt.
        await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, provider.Reads); Assert.False(clock.TimerCreated.Task.IsCompleted);
        clock.Advance(60); // No poll timer exists until the initial attempt finishes.
        await monitor.RefreshAsync(); Assert.Equal(1, provider.Reads);
        provider.First.SetResult(new(new("G915 Keyboard", firstFailure ? null : 99, null,
            firstFailure ? BatteryAvailability.Unavailable : BatteryAvailability.Available, "Synthetic startup result"), null));
        await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(firstFailure ? BatteryAvailability.Unavailable : BatteryAvailability.Available, states[0].Current.Keyboard.Availability);
        clock.Advance(59); Assert.Equal(1, provider.Reads);
        await monitor.RefreshAsync(); Assert.Equal(2, provider.Reads); // Does not shift the timer.
        clock.Advance(1); await third.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, provider.Reads); Assert.Equal(99, states[2].Current.Keyboard.Percentage);
    }

    [Fact]
    public async Task ShutdownDuringInitialAttemptCancelsWithoutCreatingPollTimerOrPostingUpdate()
    {
        var clock = new ManualClock(); var provider = new StartupProvider();
        var monitor = new BatteryMonitoringService(provider, GHubWebSocketBatteryProvider.PollInterval, clock);
        var updates = 0; monitor.Updated += _ => updates++;
        monitor.Start(); await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await monitor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, provider.Reads); Assert.Equal(0, updates); Assert.False(clock.TimerCreated.Task.IsCompleted);
    }

    private sealed class StartupProvider : IBatteryProvider
    {
        public int Reads;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<BatteryReadResult> First = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<BatteryReadResult> ReadAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref Reads) == 1) { Entered.TrySetResult(); return await First.Task.WaitAsync(token); }
            return new(new("G915 Keyboard", 99, null, BatteryAvailability.Available, "Synthetic recovered response"), null);
        }
    }

    private sealed class Factory(params Socket[] sockets) : IGHubGatewayConnectionFactory
    {
        private readonly Queue<Socket> _queue = new(sockets);
        public int Creates;
        public IGHubGatewayConnection Create() { Creates++; return _queue.Dequeue(); }
    }
    private sealed class Socket(Func<string, string, byte[]> respond) : IGHubGatewayConnection
    {
        public Func<string, string, byte[]> Respond = respond;
        public List<string> Paths = [], Verbs = [], MsgIds = [];
        public List<byte[]> Prefix = [];
        private readonly Queue<byte[]> _frames = new(); private byte[]? _current; private int _offset;
        public bool Disposed, FailConnect, Hold, Repeat; public string? HoldStage; public int FragmentSize = 4096, CompletedMessages;
        public WebSocketMessageType MessageType = WebSocketMessageType.Text;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ConnectAsync(CancellationToken token)
        {
            if (HoldStage == "connect") { Entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
            if (FailConnect) throw new WebSocketException();
        }
        public async Task SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            if (HoldStage == "send") { Entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
            using var request = JsonDocument.Parse(bytes); var r = request.RootElement;
            var id = r.GetProperty("msgId").GetString()!; var path = r.GetProperty("path").GetString()!;
            Paths.Add(path); MsgIds.Add(id); Verbs.Add(r.GetProperty("verb").GetString()!);
            foreach (var prefix in Prefix) _frames.Enqueue(prefix); Prefix.Clear();
            _frames.Enqueue(Respond(id, path));
        }
        public async Task<GatewayFragment> ReceiveAsync(Memory<byte> buffer, CancellationToken token)
        {
            Entered.TrySetResult();
            if (Hold || HoldStage == "receive") await Task.Delay(Timeout.Infinite, token);
            if (_current is null) { _current = _frames.Dequeue(); _offset = 0; }
            var count = Math.Min(Math.Min(FragmentSize, buffer.Length), _current.Length - _offset);
            _current.AsMemory(_offset, count).CopyTo(buffer); _offset += count;
            var end = _offset == _current.Length;
            if (end) { CompletedMessages++; if (Repeat) _frames.Enqueue(_current); _current = null; }
            return new(count, MessageType, end);
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class CountingProvider : IBatteryProvider
    {
        public int Reads; public TaskCompletionSource Third = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BatteryReadResult> ReadAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref Reads) == 3) Third.TrySetResult();
            return Task.FromResult(new BatteryReadResult(BatteryDevice.UnsupportedMouse, null));
        }
    }
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-29T12:00:00Z");
        private readonly List<Timer> _timers = [];
        public TaskCompletionSource TimerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state, dueTime, period); _timers.Add(timer); TimerCreated.TrySetResult(); return timer;
        }
        public void Advance(int seconds)
        {
            _now = _now.AddSeconds(seconds);
            foreach (var t in _timers.ToArray()) t.Fire();
        }
        private sealed class Timer(ManualClock clock, TimerCallback callback, object? state, TimeSpan due, TimeSpan period) : ITimer
        {
            private DateTimeOffset _next = due == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : clock._now + due;
            private bool _disposed;
            public bool Change(TimeSpan dueTime, TimeSpan newPeriod) { _next = clock._now + dueTime; period = newPeriod; return true; }
            public void Fire() { if (!_disposed && clock._now >= _next) { _next = period == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : clock._now + period; callback(state); } }
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
