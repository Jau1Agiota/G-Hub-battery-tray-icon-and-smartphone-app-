using PeripheralBatteryCompanion.Core;
using Xunit;
namespace PeripheralBatteryCompanion.Tests;
public sealed class HyperXStartupTests
{
    [Fact]
    public async Task CleanStartupSchedulesWithoutSettingsOrSessionAcknowledgement()
    {
        await using var f = new Fixture();
        await f.Startup.StartAsync();
        Assert.True(f.Monitor.Status.Enabled); Assert.True(f.Provider.CanQuery);
        Assert.Equal(f.Clock.GetUtcNow().AddSeconds(120), f.Monitor.Status.NextCheck);
        Assert.Equal(1, f.Preflights); Assert.Equal(0, f.Inner.Calls);
        f.Clock.Advance(119); await f.Startup.TickAsync(); Assert.Equal(0, f.Inner.Calls);
        f.Clock.Advance(1); await f.Startup.TickAsync(); Assert.Equal(1, f.Inner.Calls);
        Assert.Equal(BatteryObservationState.CandidateAmbiguous, f.Provider.Current.ObservationState);
    }
    [Theory]
    [InlineData(60)] [InlineData(120)] [InlineData(300)]
    public async Task SavedCadenceAndRestartScheduleWithoutUi(int interval)
    {
        var store = new Store { Value = new(IntervalSeconds: interval) };
        await using (var first = new Fixture(store))
        {
            await first.Startup.StartAsync(); Assert.Equal(0, first.Inner.Calls);
            first.Clock.Advance(interval); await first.Startup.TickAsync(); Assert.Equal(1, first.Inner.Calls);
        }
        await using var second = new Fixture(store); await second.Startup.StartAsync();
        Assert.Equal(1, second.Preflights); Assert.Equal(0, second.Inner.Calls);
        Assert.Equal(second.Clock.GetUtcNow().AddSeconds(interval), second.Monitor.Status.NextCheck);
    }
    [Fact]
    public async Task ExplicitOffSurvivesRestartAndReenableWaitsFullInterval()
    {
        var store = new Store();
        await using (var f = new Fixture(store)) { await f.Startup.ConfigureAsync(false, 60); Assert.Equal(0, f.Preflights); }
        await using var next = new Fixture(store); await next.Startup.StartAsync();
        Assert.Equal(HyperXStartupPhase.UserDisabled, next.Startup.Phase); Assert.Equal(0, next.Preflights);
        next.Clock.Advance(5000); await next.Startup.TickAsync(); Assert.Equal(0, next.Inner.Calls);
        Assert.True(await next.Startup.ConfigureAsync(true, 60)); Assert.Equal(1, next.Preflights);
        Assert.Equal(0, next.Inner.Calls); next.Clock.Advance(60); await next.Startup.TickAsync(); Assert.Equal(1, next.Inner.Calls);
        Assert.True(store.Value.UserMonitoringEnabled); Assert.Equal(60, store.Value.IntervalSeconds);
    }
    [Theory]
    [InlineData(ReceiverPresence.Absent)] [InlineData(ReceiverPresence.Ambiguous)] [InlineData(ReceiverPresence.InspectionFailed)]
    public async Task MissingOrIncompatibleReceiverDefersPreflightAndReconnectWaits(ReceiverPresence presence)
    {
        await using var f = new Fixture(); f.Discovery.Presence = presence;
        await f.Startup.StartAsync(); f.Clock.Advance(1000); await f.Startup.TickAsync();
        Assert.Equal(0, f.Preflights); Assert.Equal(0, f.Inner.Calls);
        f.Discovery.Presence = ReceiverPresence.Detected; await f.Startup.TickAsync();
        Assert.Equal(1, f.Preflights); Assert.Equal(0, f.Inner.Calls);
        Assert.Equal(f.Clock.GetUtcNow().AddSeconds(120), f.Monitor.Status.NextCheck);
    }
    [Fact]
    public async Task RefreshMetadataAndReconnectNeverCauseImmediateRequest()
    {
        await using var f = new Fixture(); await f.Startup.StartAsync();
        await f.Monitor.RefreshMetadataAsync(); await f.Startup.TickAsync();
        f.Discovery.Presence = ReceiverPresence.Absent; f.Monitor.ReceiverRemoved(); await f.Startup.TickAsync();
        f.Discovery.Presence = ReceiverPresence.Detected; await f.Startup.TickAsync();
        Assert.Equal(0, f.Inner.Calls); Assert.Equal(1, f.Preflights);
        Assert.Equal(f.Clock.GetUtcNow().AddSeconds(120), f.Monitor.Status.NextCheck);
    }
    [Fact]
    public async Task PersistedBackoffAndFourthFailurePauseSurviveRestart()
    {
        var store = new Store();
        await using (var f = new Fixture(store))
        {
            f.Inner.Reply = new(HyperXQueryOutcome.Timeout, CompletionSafeCleanup: true);
            await f.Startup.StartAsync(); f.Clock.Advance(120);
            foreach (var delay in new[] { 120, 300, 900 })
            { await f.Startup.TickAsync(); Assert.Equal(f.Clock.GetUtcNow().AddSeconds(delay), f.Monitor.Status.NextCheck); f.Clock.Advance(delay); }
            await f.Startup.TickAsync(); Assert.True(store.Value.Suspended); Assert.Equal(4, store.Value.Failures);
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
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<HyperXQueryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Inner.Handler = _ => { entered.SetResult(); return reply.Task; };
        var manual = f.Startup.QueryManuallyAsync(); await entered.Task;
        Assert.True(store.Value.SafetyLatched); // A crash at this instant must not restart another exchange.
        f.Clock.Advance(120); await f.Startup.TickAsync(); await f.Startup.QueryManuallyAsync(); Assert.Equal(1, f.Inner.Calls);
        var exit = f.Startup.DisposeAsync().AsTask(); Assert.False(exit.IsCompleted);
        reply.SetResult(Candidate); await manual; await exit;
        Assert.Null(f.Provider.Current.Percentage);
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
        var store = new Store(); await using var f = new Fixture(store); await f.Startup.StartAsync();
        store.CanSave = false; f.Clock.Advance(120); await f.Startup.TickAsync();
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
    private sealed class Store : IHyperXStartupStore { public HyperXStartupPreferences Value=new();public bool CanSave=true;public HyperXStartupPreferences Load()=>Value;public bool Save(HyperXStartupPreferences value) {if(!CanSave)return false;Value=value;return true;} }
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
