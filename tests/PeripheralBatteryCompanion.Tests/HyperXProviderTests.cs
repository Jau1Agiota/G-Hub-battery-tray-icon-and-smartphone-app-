using PeripheralBatteryCompanion.Core;
using PeripheralBatteryCompanion.Core.Presentation;
using PeripheralBatteryCompanion.G305Inspector;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class HyperXProviderTests
{
    [Fact]
    public async Task DefaultProviderDiscoversReceiverWithoutQueryAndNeverFabricates56Percent()
    {
        var discovery = new Discovery(); var processes = new Processes(); var transport = new Transport();
        await using var provider = new HyperXBatteryProvider(discovery, processes, transport);
        await provider.ReadDeviceAsync();
        await provider.QueryOnceAsync();
        Assert.Equal(1, discovery.Calls); Assert.Equal(0, processes.Calls); Assert.Equal(0, transport.Calls);
        Assert.False(provider.ExperimentalEnabled); Assert.False(provider.CanQuery);
        Assert.Equal(ReceiverPresence.Detected, provider.Current.Receiver);
        Assert.Equal("Unknown", provider.Current.Connectivity);
        Assert.Null(provider.Current.Percentage); Assert.Null(provider.Current.LastReported);
        Assert.Equal(BatteryObservationState.Unknown, provider.Current.ObservationState);
        Assert.Contains("Battery: Unknown", HyperXPanelView.From(provider).BatteryText);
    }

    [Fact]
    public async Task ProductionCompositionHasNoTransportAndOptInCannotBypassLifetimeBlocker()
    {
        await using var provider = new HyperXBatteryProvider(new Discovery(), new Processes());
        await provider.ReadDeviceAsync();
        Assert.False(provider.ConfigureExperimentalQueries(true));
        Assert.True(provider.ConfigureExperimentalQueries(true, true));
        await provider.QueryOnceAsync();
        Assert.False(provider.CanQuery); Assert.Equal(HyperXStage.LifetimeGate, provider.Diagnostic.Stage);
        Assert.False(provider.Diagnostic.RequestAttempted);
        Assert.Equal(HyperXBatteryProvider.LifetimeBlocker, provider.QueryBlocker);
        Assert.Null(provider.Current.Percentage);
    }

    [Fact]
    public async Task UnverifiedTransportCannotRunEvenAfterOptIn()
    {
        var transport = new Transport { Verified = false };
        await using var provider = new HyperXBatteryProvider(new Discovery(), new Processes(), transport);
        provider.ConfigureExperimentalQueries(true, true);
        await provider.QueryOnceAsync();
        Assert.Equal(0, transport.Calls); Assert.Equal(HyperXStage.LifetimeGate, provider.Diagnostic.Stage);
    }

    [Theory]
    [InlineData(ReceiverPresence.Absent)] [InlineData(ReceiverPresence.Ambiguous)] [InlineData(ReceiverPresence.InspectionFailed)]
    public async Task MissingAmbiguousOrFailedDiscoveryDoesNotAttemptExchange(ReceiverPresence presence)
    {
        var discovery = new Discovery { Result = new(presence, 5) }; var transport = new Transport();
        await using var provider = new HyperXBatteryProvider(discovery, new Processes(), transport);
        provider.ConfigureExperimentalQueries(true, true);
        await provider.ReadDeviceAsync();
        Assert.Equal(5, provider.Diagnostic.Win32Error);
        await provider.QueryOnceAsync();
        Assert.Equal(presence, provider.Current.Receiver);
        Assert.Equal(BatteryObservationState.Unavailable, provider.Current.ObservationState);
        Assert.False(provider.CanQuery); Assert.Equal(0, transport.Calls);
    }

    [Theory]
    [InlineData(NgenuityPresence.Unknown)] [InlineData(NgenuityPresence.NotObserved)]
    public async Task ConcurrentProcessGuardRefusesUnknownAndClosedApp(NgenuityPresence state)
    {
        var transport = new Transport();
        await using var provider = new HyperXBatteryProvider(new Discovery(), new Processes { Presence = state }, transport);
        provider.ConfigureExperimentalQueries(true, true);
        await provider.QueryOnceAsync();
        Assert.Equal(HyperXStage.ProcessGuard, provider.Diagnostic.Stage); Assert.Equal(0, transport.Calls);
        Assert.False(provider.Diagnostic.RequestAttempted);
    }

    [Fact]
    public async Task CandidateIsAlwaysAmbiguousWithObservedTimestampAndSource()
    {
        var time = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        await using var provider = new HyperXBatteryProvider(new Discovery(), new Processes(), new Transport(), new Clock(time));
        provider.ConfigureExperimentalQueries(true, true);
        await provider.QueryOnceAsync();
        Assert.Equal(56, provider.Current.Percentage); Assert.Equal(time, provider.Current.LastReported);
        Assert.Equal(BatteryObservationState.CandidateAmbiguous, provider.Current.ObservationState);
        Assert.Contains("ownership ambiguous", provider.Current.Source);
        var view = HyperXPanelView.From(provider);
        Assert.Contains("candidate: 56% (ambiguous)", view.BatteryText);
        Assert.DoesNotContain("ConfirmedFresh", view.BatteryText);
        Assert.Contains("headset connectivity: Unknown", view.ReceiverText);
        Assert.True(provider.Diagnostic.ResponseOwnershipAmbiguous);
    }

    [Fact]
    public async Task DisconnectReconnectRetainsOnlyExplicitStaleCandidateAndNeverRefreshesItsTimestamp()
    {
        var discovery = new Discovery(); var transport = new Transport();
        await using var provider = new HyperXBatteryProvider(discovery, new Processes(), transport);
        provider.ConfigureExperimentalQueries(true, true);
        await provider.QueryOnceAsync();
        var observed = provider.Current.LastReported;
        discovery.Result = new(ReceiverPresence.Absent);
        await provider.ReadDeviceAsync();
        Assert.Equal(BatteryObservationState.Stale, provider.Current.ObservationState);
        Assert.Equal(56, provider.Current.Percentage);
        Assert.False(provider.CanQuery);
        discovery.Result = new(ReceiverPresence.Detected);
        await provider.ReadDeviceAsync();
        Assert.Equal(BatteryObservationState.Stale, provider.Current.ObservationState);
        Assert.Equal(observed, provider.Current.LastReported); Assert.Equal(1, transport.Calls);
        Assert.Contains("not current", HyperXPanelView.From(provider).BatteryText);
    }

    [Fact]
    public async Task SecondConcurrentManualQueryIsSkippedAndNoPollingIsCreated()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<HyperXQueryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new Transport { Handler = token => { started.SetResult(); return reply.Task.WaitAsync(token); } };
        await using var provider = new HyperXBatteryProvider(new Discovery(), new Processes(), transport);
        provider.ConfigureExperimentalQueries(true, true);
        var first = provider.QueryOnceAsync();
        await started.Task;
        await provider.QueryOnceAsync();
        Assert.Equal(1, transport.Calls); Assert.Equal(HyperXStage.Busy, provider.Diagnostic.Stage);
        reply.SetResult(Transport.Candidate);
        await first;
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData(HyperXQueryOutcome.Timeout)] [InlineData(HyperXQueryOutcome.Cancelled)]
    [InlineData(HyperXQueryOutcome.AccessDenied)] [InlineData(HyperXQueryOutcome.InvalidResponse)]
    [InlineData(HyperXQueryOutcome.Unavailable)] [InlineData(HyperXQueryOutcome.Failed)]
    public async Task FailureStopsFurtherAttemptsEvenIfOptInIsToggled(HyperXQueryOutcome outcome)
    {
        var transport = new Transport { Reply = new(outcome, Win32Error: 32, CompletionSafeCleanup: true) };
        await using var provider = new HyperXBatteryProvider(new Discovery(), new Processes(), transport);
        provider.ConfigureExperimentalQueries(true, true);
        await provider.QueryOnceAsync();
        Assert.Equal(32, provider.Diagnostic.Win32Error);
        Assert.Equal(BatteryObservationState.Unavailable, provider.Current.ObservationState);
        provider.ConfigureExperimentalQueries(false); provider.ConfigureExperimentalQueries(true, true);
        await provider.QueryOnceAsync();
        Assert.Equal(1, transport.Calls); Assert.False(provider.CanQuery);
        Assert.Equal(HyperXStage.Stopped, provider.Diagnostic.Stage);
    }

    [Theory]
    [InlineData(-1, true)] [InlineData(101, true)] [InlineData(56, false)]
    public async Task InvalidPercentageOrUnverifiedCleanupNeverPromotesCandidate(int percentage, bool cleanup)
    {
        var transport = new Transport { Reply = Transport.Candidate with { CandidatePercentage = percentage, CompletionSafeCleanup = cleanup } };
        await using var provider = new HyperXBatteryProvider(new Discovery(), new Processes(), transport);
        provider.ConfigureExperimentalQueries(true, true);
        await provider.QueryOnceAsync();
        Assert.Null(provider.Current.Percentage); Assert.Equal(BatteryObservationState.Unavailable, provider.Current.ObservationState);
        Assert.False(provider.CanQuery);
    }

    [Fact]
    public async Task CancellationAndDisposeWaitForOwnedTransportCleanupAndNeverRetry()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new Transport
        {
            Handler = async token => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return Transport.Candidate; },
            Cleanup = cleanup.Task
        };
        var provider = new HyperXBatteryProvider(new Discovery(), new Processes(), transport);
        provider.ConfigureExperimentalQueries(true, true);
        var query = provider.QueryOnceAsync(); await started.Task;
        var disposal = provider.DisposeAsync().AsTask();
        await query.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(disposal.IsCompleted);
        cleanup.SetResult(); await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, transport.Calls); Assert.Equal(1, transport.Disposals); Assert.False(provider.CanQuery);
        await provider.DisposeAsync(); Assert.Equal(1, transport.Disposals);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.ReadDeviceAsync());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.QueryOnceAsync());
    }

    [Fact]
    public async Task MetadataRefreshRetainsTheLastExchangeFailureDiagnostic()
    {
        var transport = new Transport { Reply = new(HyperXQueryOutcome.AccessDenied, Win32Error: 32, CompletionSafeCleanup: true) };
        await using var provider = new HyperXBatteryProvider(new Discovery(), new Processes(), transport);
        provider.ConfigureExperimentalQueries(true, true);
        await provider.QueryOnceAsync();
        await provider.ReadDeviceAsync();
        Assert.Equal(HyperXStage.Discovery, provider.Diagnostic.Stage);
        Assert.Equal(32, provider.LastExchangeDiagnostic!.Win32Error);
        Assert.Contains("Win32=32", HyperXPanelView.From(provider).Details);
        Assert.False(provider.CanQuery);
    }

    [Fact]
    public async Task ExceptionMessagesAreNotExposedInUiDiagnostics()
    {
        var transport = new Transport { Handler = _ => throw new IOException(@"C:\private-user\hid-instance") };
        await using var provider = new HyperXBatteryProvider(new Discovery(), new Processes(), transport);
        provider.ConfigureExperimentalQueries(true, true);
        await provider.QueryOnceAsync();
        Assert.DoesNotContain("private-user", HyperXPanelView.From(provider).Details);
        Assert.False(provider.Diagnostic.CompletionSafeCleanup);
        Assert.False(provider.CanQuery);
    }

    [Fact]
    public void SharedTargetRulesExcludeOtherCollectionsInterfacesAndDuplicateReceiverInstances()
    {
        var target = Path();
        Assert.Single(HyperXCollectionRules.TargetPaths([target, target.ToUpperInvariant(), Path("mi_03&col02"), Path("mi_02&col01")]));
        Assert.Equal(2, HyperXCollectionRules.TargetPaths([target, Path(instance: "other")]).Length);
        Assert.Empty(HyperXCollectionRules.TargetPaths([target.Replace("vid_03f0", "vid_046d"), target.Replace("pid_05b7", "pid_c547")]));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MetadataProbeInspectsOnlySingleTargetAndRetainsNumericErrors(bool error)
    {
        var backend = new Metadata { Failure = error ? new(InspectionStep.GetCapabilities, HidStatus: -1) : null };
        var result = HyperXCollectionRules.Inspect(backend);
        Assert.Equal(error ? HyperXMetadataPresence.InspectionFailed : HyperXMetadataPresence.Detected, result.Presence);
        Assert.Equal(1, backend.Reads);
        if (error) Assert.Equal(-1, result.Failure!.HidStatus);
        backend.Paths = [Path(), Path(instance: "other")];
        result = HyperXCollectionRules.Inspect(backend);
        Assert.Equal(HyperXMetadataPresence.Ambiguous, result.Presence); Assert.Equal(1, backend.Reads);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void MissingIncompleteOrIncompatibleMetadataNeverDetectsReceiver(int scenario)
    {
        var backend = new Metadata();
        if (scenario == 0) backend.Paths = [];
        if (scenario == 1) backend.EnumerationFailure = new(InspectionStep.InterfaceDetail, 5);
        if (scenario == 2) backend.Capabilities = backend.Capabilities with { InputReportBytes = 61 };
        var result = HyperXCollectionRules.Inspect(backend);
        Assert.Equal(scenario == 0 ? HyperXMetadataPresence.Absent : HyperXMetadataPresence.InspectionFailed, result.Presence);
        Assert.Equal(scenario == 2 ? 1 : 0, backend.Reads);
        if (scenario == 1) Assert.Equal(5, result.Failure!.Win32Error);
    }

    private static string Path(string hardware = "mi_03&col01", string instance = "synthetic") =>
        $@"\\?\hid#vid_03f0&pid_05b7&{hardware}#{instance}#{{4d1e55b2-f16f-11cf-88cb-001111000030}}";
    private sealed class Metadata : IHidMetadataBackend
    {
        public string[] Paths = [Path()]; public int Reads; public InspectionFailure? Failure, EnumerationFailure;
        public HidCapabilities Capabilities = new(0xFF13, 1, 62, 62, 0, 0, new(0, 0, 0), new(0, 0, 0), new(0, 0, 0));
        public InterfaceEnumeration EnumerateInterfaces() => new(Paths, EnumerationFailure is null ? [] : [EnumerationFailure]);
        public CapabilityRead ReadCapabilities(string path)
        {
            Reads++;
            return new(Capabilities, Failure);
        }
    }
    private sealed class Discovery : IHyperXReceiverDiscovery
    {
        public int Calls; public HyperXDiscoveryResult Result = new(ReceiverPresence.Detected);
        public Task<HyperXDiscoveryResult> DiscoverAsync(CancellationToken token) { Calls++; token.ThrowIfCancellationRequested(); return Task.FromResult(Result); }
    }
    private sealed class Processes : INgenuityPresenceProbe
    {
        public int Calls; public NgenuityPresence Presence = NgenuityPresence.Running;
        public Task<NgenuityPresence> InspectAsync(CancellationToken token) { Calls++; token.ThrowIfCancellationRequested(); return Task.FromResult(Presence); }
    }
    private sealed class Transport : IHyperXManualQueryTransport
    {
        public static readonly HyperXQueryResult Candidate = new(HyperXQueryOutcome.CandidateObserved, 56, true, true, CompletionSafeCleanup: true);
        public bool Verified = true; public bool HasVerifiedLongRunningLifetime => Verified;
        public int Calls, Disposals; public HyperXQueryResult Reply = Candidate;
        public Func<CancellationToken, Task<HyperXQueryResult>>? Handler;
        public Task Cleanup = Task.CompletedTask;
        public Task<HyperXQueryResult> QueryOnceAsync(CancellationToken token) { Calls++; return Handler?.Invoke(token) ?? Task.FromResult(Reply); }
        public async ValueTask DisposeAsync() { Disposals++; await Cleanup; }
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
