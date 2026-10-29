using PeripheralBatteryCompanion.Core;
using Xunit;
namespace PeripheralBatteryCompanion.Tests;
public sealed class HyperXStartupTests
{
    [Fact]
    public async Task CleanStartupReadsOnceAfterReadinessThenWaitsNormalInterval()
    {
        await using var f = new Fixture();
        await f.Startup.StartAsync();
        Assert.True(f.Monitor.Status.Enabled); Assert.True(f.Provider.CanQuery);
        Assert.Equal(f.Clock.GetUtcNow().AddSeconds(120), f.Monitor.Status.NextCheck);
        Assert.Equal(1, f.Preflights); Assert.Equal(1, f.Inner.Calls);
        f.Clock.Advance(119); await f.Startup.TickAsync(); Assert.Equal(1, f.Inner.Calls);
        f.Clock.Advance(1); await f.Startup.TickAsync(); Assert.Equal(2, f.Inner.Calls);
        Assert.Equal(BatteryObservationState.CandidateAmbiguous, f.Provider.Current.ObservationState);
    }
    [Theory]
    [InlineData(60)] [InlineData(120)] [InlineData(300)]
    public async Task SavedCadenceAndRestartScheduleWithoutUi(int interval)
    {
        var store = new Store { Value = new(IntervalSeconds: interval) };
        await using (var first = new Fixture(store))
        {
            await first.Startup.StartAsync(); Assert.Equal(1, first.Inner.Calls);
            first.Clock.Advance(interval); await first.Startup.TickAsync(); Assert.Equal(2, first.Inner.Calls);
        }
        await using var second = new Fixture(store); await second.Startup.StartAsync();
        Assert.Equal(1, second.Preflights); Assert.Equal(1, second.Inner.Calls);
        Assert.Equal(second.Clock.GetUtcNow().AddSeconds(interval), second.Monitor.Status.NextCheck);
    }
    [Fact]
    public async Task ExplicitOffSurvivesRestartAndEnableMakesFirstEligibleRead()
    {
        var store = new Store();
        await using (var f = new Fixture(store)) { await f.Startup.ConfigureAsync(false, 60); Assert.Equal(0, f.Preflights); }
        await using var next = new Fixture(store); await next.Startup.StartAsync();
        Assert.Equal(HyperXStartupPhase.UserDisabled, next.Startup.Phase); Assert.Equal(0, next.Preflights);
        next.Clock.Advance(5000); await next.Startup.TickAsync(); Assert.Equal(0, next.Inner.Calls);
        Assert.True(await next.Startup.ConfigureAsync(true, 60)); Assert.Equal(1, next.Preflights);
        Assert.Equal(1, next.Inner.Calls); next.Clock.Advance(60); await next.Startup.TickAsync(); Assert.Equal(2, next.Inner.Calls);
        Assert.True(store.Value.UserMonitoringEnabled); Assert.Equal(60, store.Value.IntervalSeconds);
    }
    [Theory]
    [InlineData(ReceiverPresence.Absent)] [InlineData(ReceiverPresence.Ambiguous)] [InlineData(ReceiverPresence.InspectionFailed)]
    public async Task MissingReceiverDefersInitialReadUntilFirstEligibleReadiness(ReceiverPresence presence)
    {
        await using var f = new Fixture(); f.Discovery.Presence = presence;
        await f.Startup.StartAsync(); f.Clock.Advance(1000); await f.Startup.TickAsync();
        Assert.Equal(0, f.Preflights); Assert.Equal(0, f.Inner.Calls);
        f.Discovery.Presence = ReceiverPresence.Detected; await f.Startup.TickAsync();
        Assert.Equal(1, f.Preflights); Assert.Equal(1, f.Inner.Calls);
        Assert.Equal(f.Clock.GetUtcNow().AddSeconds(120), f.Monitor.Status.NextCheck);
    }
    [Fact]
    public async Task RefreshMetadataAndReconnectNeverCauseImmediateRequest()
    {
        await using var f = new Fixture(); await f.Startup.StartAsync();
        await f.Monitor.RefreshMetadataAsync(); await f.Startup.TickAsync();
        f.Discovery.Presence = ReceiverPresence.Absent; f.Monitor.ReceiverRemoved(); await f.Startup.TickAsync();
        f.Discovery.Presence = ReceiverPresence.Detected; await f.Startup.TickAsync();
        Assert.Equal(1, f.Inner.Calls); Assert.Equal(1, f.Preflights);
        Assert.Equal(f.Clock.GetUtcNow().AddSeconds(120), f.Monitor.Status.NextCheck);
    }
    [Fact]
    public async Task PersistedBackoffAndFourthFailurePauseSurviveRestart()
    {
        var store = new Store();
        await using (var f = new Fixture(store))
        {
            f.Inner.Reply = new(HyperXQueryOutcome.Timeout, CompletionSafeCleanup: true);
            await f.Startup.StartAsync(); Assert.Equal(1, f.Inner.Calls);
            foreach (var delay in new[] { 120, 300, 900 })
            { Assert.Equal(f.Clock.GetUtcNow().AddSeconds(delay), f.Monitor.Status.NextCheck); f.Clock.Advance(delay); await f.Startup.TickAsync(); }
            Assert.True(store.Value.Suspended); Assert.Equal(4, store.Value.Failures); Assert.Equal(4, f.Inner.Calls);
        }
        await using var next = new Fixture(store); await next.Startup.StartAsync(); next.Clock.Advance(10000); await next.Startup.TickAsync();
        Assert.Equal(0, next.Inner.Calls); Assert.Equal(HyperXStartupPhase.RecoverableSuspended, next.Startup.Phase);
        Assert.True(next.Startup.Resume()); Assert.Equal(next.Clock.GetUtcNow().AddSeconds(120), next.Monitor.Status.NextCheck);
    }
    [Fact]
    public async Task RestartAndIntervalChangesDoNotShortenBackoffDeadline()
    {
        var retry = new Clock().GetUtcNow().AddMinutes(15);
        var store = new Store { Value = new(Failures: 3, RetryNotBefore: retry) };
        await using var f = new Fixture(store); await f.Startup.StartAsync();
        Assert.Equal(retry, f.Monitor.Status.NextCheck);
        await f.Startup.ConfigureAsync(true, 60); Assert.Equal(retry, f.Monitor.Status.NextCheck);
        f.Clock.Advance(899); await f.Startup.TickAsync(); Assert.Equal(0, f.Inner.Calls);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedPreflightOrPersistedLatchNeverClearedByToggle(bool persisted)
    {
        var store = new Store { Value = new(SafetyLatched: persisted) };
        await using var f = new Fixture(store); f.PreflightResult = false;
        await f.Startup.StartAsync(); await f.Startup.ConfigureAsync(false, 120); await f.Startup.ConfigureAsync(true, 120);
        Assert.False(f.Startup.Resume()); Assert.Equal(0, f.Inner.Calls);
        Assert.Equal(persisted ? 0 : 1, f.Preflights); Assert.True(store.Value.SafetyLatched);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task UnsafeCleanupOrTransportCrashPersistsSafetyStopAcrossRestart(bool crash)
    {
        var store = new Store();
        await using (var f = new Fixture(store))
        {
            f.Inner.Handler = _ => crash ? throw new IOException("private data") : Task.FromResult(new HyperXQueryResult(HyperXQueryOutcome.Failed, CompletionSafeCleanup: false));
            await f.Startup.StartAsync(); f.Clock.Advance(120); await f.Startup.TickAsync();
            Assert.True(store.Value.SafetyLatched); Assert.Null(f.Provider.Current.Percentage);
        }
        await using var next = new Fixture(store); await next.Startup.StartAsync();
        Assert.Equal(0, next.Preflights); Assert.Equal(0, next.Inner.Calls); Assert.False(next.Startup.Resume());
    }
    [Fact]
    public async Task NormalExitWithLatchedInnerTransportStillRequiresPersistentSafetyReview()
    {
        var store = new Store(); await using var f = new Fixture(store);
        f.Inner.Handler = _ => { f.Inner.Verified = false; return Task.FromResult(new HyperXQueryResult(HyperXQueryOutcome.Failed, CompletionSafeCleanup: true)); };
        await f.Startup.StartAsync(); f.Clock.Advance(120); await f.Startup.TickAsync();
        Assert.True(store.Value.SafetyLatched); Assert.False(f.Startup.Resume());
        await f.Startup.ConfigureAsync(false, 120); await f.Startup.ConfigureAsync(true, 120);
        Assert.Equal(1, f.Inner.Calls);
    }
    [Fact]
    public async Task PreflightWritesCrashMarkerAndCannotStartIfPersistenceFails()
    {
        var store = new Store { CanSave = false }; await using var f = new Fixture(store);
        await f.Startup.StartAsync(); Assert.Equal(0, f.Preflights); Assert.Equal(0, f.Inner.Calls);
        Assert.Equal(HyperXStartupPhase.SafetyLatched, f.Startup.Phase);
    }

    [Fact]
    public async Task SingleFlightDurableMarkerAndShutdownRejectLateCandidate()
    {
        var store = new Store(); await using var f = new Fixture(store); await f.Startup.StartAsync();
        var observed = f.Provider.Current.LastReported;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<HyperXQueryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Inner.Handler = _ => { entered.SetResult(); return reply.Task; };
        var manual = f.Startup.QueryManuallyAsync(); await entered.Task;
        Assert.True(store.Value.SafetyLatched); // A crash at this instant must not restart another exchange.
        f.Clock.Advance(120); await f.Startup.TickAsync(); await f.Startup.QueryManuallyAsync(); Assert.Equal(2, f.Inner.Calls);
        var exit = f.Startup.DisposeAsync().AsTask(); Assert.False(exit.IsCompleted);
        reply.SetResult(Candidate with { CandidatePercentage = 66 }); await manual; await exit;
        Assert.Equal(50, f.Provider.Current.Percentage); // Retained first value, never the late 66 candidate.
        Assert.Equal(observed, f.Provider.Current.LastReported);
        Assert.Equal(BatteryObservationState.Stale, f.Provider.Current.ObservationState);
        Assert.Equal(BatteryAvailability.Unavailable, f.Provider.Current.Availability);
    }
    [Fact]
    public async Task OffWhilePreflightPendingCannotBeOverwrittenByLateReadiness()
    {
        await using var f = new Fixture(); var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Prepare = _ => ready.Task; var startup = f.Startup.StartAsync();
        await f.Startup.ConfigureAsync(false, 120); ready.SetResult(true); await startup;
        Assert.False(f.Provider.CanQuery); Assert.Equal(0, f.Inner.Calls); Assert.False(f.State.Current.UserMonitoringEnabled);
    }
    [Fact]
    public async Task FailedPersistenceBlocksBeforeNativeExchange()
    {
        var store = new Store(); await using var f = new Fixture(store);
        // Allow safe preflight completion, then fail the durable marker for the first exchange.
        store.AfterSave = p => { if (!p.SafetyLatched) store.CanSave = false; };
        await f.Startup.StartAsync();
        Assert.Equal(0, f.Inner.Calls); Assert.False(f.Provider.CanQuery); Assert.Null(f.Provider.Current.Percentage);
    }
    [Theory]
    [InlineData(null)] [InlineData("{}")] [InlineData("bad")] [InlineData("null")]
    [InlineData("{\"IntervalSeconds\":1}")] [InlineData("{\"IntervalSeconds\":\"bad\"}")]
    public void MissingAndInvalidPreferencesDefaultOnAt120(string? json)
    {
        var value = HyperXStartupPreferences.Parse(json); Assert.True(value.UserMonitoringEnabled); Assert.Equal(120, value.IntervalSeconds);
        Assert.True(HyperXStartupPreferences.Parse(new string('x', 4097)).UserMonitoringEnabled);
    }
    [Fact]
    public void LegacyFalseIsConservativelyRetainedButOldAcknowledgementIsNotRequired()
    {
        Assert.False(HyperXStartupPreferences.Parse("{\"Enabled\":false,\"IntervalSeconds\":60}").UserMonitoringEnabled);
        var value = HyperXStartupPreferences.Parse("{\"ExperimentalEnabled\":false,\"Acknowledged\":false,\"IntervalSeconds\":300}");
        Assert.True(value.UserMonitoringEnabled); Assert.Equal(300, value.IntervalSeconds);
        Assert.Equal(value, HyperXStartupPreferences.Parse(value.ToJson()));
    }
    [Fact]
    public async Task Stage52FirstEligibleReadDoesNotWaitForPeriodicInterval()
    {
        await using var f = new Fixture(new Store { Value = new(IntervalSeconds: 60) });
        await f.Startup.StartAsync();
        Assert.Equal(1, f.Preflights);
        Assert.Equal(1, f.Inner.Calls);
        Assert.Equal(BatteryObservationState.CandidateAmbiguous, f.Provider.Current.ObservationState);
        Assert.Equal(f.Clock.GetUtcNow().AddSeconds(60), f.Monitor.Status.NextCheck);
    }

    [Fact]
    public async Task RefreshDuringPreflightCoalescesIntoOneFirstReadAndEarlyRefreshWorksAfterReady()
    {
        await using var f = new Fixture(new Store { Value = new(IntervalSeconds: 60) });
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Prepare = _ => ready.Task;
        var start = f.Startup.StartAsync();
        Assert.Equal(HyperXStartupPhase.PreflightPending, f.Startup.Phase);
        Assert.Equal(0, f.Inner.Calls);
        await Task.WhenAll(f.Startup.QueryManuallyAsync(), f.Startup.QueryManuallyAsync(), f.Startup.TickAsync());
        ready.SetResult(true); await start;
        Assert.Equal(1, f.Preflights); Assert.Equal(1, f.Inner.Calls);
        f.Clock.Advance(10); await f.Startup.QueryManuallyAsync();
        Assert.Equal(2, f.Inner.Calls); Assert.Equal(HyperXCheckOrigin.Manual, f.Monitor.Status.Origin);
        Assert.Equal(f.Clock.GetUtcNow().AddSeconds(60), f.Monitor.Status.NextCheck);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ManualRefreshHonorsOffAndSuspensionWithoutEnablingOrQuerying(bool suspended)
    {
        var store = new Store { Value = new(UserMonitoringEnabled: suspended, Suspended: suspended) };
        await using var f = new Fixture(store);
        await f.Startup.QueryManuallyAsync(); await f.Startup.StartAsync();
        f.Clock.Advance(500); await f.Startup.TickAsync(); await f.Startup.QueryManuallyAsync();
        Assert.Equal(0, f.Inner.Calls); Assert.Null(f.Provider.Current.Percentage);
        Assert.Equal(suspended, store.Value.UserMonitoringEnabled);
        Assert.Equal(suspended, f.Monitor.Status.Suspended);
        if (!suspended) { Assert.Equal(0, f.Preflights); Assert.Equal(HyperXStartupPhase.UserDisabled, f.Startup.Phase); }
        else
        {
            Assert.Equal(HyperXStartupPhase.RecoverableSuspended, f.Startup.Phase);
            Assert.True(f.Startup.Resume()); await f.Startup.TickAsync(); Assert.Equal(0, f.Inner.Calls);
            f.Clock.Advance(120); await f.Startup.TickAsync(); Assert.Equal(1, f.Inner.Calls);
        }
    }

    [Fact]
    public async Task StartupFailureBackoffCannotBeBypassedByRefreshOrRestart()
    {
        var store = new Store();
        await using (var first = new Fixture(store))
        {
            first.Inner.Reply = new(HyperXQueryOutcome.Timeout, CompletionSafeCleanup: true);
            await first.Startup.StartAsync(); Assert.Equal(1, first.Inner.Calls);
            var retry = first.Monitor.Status.NextCheck;
            for (var i = 0; i < 3; i++) { await first.Startup.QueryManuallyAsync(); await first.Startup.TickAsync(); }
            Assert.Equal(1, first.Inner.Calls); Assert.Equal(retry, first.Monitor.Status.NextCheck);
            Assert.Equal(1, store.Value.Failures);
        }
        await using var restart = new Fixture(store);
        await restart.Startup.StartAsync(); await restart.Startup.QueryManuallyAsync(); Assert.Equal(0, restart.Inner.Calls);
        restart.Clock.Advance(120); await restart.Startup.TickAsync(); Assert.Equal(1, restart.Inner.Calls);
        Assert.Equal(0, store.Value.Failures);
    }

    [Fact]
    public async Task InitialQueryCollisionsAndRemovalNeverPublishLateCandidate()
    {
        await using var f = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<HyperXQueryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Inner.Handler = _ => { entered.TrySetResult(); return reply.Task; };
        var start = f.Startup.StartAsync(); await entered.Task;
        await Task.WhenAll(f.Startup.QueryManuallyAsync(), f.Startup.TickAsync()); Assert.Equal(1, f.Inner.Calls);
        f.Discovery.Presence = ReceiverPresence.Absent; f.Monitor.ReceiverRemoved();
        reply.SetResult(Candidate); await start;
        Assert.Null(f.Provider.Current.Percentage); Assert.True(f.Monitor.Status.Suspended);
        Assert.Equal(ReceiverPresence.Absent, f.Provider.Current.Receiver);
        await f.Startup.QueryManuallyAsync(); Assert.Equal(1, f.Inner.Calls);
    }

    [Fact]
    public async Task ShutdownDuringPreparationCancelsAndJoinsWithoutQuery()
    {
        await using var f = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Prepare = async token => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return true; };
        var start = f.Startup.StartAsync(); await entered.Task;
        await f.Startup.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); await start;
        Assert.Equal(0, f.Inner.Calls); Assert.Null(f.Provider.Current.Percentage);
        await f.Startup.QueryManuallyAsync(); Assert.Equal(0, f.Inner.Calls);
    }

    [Fact]
    public async Task FailedPreflightIsNotRetriedByRepeatedRefreshAndNeverEntersTransport()
    {
        await using var f = new Fixture(); f.PreflightResult = false;
        await f.Startup.StartAsync();
        await f.Startup.QueryManuallyAsync(); f.Clock.Advance(1000); await f.Startup.TickAsync();
        Assert.Equal(1, f.Preflights); Assert.Equal(0, f.Inner.Calls);
        Assert.Equal(HyperXStartupPhase.SafetyLatched, f.Startup.Phase);
    }

    private static readonly HyperXQueryResult Candidate = new(HyperXQueryOutcome.CandidateObserved, 50, true, true, CompletionSafeCleanup: true);
    private sealed class Clock : TimeProvider { private DateTimeOffset _now = new(2026,10,28,0,0,0,TimeSpan.Zero); public override DateTimeOffset GetUtcNow()=>_now; public void Advance(int seconds)=>_now=_now.AddSeconds(seconds); }
    private sealed class Discovery : IHyperXReceiverDiscovery { public ReceiverPresence Presence=ReceiverPresence.Detected; public Task<HyperXDiscoveryResult> DiscoverAsync(CancellationToken token)=>Task.FromResult(new HyperXDiscoveryResult(Presence)); }
    private sealed class Processes : INgenuityPresenceProbe { public Task<NgenuityPresence> InspectAsync(CancellationToken token)=>Task.FromResult(NgenuityPresence.NotObserved); }
    private sealed class Transport : IHyperXManualQueryTransport
    {
        public bool Verified = true; public bool HasVerifiedLongRunningLifetime=>Verified; public int Calls; public HyperXQueryResult Reply=Candidate;
        public Func<CancellationToken,Task<HyperXQueryResult>>? Handler;
        public Task<HyperXQueryResult> QueryOnceAsync(CancellationToken token) {Calls++;return Handler?.Invoke(token)??Task.FromResult(Reply);}
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    private sealed class Store : IHyperXStartupStore { public HyperXStartupPreferences Value=new();public bool CanSave=true;public Action<HyperXStartupPreferences>? AfterSave;public HyperXStartupPreferences Load()=>Value;public bool Save(HyperXStartupPreferences value) {if(!CanSave)return false;Value=value;AfterSave?.Invoke(value);return true;} }
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly Clock Clock=new();public readonly Discovery Discovery=new();public readonly Transport Inner=new();
        public readonly HyperXStartupState State;public readonly HyperXBatteryProvider Provider;public readonly HyperXAutomaticMonitor Monitor;public readonly HyperXStartupCoordinator Startup;
        public int Preflights; public bool PreflightResult=true;public Func<CancellationToken,Task<bool>>? Prepare;
        public Fixture(Store? store=null)
        {
            State=new(store??new Store());var durable=new HyperXDurableTransport(Inner,State,Clock);
            Provider=new(Discovery,new Processes(),durable,Clock,allowWithoutNgenuity:true);Monitor=new(Provider,Clock);
            Startup=new(Provider,Monitor,token=>{Preflights++;return Prepare?.Invoke(token)??Task.FromResult(PreflightResult);},State);
        }
        public async ValueTask DisposeAsync() {await Startup.DisposeAsync();await Monitor.DisposeAsync();await Provider.DisposeAsync();}
    }
}
