using System.ComponentModel;
using PeripheralBatteryCompanion.Core;
using PeripheralBatteryCompanion.HyperXHelperProtocol;
using PeripheralBatteryCompanion.HyperXHelper;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class HyperXHelperTests
{
    [Fact]
    public async Task MissingAcknowledgementOrComponentStartsNothingAndPreflightNeverQueries()
    {
        var factory = new Factory(); await using var transport = new HyperXHelperTransport(factory);
        Assert.False(transport.HasVerifiedLongRunningLifetime);
        Assert.False(await transport.PrepareAsync(false)); Assert.Empty(factory.Children);
        Assert.Equal(HyperXQueryOutcome.Unavailable, (await transport.QueryOnceAsync(default)).Outcome);
        Assert.True(await transport.PrepareAsync(true));
        Assert.Equal(new[] { HelperMode.Simulate, HelperMode.SimulateHold }, factory.Children.Select(child => child.Mode));
        Assert.All(factory.Children, child => Assert.True(child.Disposed));
        Assert.True(factory.Children[1].Killed);
        Assert.True(transport.HasVerifiedLongRunningLifetime);
        var absent = new Factory { IsAvailable = false };
        await using var unavailable = new HyperXHelperTransport(absent);
        Assert.False(await unavailable.PrepareAsync(true)); Assert.Empty(absent.Children);
    }

    [Fact]
    public async Task UncontainedChildIsNeverArmedAndCannotEnableQuery()
    {
        var factory = new Factory { Configure = child => child.Contained = false };
        await using var transport = new HyperXHelperTransport(factory);
        Assert.False(await transport.PrepareAsync(true));
        Assert.All(factory.Children, child => Assert.DoesNotContain(child.Commands, command => command.Action == HelperAction.Start));
        Assert.False(transport.HasVerifiedLongRunningLifetime);
    }

    [Fact]
    public async Task OneCandidateRequiresValidatedReplyAndObservedNormalExit()
    {
        var factory = new Factory(); await using var transport = new HyperXHelperTransport(factory);
        Assert.True(await transport.PrepareAsync(true));
        var result = await transport.QueryOnceAsync(default);
        Assert.Equal(HyperXQueryOutcome.CandidateObserved, result.Outcome); Assert.Equal(56, result.CandidatePercentage);
        Assert.True(result.RequestAttempted); Assert.True(result.OutputCompleted); Assert.True(result.CompletionSafeCleanup);
        var child = Assert.Single(factory.Children, child => child.Mode == HelperMode.NativeOneShot);
        Assert.Single(child.Commands, command => command.Action == HelperAction.Start);
        Assert.True(child.Disposed); Assert.False(child.Killed);
        Assert.Contains("ownership=ambiguous", transport.Diagnostics);
        Assert.Contains("normal exit observed", transport.Diagnostics);
        Assert.DoesNotContain("synthetic-instance", transport.Diagnostics);
    }

    [Theory]
    [InlineData(0)] [InlineData(5)] [InlineData(61)] [InlineData(63)]
    public async Task InvalidLengthsNeverBecomeCandidates(int size)
    {
        var factory = new Factory { ConfigureNative = child => child.Reply = child.Reply with { Response = new byte[size] } };
        await using var transport = new HyperXHelperTransport(factory);
        await transport.PrepareAsync(true);
        var result = await transport.QueryOnceAsync(default);
        Assert.Null(result.CandidatePercentage); Assert.Equal(HyperXQueryOutcome.Failed, result.Outcome);
        Assert.False(transport.HasVerifiedLongRunningLifetime);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public async Task WrongHeaderOutOfRangeNonceVersionModeOrOpenGateFailClosed(int defect)
    {
        var factory = new Factory { ConfigureNative = child =>
        {
            child.Reply = defect switch
            {
                0 => child.Reply with { Response = new byte[62] },
                1 => child.Reply with { Response = Reply(101) },
                2 => child.Reply with { Nonce = Guid.NewGuid().ToString("N") },
                3 => child.Reply with { Version = 999 },
                4 => child.Reply with { Mode = HelperMode.Simulate },
                5 => child.Reply with { SharedOpenSucceeded = false },
                _ => child.Reply with { Win32Error = 5 }
            };
        } };
        await using var transport = new HyperXHelperTransport(factory); await transport.PrepareAsync(true);
        Assert.Null((await transport.QueryOnceAsync(default)).CandidatePercentage);
        Assert.False(transport.HasVerifiedLongRunningLifetime);
    }

    [Theory]
    [InlineData(HelperStatus.NoReceiver)] [InlineData(HelperStatus.AmbiguousReceiver)]
    [InlineData(HelperStatus.CapabilitiesMismatch)] [InlineData(HelperStatus.BufferedInputBeforeRequest)]
    [InlineData(HelperStatus.InvalidResponse)] [InlineData(HelperStatus.Timeout)] [InlineData(HelperStatus.Refused)]
    public async Task RefusalsAndTimeoutStopFurtherAttemptsWithoutRetries(HelperStatus status)
    {
        var factory = new Factory { ConfigureNative = child => child.Reply = child.Reply with
            { Status = status, RequestAttempted = false, OutputCompleted = false, Response = null } };
        await using var transport = new HyperXHelperTransport(factory); await transport.PrepareAsync(true);
        var result = await transport.QueryOnceAsync(default);
        Assert.NotEqual(HyperXQueryOutcome.CandidateObserved, result.Outcome);
        Assert.False(await transport.PrepareAsync(true));
        await transport.QueryOnceAsync(default);
        Assert.Single(factory.Children, child => child.Mode == HelperMode.NativeOneShot);
    }

    [Theory]
    [InlineData(5)] [InlineData(32)]
    public async Task SharingAndAccessErrorsAreNumericAndDoNotFallback(int error)
    {
        var factory = new Factory { ConfigureNative = child => child.Reply = child.Reply with
            { Status = HelperStatus.NativeFailure, Stage = HelperStage.OpenReadHandle, Response = null,
                RequestAttempted = false, OutputCompleted = false, SharedOpenSucceeded = false, Win32Error = error } };
        await using var transport = new HyperXHelperTransport(factory); await transport.PrepareAsync(true);
        var result = await transport.QueryOnceAsync(default);
        Assert.Equal(error, result.Win32Error); Assert.Equal(HyperXQueryOutcome.AccessDenied, result.Outcome);
        Assert.Contains($"Win32={error}", transport.Diagnostics);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ForcedOrUnconfirmedExitNeverPromotesAValidReply(bool neverExits)
    {
        var factory = new Factory { ConfigureNative = child => { child.MustKill = true; child.NeverExits = neverExits; } };
        await using var transport = new HyperXHelperTransport(factory); await transport.PrepareAsync(true);
        var result = await transport.QueryOnceAsync(default);
        Assert.Null(result.CandidatePercentage); Assert.False(transport.HasVerifiedLongRunningLifetime);
        var child = factory.Children.Last(); Assert.True(child.Killed);
        Assert.Equal(!neverExits, result.CompletionSafeCleanup);
        Assert.Equal(neverExits, child.Retained);
        Assert.Contains(neverExits ? "exit unconfirmed" : "forced exit observed", transport.Diagnostics);
    }

    [Fact]
    public async Task CancelWaitsForBoundedOwnedCleanupAndShutdownDoesNotStartAnotherChild()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new Factory { ConfigureNative = child => child.Hold = started };
        var transport = new HyperXHelperTransport(factory); await transport.PrepareAsync(true);
        var query = transport.QueryOnceAsync(default); await started.Task;
        await transport.DisposeAsync();
        var result = await query;
        Assert.Equal(HyperXQueryOutcome.Cancelled, result.Outcome);
        Assert.True(result.CompletionSafeCleanup); Assert.True(factory.Children.Last().Disposed);
        Assert.Contains(factory.Children.Last().Commands, command => command.Action == HelperAction.Cancel);
        Assert.False(transport.HasVerifiedLongRunningLifetime);
        Assert.False(await transport.PrepareAsync(true));
    }

    [Fact]
    public async Task GlobalSingleFlightAcrossTransportInstancesDoesNotQueueRequests()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstFactory = new Factory { ConfigureNative = child => child.Hold = started };
        var secondFactory = new Factory();
        await using var first = new HyperXHelperTransport(firstFactory);
        await using var second = new HyperXHelperTransport(secondFactory);
        await first.PrepareAsync(true); await second.PrepareAsync(true);
        using var cancel = new CancellationTokenSource();
        var query = first.QueryOnceAsync(cancel.Token); await started.Task;
        Assert.Equal(HyperXQueryOutcome.Unavailable, (await second.QueryOnceAsync(default)).Outcome);
        Assert.DoesNotContain(secondFactory.Children, child => child.Mode == HelperMode.NativeOneShot);
        cancel.Cancel(); await query;
    }

    [Fact]
    public async Task StartupErrorsExposeNumericCodeWithoutRawExceptionPath()
    {
        var factory = new Factory { ConfigureNative = _ => throw new Win32Exception(5, @"C:\private\synthetic-instance") };
        await using var transport = new HyperXHelperTransport(factory); await transport.PrepareAsync(true);
        Assert.Equal(HyperXQueryOutcome.Failed, (await transport.QueryOnceAsync(default)).Outcome);
        Assert.Contains("supervisor Win32=5", transport.Diagnostics);
        Assert.DoesNotContain("private", transport.Diagnostics); Assert.DoesNotContain("synthetic-instance", transport.Diagnostics);
    }

    [Fact]
    public async Task HelperServerRequiresAnExplicitMatchingRiskAcknowledgedArmBeforeExecution()
    {
        var nonce = Guid.NewGuid().ToString("N"); var calls = 0;
        var input = new StringReader(HelperProtocol.Encode(new HelperCommand(1, nonce, HelperAction.Start)) + "\n");
        using var output = new StringWriter();
        var code = await HelperServer.RunAsync(input, output, nonce, HelperMode.NativeOneShot, "1.0.0-test",
            _ => { calls++; return Task.FromResult(new HelperReply(1, nonce, HelperMode.NativeOneShot, HelperStatus.Refused)); });
        Assert.Equal(2, code); Assert.Equal(0, calls);
        Assert.Single(output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(2, await PeripheralBatteryCompanion.HyperXHelper.Program.Main([]));
    }

    [Fact]
    public async Task HelperSimulationAndStrictBoundedFramesNeverNeedNativeExecution()
    {
        var nonce = Guid.NewGuid().ToString("N"); var calls = 0;
        using var output = new StringWriter();
        var code = await HelperServer.RunAsync(new StringReader(HelperProtocol.Encode(new HelperCommand(1, nonce, HelperAction.Start)) + "\n"),
            output, nonce, HelperMode.Simulate, "1.0.0-test", _ =>
            { calls++; return Task.FromResult(new HelperReply(1, nonce, HelperMode.Simulate, HelperStatus.SimulationCompleted)); });
        Assert.Equal(0, code); Assert.Equal(1, calls);
        Assert.Equal(2, output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        await Assert.ThrowsAsync<InvalidDataException>(() => HelperProtocol.ReadFrameAsync(new StringReader(new string('x', 4097) + "\n"), default));
        await Assert.ThrowsAsync<InvalidDataException>(() => HelperProtocol.ReadFrameAsync(new StringReader("truncated"), default));
        Assert.ThrowsAny<Exception>(() => HelperProtocol.Decode<HelperReady>("{\"Unexpected\":true}"));
        Assert.False(HelperProtocol.ValidReady(new(1, nonce, HelperMode.Simulate, @"C:\private\app"), nonce, HelperMode.Simulate));
    }

    [Theory]
    [InlineData(NgenuityPresence.Unknown)] [InlineData(NgenuityPresence.NotObserved)]
    public async Task ProductionProcessGuardRefusesBeforeStartingANativeHelper(NgenuityPresence presence)
    {
        var factory = new Factory(); await using var transport = new HyperXHelperTransport(factory);
        await transport.PrepareAsync(true);
        await using var provider = new HyperXBatteryProvider(new Discovery(), new Processes(presence), transport);
        provider.ConfigureExperimentalQueries(true, true);
        await provider.QueryOnceAsync();
        Assert.DoesNotContain(factory.Children, child => child.Mode == HelperMode.NativeOneShot);
        Assert.Equal(HyperXStage.ProcessGuard, provider.Diagnostic.Stage);
    }

    [Fact]
    public async Task ProductionIntegrationRemainsOffUntilAcknowledgementAndManualClickAndNeverEmitsFresh()
    {
        var factory = new Factory(); await using var transport = new HyperXHelperTransport(factory);
        await using var provider = new HyperXBatteryProvider(new Discovery(), new Processes(NgenuityPresence.Running), transport);
        await provider.ReadDeviceAsync(); await provider.QueryOnceAsync();
        Assert.Empty(factory.Children); Assert.Null(provider.Current.Percentage);
        Assert.False(provider.ConfigureExperimentalQueries(true));
        provider.ConfigureExperimentalQueries(true, true); await transport.PrepareAsync(true);
        await provider.ReadDeviceAsync();
        Assert.DoesNotContain(factory.Children, child => child.Mode == HelperMode.NativeOneShot);
        Assert.True(provider.CanQuery);
        await provider.QueryOnceAsync();
        Assert.Equal(BatteryObservationState.CandidateAmbiguous, provider.Current.ObservationState);
        Assert.Equal(56, provider.Current.Percentage); Assert.NotNull(provider.Current.LastReported);
        Assert.Equal("Unknown", provider.Current.Connectivity);
        Assert.Single(factory.Children, child => child.Mode == HelperMode.NativeOneShot);
        await provider.ReadDeviceAsync(); // Refresh is still metadata-only after a successful query.
        Assert.Single(factory.Children, child => child.Mode == HelperMode.NativeOneShot);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task NonzeroExitOrEarlyEofCannotPromoteACandidate(bool nonzeroExit)
    {
        var factory = new Factory { ConfigureNative = child =>
        {
            if (nonzeroExit) child.ExitOverride = 1;
            else child.EarlyEof = true;
        } };
        await using var transport = new HyperXHelperTransport(factory); await transport.PrepareAsync(true);
        Assert.Null((await transport.QueryOnceAsync(default)).CandidatePercentage);
        Assert.False(transport.HasVerifiedLongRunningLifetime);
    }

    [Fact]
    public async Task ReceiverRemovedBetweenParentMetadataAndHelperDiscoveryBecomesAbsentWithoutRetry()
    {
        var factory = new Factory { ConfigureNative = child => child.Reply = child.Reply with
            { Status = HelperStatus.NoReceiver, Response = null, RequestAttempted = false, OutputCompleted = false,
                CapabilitiesValid = false, SharedOpenSucceeded = false } };
        await using var transport = new HyperXHelperTransport(factory); await transport.PrepareAsync(true);
        await using var provider = new HyperXBatteryProvider(new Discovery(), new Processes(NgenuityPresence.Running), transport);
        provider.ConfigureExperimentalQueries(true, true);
        await provider.ReadDeviceAsync(); Assert.Equal(ReceiverPresence.Detected, provider.Current.Receiver);
        await provider.QueryOnceAsync();
        Assert.Equal(ReceiverPresence.Absent, provider.Current.Receiver);
        Assert.Equal(BatteryObservationState.Unavailable, provider.Current.ObservationState);
        Assert.False(provider.CanQuery); Assert.Null(provider.Current.Percentage);
        await provider.ReadDeviceAsync(); // Re-enumeration must never restart a query.
        Assert.Single(factory.Children, child => child.Mode == HelperMode.NativeOneShot);
    }

    [Theory]
    [InlineData(HelperStatus.Timeout)] [InlineData(HelperStatus.InvalidResponse)]
    [InlineData(HelperStatus.NativeFailure)]
    public async Task MonitoringRecoveryRequiresNormalObservedExitAndNeverRetriesInsideTransport(HelperStatus status)
    {
        var factory = new Factory { ConfigureNative = child => child.Reply = child.Reply with
            { Status = status, Response = null } };
        await using var transport = new HyperXHelperTransport(factory, allowRecoverableFailures: true);
        await transport.PrepareAsync(true);
        var result = await transport.QueryOnceAsync(default);
        Assert.NotEqual(HyperXQueryOutcome.CandidateObserved, result.Outcome);
        Assert.True(result.CompletionSafeCleanup); Assert.True(transport.HasVerifiedLongRunningLifetime);
        Assert.Single(factory.Children, child => child.Mode == HelperMode.NativeOneShot);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MonitoringRecoveryNeverClearsCrashOrUnconfirmedExitLatch(bool unconfirmed)
    {
        var factory = new Factory { ConfigureNative = child =>
        {
            if (unconfirmed) child.NeverExits = true;
            else child.ExitOverride = 1;
        } };
        await using var transport = new HyperXHelperTransport(factory, allowRecoverableFailures: true);
        await transport.PrepareAsync(true); await transport.QueryOnceAsync(default);
        Assert.False(transport.HasVerifiedLongRunningLifetime);
        Assert.False(await transport.PrepareAsync(true));
        await transport.QueryOnceAsync(default);
        Assert.Single(factory.Children, child => child.Mode == HelperMode.NativeOneShot);
    }

    private sealed class Discovery : IHyperXReceiverDiscovery
    {
        public Task<HyperXDiscoveryResult> DiscoverAsync(CancellationToken token) => Task.FromResult(new HyperXDiscoveryResult(ReceiverPresence.Detected));
    }
    private sealed class Processes(NgenuityPresence presence) : INgenuityPresenceProbe
    {
        public Task<NgenuityPresence> InspectAsync(CancellationToken token) => Task.FromResult(presence);
    }

    private static byte[] Reply(int percentage = 56) { var bytes = HelperProtocol.Request(); bytes[2] = 0x0E; bytes[3] = 0xE3; bytes[4] = (byte)percentage; return bytes; }
    private sealed class Factory : IHyperXHelperProcessFactory
    {
        public bool IsAvailable { get; set; } = true;
        public List<Child> Children = [];
        public Action<Child>? Configure, ConfigureNative;
        public Task<IHyperXHelperProcess> StartAsync(HelperMode mode, string nonce, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var child = new Child(mode, nonce); Configure?.Invoke(child);
            if (mode == HelperMode.NativeOneShot) ConfigureNative?.Invoke(child);
            Children.Add(child); return Task.FromResult<IHyperXHelperProcess>(child);
        }
    }
    private sealed class Child(HelperMode mode, string nonce) : IHyperXHelperProcess
    {
        public HelperMode Mode = mode;
        public bool Contained { get; set; } = true;
        public int? ExitOverride;
        public bool EarlyEof;
        public int? ExitCode => ExitOverride ?? (Killed ? 224 : 0);
        public bool Disposed, Killed, Retained, MustKill, NeverExits;
        private int _reads;
        public TaskCompletionSource? Hold;
        public List<HelperCommand> Commands = [];
        public HelperReply Reply = mode == HelperMode.NativeOneShot
            ? new(1, nonce, mode, HelperStatus.CandidateObservedAmbiguous, HelperStage.Exchange, HelperProcessObservation.Running,
                true, true, true, true, HyperXHelperTests.Reply(), NativeCleanupCompleted: true)
            : new(1, nonce, mode, HelperStatus.SimulationCompleted, NativeCleanupCompleted: true);
        public async Task<string?> ReadFrameAsync(CancellationToken token)
        {
            if (++_reads == 1) return HelperProtocol.Encode(new HelperReady(1, nonce, Mode, "1.0.0-test"));
            if (EarlyEof) return null;
            if (Hold is not null) { Hold.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
            return HelperProtocol.Encode(Reply);
        }
        public Task SendAsync(HelperCommand command, CancellationToken token) { Commands.Add(command); return Task.CompletedTask; }
        public void CloseInput() { }
        public void RequestTermination() => Killed = true;
        public Task<bool> WaitForExitAsync(TimeSpan timeout) => Task.FromResult(!NeverExits && (!(MustKill || Mode == HelperMode.SimulateHold) || Killed));
        public void RetainUntilExit() => Retained = true;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
