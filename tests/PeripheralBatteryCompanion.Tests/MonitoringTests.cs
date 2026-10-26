using PeripheralBatteryCompanion.Core;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class MonitoringTests
{
    private static readonly BatteryDevice Keyboard = new("G915 Keyboard", 45,
        DateTimeOffset.Parse("2026-10-26T13:41:42Z"), BatteryAvailability.Available, "Last reported");

    [Fact]
    public async Task UnchangedValuesStillReportSuccessfulAccess()
    {
        var provider = new FakeProvider(_ => Task.FromResult(new BatteryReadResult(Keyboard, DateTimeOffset.UtcNow)));
        await using var service = new BatteryMonitoringService(provider);
        var states = new List<BatteryMonitoringState>();
        service.Updated += states.Add;
        await service.RefreshAsync();
        await service.RefreshAsync();
        Assert.Equal(2, states.Count);
        Assert.Equal(states[0].Current.Keyboard, states[1].Current.Keyboard);
        Assert.NotNull(states[1].LastSuccessfulDatabaseAccess);
    }

    [Fact]
    public async Task FailedReadRetainsExplicitlySeparateLastKnownValue()
    {
        var count = 0;
        var provider = new FakeProvider(_ => Task.FromResult(++count == 1
            ? new BatteryReadResult(Keyboard, DateTimeOffset.UtcNow)
            : new BatteryReadResult(Keyboard with { Percentage = null, Availability = BatteryAvailability.Unavailable }, null)));
        await using var service = new BatteryMonitoringService(provider);
        BatteryMonitoringState? state = null;
        service.Updated += value => state = value;
        await service.RefreshAsync();
        var accessed = state!.LastSuccessfulDatabaseAccess;
        await service.RefreshAsync();
        Assert.Null(state!.Current.Keyboard.Percentage);
        Assert.Equal(45, state.LastKnownKeyboard!.Percentage);
        Assert.Equal(accessed, state.LastSuccessfulDatabaseAccess);
    }

    [Fact]
    public async Task OverlappingManualReadsAreSkippedAndShutdownWaitsForRead()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var provider = new FakeProvider(async _ =>
        {
            Interlocked.Increment(ref count);
            entered.SetResult();
            // Model synchronous SQLite work which cannot cancel mid-read.
            await release.Task;
            return new(Keyboard, DateTimeOffset.UtcNow);
        });
        var service = new BatteryMonitoringService(provider);
        var updates = 0;
        service.Updated += _ => updates++;
        var reading = service.RefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.RefreshAsync();
        Assert.Equal(1, count);
        var shutdown = service.DisposeAsync().AsTask();
        Assert.False(shutdown.IsCompleted);
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, updates);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RefreshAsync());
        await service.DisposeAsync();
    }

    [Fact]
    public async Task PollingStartsImmediatelyThenRepeatsAndCancels()
    {
        var twice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var provider = new FakeProvider(_ => Task.FromResult(new BatteryReadResult(Keyboard, DateTimeOffset.UtcNow)));
        var service = new BatteryMonitoringService(provider, TimeSpan.FromMilliseconds(20));
        service.Updated += _ => { if (Interlocked.Increment(ref count) >= 2) twice.TrySetResult(); };
        service.Start();
        service.Start();
        await twice.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.DisposeAsync();
        var stoppedCount = count;
        await Task.Delay(70);
        Assert.Equal(stoppedCount, count);
        Assert.Throws<ObjectDisposedException>(service.Start);
    }

    [Fact]
    public async Task CancellationReachesProviderAndDisposalIsIdempotent()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeProvider(async token =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new(Keyboard, null);
        });
        var service = new BatteryMonitoringService(provider);
        service.Start();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await service.DisposeAsync();
    }

    [Fact]
    public async Task CallerCancellationDoesNotStopFutureRefreshes()
    {
        var provider = new FakeProvider(_ => Task.FromResult(new BatteryReadResult(Keyboard, DateTimeOffset.UtcNow)));
        await using var service = new BatteryMonitoringService(provider);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RefreshAsync(stop.Token));
        await service.RefreshAsync();
    }

    private sealed class FakeProvider(Func<CancellationToken, Task<BatteryReadResult>> read) : IBatteryProvider
    {
        public Task<BatteryReadResult> ReadAsync(CancellationToken cancellationToken) => read(cancellationToken);
    }
}
