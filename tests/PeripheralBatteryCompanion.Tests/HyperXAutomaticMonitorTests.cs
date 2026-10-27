using PeripheralBatteryCompanion.Core;
using PeripheralBatteryCompanion.HyperXHelperProtocol;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class HyperXAutomaticMonitorTests
{
    [Theory]
    [InlineData(60)] [InlineData(120)] [InlineData(300)]
    public async Task FirstAndSubsequentChecksWaitFullIntervalWithoutCatchup(int interval)
    {
        await using var f = new Fixture();
        await f.Ready(); f.Monitor.Configure(true, interval);
        await f.Monitor.TickAsync(); Assert.Equal(0, f.Transport.Calls);
        f.Clock.Advance(interval - 1); await f.Monitor.TickAsync(); Assert.Equal(0, f.Transport.Calls);
        f.Clock.Advance(1); await f.Monitor.TickAsync(); Assert.Equal(1, f.Transport.Calls);
        Assert.Equal(HyperXCheckOrigin.Scheduled, f.Monitor.Status.Origin);
        f.Clock.Advance(interval * 10); await f.Monitor.TickAsync(); Assert.Equal(2, f.Transport.Calls);
        await f.Monitor.TickAsync(); Assert.Equal(2, f.Transport.Calls);
        Assert.Equal(f.Clock.GetUtcNow().AddSeconds(interval), f.Monitor.Status.NextCheck);
        Assert.Equal(BatteryObservationState.CandidateAmbiguous, f.Provider.Current.ObservationState);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PreferencesNeverGrantSessionConsentOrPreflight(bool persistedEnabled)
    {
        var preferences = HyperXMonitoringPreferences.Parse(new HyperXMonitoringPreferences(persistedEnabled, 120).ToJson());
        await using var f = new Fixture();
        f.Monitor.Configure(preferences.Enabled, preferences.IntervalSeconds);
        await f.Monitor.TickAsync(); f.Clock.Advance(5000); await f.Monitor.TickAsync();
        Assert.Equal(0, f.Transport.Calls); Assert.Null(f.Monitor.Status.NextCheck);
        Assert.False(f.Provider.ExperimentalEnabled);
        f.Provider.ConfigureExperimentalQueries(true, true); f.Transport.Verified = false;
        await f.Monitor.TickAsync(); f.Clock.Advance(5000); await f.Monitor.TickAsync();
        Assert.Equal(0, f.Transport.Calls);
    }

    [Theory]
    [InlineData("{}")] [InlineData("bad json")] [InlineData("null")]
    [InlineData("{\"Enabled\":true,\"IntervalSeconds\":1}")]
    public void MissingOrInvalidPreferencesDefaultOff(string json)
    {
        var value = HyperXMonitoringPreferences.Parse(json);
        Assert.False(value.Enabled); Assert.Equal(120, value.IntervalSeconds);
    }

    [Fact]
    public async Task DefaultOffAndMetadataRefreshNeverQueryEvenAfterConsent()
    {
        await using var f = new Fixture(); await f.Ready();
        await f.Monitor.TickAsync(); f.Clock.Advance(10000); await f.Monitor.TickAsync();
        await f.Provider.ReadDeviceAsync(); Assert.Equal(0, f.Transport.Calls);
        await f.Monitor.QueryManuallyAsync(); Assert.Equal(1, f.Transport.Calls);
        Assert.Equal(HyperXCheckOrigin.Manual, f.Monitor.Status.Origin);
        Assert.Null(f.Monitor.Status.NextCheck);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ManualScheduledCollisionDropsOverlapAndRebasesNextCheck(bool manualFirst)
    {
        await using var f = new Fixture(); await f.Ready(); f.Monitor.Configure(true, 120);
        f.Clock.Advance(120);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<HyperXQueryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Transport.Handler = token => { entered.SetResult(); return reply.Task; };
        var first = manualFirst ? f.Monitor.QueryManuallyAsync() : f.Monitor.TickAsync();
        await entered.Task;
        await Task.WhenAll(f.Monitor.TickAsync(), f.Monitor.QueryManuallyAsync());
        Assert.True(f.Monitor.Status.Busy); Assert.Equal(1, f.Transport.Calls);
        reply.SetResult(Candidate); await first;
        Assert.False(f.Monitor.Status.Busy); Assert.Equal(1, f.Transport.Calls);
        Assert.Equal(f.Clock.GetUtcNow().AddSeconds(120), f.Monitor.Status.NextCheck);
    }

    [Fact]
    public async Task AbsentReceiverRetainsStaleUntilOneBoundedReconnectCheck()
    {
        await using var f = new Fixture(); await f.Ready(); f.Monitor.Configure(true, 120);
        await f.Monitor.QueryManuallyAsync(); var observed = f.Provider.Current.LastReported;
        f.Discovery.Presence = ReceiverPresence.Absent; f.Monitor.ReceiverRemoved();
        Assert.False(f.Provider.CanQuery); Assert.Equal(BatteryObservationState.Stale, f.Provider.Current.ObservationState);
        await f.Monitor.TickAsync(); f.Clock.Advance(5000); await f.Monitor.TickAsync();
        Assert.Equal(1, f.Transport.Calls); Assert.Null(f.Monitor.Status.NextCheck);
        f.Discovery.Presence = ReceiverPresence.Detected; await f.Monitor.TickAsync();
        Assert.Equal(BatteryObservationState.Stale, f.Provider.Current.ObservationState);
        Assert.Equal(observed, f.Provider.Current.LastReported); Assert.Equal(1, f.Transport.Calls);
        f.Clock.Advance(119); await f.Monitor.TickAsync(); Assert.Equal(1, f.Transport.Calls);
        f.Clock.Advance(1); await f.Monitor.TickAsync(); Assert.Equal(2, f.Transport.Calls);
    }

    [Theory]
    [InlineData(NgenuityPresence.Running)] [InlineData(NgenuityPresence.NotObserved)]
    public async Task ProductionMonitoringAllowsKnownProcessStatesWithoutClaimingFreshness(NgenuityPresence state)
    {
        await using var f = new Fixture(); f.Processes.Presence = state; await f.Ready();
        await f.Monitor.QueryManuallyAsync(); Assert.Equal(1, f.Transport.Calls);
        Assert.Equal(state, f.Provider.LastProcessPresence);
        Assert.Equal(BatteryObservationState.CandidateAmbiguous, f.Provider.Current.ObservationState);
        Assert.Equal("Unknown", f.Provider.Current.Connectivity);
    }

    [Fact]
    public async Task UnknownProcessStateFailsClosedWithoutNativeRequest()
    {
        await using var f = new Fixture(); f.Processes.Presence = NgenuityPresence.Unknown; await f.Ready();
        await f.Monitor.QueryManuallyAsync(); Assert.Equal(0, f.Transport.Calls);
        Assert.Null(f.Provider.Current.Percentage);
    }

    [Theory]
    [InlineData(HyperXQueryOutcome.Timeout)] [InlineData(HyperXQueryOutcome.AccessDenied)]
    [InlineData(HyperXQueryOutcome.InvalidResponse)] [InlineData(HyperXQueryOutcome.Unavailable)]
    public async Task CompletedFailuresBackOffTwoFiveFifteenMinutesThenSuspend(HyperXQueryOutcome outcome)
    {
        await using var f = new Fixture(); await f.Ready(); f.Monitor.Configure(true, 120);
        f.Transport.Reply = new(outcome, Win32Error: 32, CompletionSafeCleanup: true);
        f.Clock.Advance(120);
        foreach (var seconds in new[] { 120, 300, 900 })
        {
            var before = f.Transport.Calls;
            await f.Monitor.TickAsync(); Assert.Equal(before + 1, f.Transport.Calls);
            Assert.Equal(f.Clock.GetUtcNow().AddSeconds(seconds), f.Monitor.Status.NextCheck);
            f.Clock.Advance(seconds - 1); await f.Monitor.TickAsync(); Assert.Equal(before + 1, f.Transport.Calls);
            f.Clock.Advance(1);
        }
        await f.Monitor.TickAsync(); Assert.Equal(4, f.Transport.Calls);
        Assert.True(f.Monitor.Status.Suspended); Assert.Null(f.Monitor.Status.NextCheck);
        f.Clock.Advance(5000); await f.Monitor.TickAsync(); Assert.Equal(4, f.Transport.Calls);
        Assert.True(f.Monitor.Resume()); await f.Monitor.TickAsync(); Assert.Equal(4, f.Transport.Calls);
        f.Transport.Reply = Candidate; f.Clock.Advance(120); await f.Monitor.TickAsync();
        Assert.Equal(5, f.Transport.Calls); Assert.Equal(0, f.Monitor.Status.ConsecutiveFailures);
    }

    [Fact]
    public async Task ChangingIntervalOrReconnectingCannotShortenFailureBackoff()
    {
        await using var f = new Fixture(); await f.Ready(); f.Monitor.Configure(true, 120);
        f.Transport.Reply = new(HyperXQueryOutcome.Timeout, CompletionSafeCleanup: true);
        await f.Monitor.QueryManuallyAsync(); f.Clock.Advance(120); await f.Monitor.TickAsync();
        var retry = f.Monitor.Status.NextCheck;
        f.Monitor.Configure(true, 60); Assert.Equal(retry, f.Monitor.Status.NextCheck);
        f.Discovery.Presence = ReceiverPresence.Absent; await f.Monitor.TickAsync();
        f.Discovery.Presence = ReceiverPresence.Detected; await f.Monitor.TickAsync();
        Assert.Equal(retry, f.Monitor.Status.NextCheck);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task UnsafeCleanupOrTransportLatchCannotBeResumed(bool verifiedCleanup)
    {
        await using var f = new Fixture(); await f.Ready(); f.Monitor.Configure(true, 120);
        f.Transport.Handler = _ => { if (verifiedCleanup) f.Transport.Verified = false;
            return Task.FromResult(new HyperXQueryResult(HyperXQueryOutcome.Failed, CompletionSafeCleanup: verifiedCleanup)); };
        await f.Monitor.QueryManuallyAsync(); Assert.True(f.Monitor.Status.Suspended);
        Assert.False(f.Monitor.Resume()); f.Clock.Advance(5000); await f.Monitor.TickAsync();
        Assert.Equal(1, f.Transport.Calls);
    }

    [Fact]
    public async Task CancelAndDisposeWaitForCleanupAndRejectLateCandidate()
    {
        await using var f = new Fixture(); await f.Ready(); f.Monitor.Configure(true, 120);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource<HyperXQueryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Transport.Handler = _ => { entered.SetResult(); return cleanup.Task; };
        var query = f.Monitor.QueryManuallyAsync(); await entered.Task;
        f.Monitor.Cancel(); var disposal = f.Monitor.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted); Assert.Null(f.Provider.Current.Percentage);
        cleanup.SetResult(Candidate); await query; await disposal;
        Assert.Null(f.Provider.Current.Percentage); Assert.Null(f.Monitor.Status.NextCheck);
        f.Clock.Advance(10000); await f.Monitor.TickAsync(); Assert.Equal(1, f.Transport.Calls);
    }

    [Fact]
    public async Task RemovalDuringQueryCancelsAndLateCandidateCannotBecomeFresh()
    {
        await using var f = new Fixture(); await f.Ready();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<HyperXQueryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Transport.Handler = _ => { entered.SetResult(); return reply.Task; };
        var active = f.Monitor.QueryManuallyAsync(); await entered.Task;
        f.Discovery.Presence = ReceiverPresence.Absent; f.Monitor.ReceiverRemoved();
        Assert.False(f.Provider.CanQuery); Assert.Equal(ReceiverPresence.Absent, f.Provider.Current.Receiver);
        reply.SetResult(Candidate); await active;
        Assert.Null(f.Provider.Current.Percentage); Assert.True(f.Monitor.Status.Suspended);
        Assert.Null(f.Monitor.Status.NextCheck);
    }

    [Fact]
    public async Task DisableCancelsOwnedCheckAndRequiresExplicitResume()
    {
        await using var f = new Fixture(); await f.Ready(); f.Monitor.Configure(true, 120);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<HyperXQueryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Transport.Handler = _ => { entered.SetResult(); return reply.Task; };
        var active = f.Monitor.QueryManuallyAsync(); await entered.Task;
        f.Monitor.Configure(false, 120); reply.SetResult(Candidate); await active;
        Assert.Null(f.Provider.Current.Percentage); Assert.False(f.Monitor.Status.Enabled);
        Assert.True(f.Monitor.Status.Suspended);
        f.Monitor.Configure(true, 120); f.Clock.Advance(1000); await f.Monitor.TickAsync();
        Assert.Equal(1, f.Transport.Calls); Assert.True(f.Monitor.Resume());
        Assert.Equal(f.Clock.GetUtcNow().AddSeconds(120), f.Monitor.Status.NextCheck);
    }

    [Fact]
    public async Task TurnOffCancelsActiveQueryAndNeverPersistsConsent()
    {
        await using var f = new Fixture(); await f.Ready(); f.Monitor.Configure(true, 120);
        f.Monitor.Configure(false, 300); f.Clock.Advance(5000); await f.Monitor.TickAsync();
        Assert.Equal(0, f.Transport.Calls); Assert.False(f.Monitor.Status.Enabled);
        Assert.DoesNotContain("Acknowledge", new HyperXMonitoringPreferences(true, 300).ToJson());
    }

    [Theory]
    [InlineData(HelperProcessObservation.Running)] [InlineData(HelperProcessObservation.NotObserved)]
    public void ProtocolAcceptsValidatedAmbiguousCandidateWithOrWithoutNgenuity(HelperProcessObservation presence)
    {
        var nonce = Guid.NewGuid().ToString("N"); var bytes = HelperProtocol.Request(); bytes[4] = 52;
        var reply = new HelperReply(1, nonce, HelperMode.NativeOneShot, HelperStatus.CandidateObservedAmbiguous,
            Ngenuity: presence, CapabilitiesValid: true, SharedOpenSucceeded: true, RequestAttempted: true,
            OutputCompleted: true, Response: bytes, NativeCleanupCompleted: true);
        Assert.True(HelperProtocol.ValidReply(reply, nonce, HelperMode.NativeOneShot));
        Assert.False(HelperProtocol.ValidReply(reply with { Ngenuity = HelperProcessObservation.Unknown }, nonce, HelperMode.NativeOneShot));
    }

    private static readonly HyperXQueryResult Candidate = new(HyperXQueryOutcome.CandidateObserved, 52, true, true, CompletionSafeCleanup: true);
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 27, 16, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }
    private sealed class Discovery : IHyperXReceiverDiscovery
    {
        public ReceiverPresence Presence = ReceiverPresence.Detected;
        public Task<HyperXDiscoveryResult> DiscoverAsync(CancellationToken token) => Task.FromResult(new HyperXDiscoveryResult(Presence));
    }
    private sealed class Processes : INgenuityPresenceProbe
    {
        public NgenuityPresence Presence = NgenuityPresence.Running;
        public Task<NgenuityPresence> InspectAsync(CancellationToken token) => Task.FromResult(Presence);
    }
    private sealed class Transport : IHyperXManualQueryTransport
    {
        public bool Verified = true; public bool HasVerifiedLongRunningLifetime => Verified;
        public int Calls; public HyperXQueryResult Reply = Candidate;
        public Func<CancellationToken, Task<HyperXQueryResult>>? Handler;
        public Task<HyperXQueryResult> QueryOnceAsync(CancellationToken token) { Calls++; return Handler?.Invoke(token) ?? Task.FromResult(Reply); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly Clock Clock = new(); public readonly Discovery Discovery = new(); public readonly Processes Processes = new();
        public readonly Transport Transport = new(); public readonly HyperXBatteryProvider Provider; public readonly HyperXAutomaticMonitor Monitor;
        public Fixture() { Provider = new(Discovery, Processes, Transport, Clock, allowWithoutNgenuity: true); Monitor = new(Provider, Clock); }
        public async Task Ready() { Provider.ConfigureExperimentalQueries(true, true); await Provider.ReadDeviceAsync(); }
        public async ValueTask DisposeAsync() { await Monitor.DisposeAsync(); await Provider.DisposeAsync(); }
    }
}
