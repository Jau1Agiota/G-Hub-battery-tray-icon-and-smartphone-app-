namespace PeripheralBatteryCompanion.Core;

public sealed class BatteryMonitoringService : IAsyncDisposable
{
    private readonly IBatteryProvider _provider;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private Task? _loop;
    private Task? _shutdown;
    private readonly object _lifetime = new();
    private BatteryDevice? _lastKnown;
    private DateTimeOffset? _lastAccess;
    public event Action<BatteryMonitoringState>? Updated;

    public BatteryMonitoringService(IBatteryProvider provider, TimeSpan? interval = null)
    {
        _provider = provider;
        _interval = interval ?? TimeSpan.FromSeconds(30);
        if (_interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
    }

    public void Start()
    {
        lock (_lifetime)
        {
            if (_shutdown is not null) throw new ObjectDisposedException(nameof(BatteryMonitoringService));
            _loop ??= Task.Run(PollAsync);
        }
    }

    private async Task PollAsync()
    {
        try
        {
            await RefreshAsync();
            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(_stop.Token)) await RefreshAsync();
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        var token = linked.Token;
        if (!await _readGate.WaitAsync(0, token)) return;
        try
        {
            token.ThrowIfCancellationRequested();
            var result = await _provider.ReadAsync(token);
            // CancelAsync marks the source immediately but linked-token callbacks may
            // still be queued. Never publish a completed read after requested shutdown.
            _stop.Token.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            token.ThrowIfCancellationRequested();
            if (result.Keyboard.Availability == BatteryAvailability.Available) _lastKnown = result.Keyboard;
            if (result.DatabaseAccessedAt.HasValue) _lastAccess = result.DatabaseAccessedAt;
            Updated?.Invoke(new(result, _lastKnown, _lastAccess));
        }
        finally { _readGate.Release(); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifetime) return new(_shutdown ??= StopAsync());
    }

    private async Task StopAsync()
    {
        await _stop.CancelAsync();
        if (_loop is not null) await _loop;
        // Also wait for a manually initiated refresh before permitting UI teardown.
        await _readGate.WaitAsync();
        _readGate.Release();
        // Leave synchronization primitives alive so late refresh calls cancel safely.
    }
}
