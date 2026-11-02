using PeripheralBatteryCompanion.Core;
using PeripheralBatteryCompanion.Core.Presentation;
using PeripheralBatteryCompanion.Core.SnapshotService;

namespace PeripheralBatteryCompanion.Windows;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly PublishedSnapshotStore _publishedSnapshots = new(Guid.NewGuid());
    private readonly CachedSnapshotPublisher _snapshotPublisher;
    private LanPreviewServer? _lanPreview;
    private LanPreviewSettingsForm? _lanSettings;
    private readonly Control _dispatcher = new();
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _refresh = new("Refresh");
    private readonly BatteryPopupForm _popup = new(); // One instance, retained until application exit.
    private readonly TrayPanelState _interaction = new();
    private readonly BatteryMonitoringService _monitor = new(new GHubWebSocketBatteryProvider(), GHubWebSocketBatteryProvider.PollInterval);
    private readonly HyperXHelperTransport _hyperXTransport = new(new HyperXHelperProcessFactory(), allowRecoverableFailures: true);
    private readonly HyperXBatteryProvider _hyperX;
    private readonly HyperXAutomaticMonitor _hyperXAutomatic;
    private readonly HyperXDeviceNotifications _hyperXDevices;
    private readonly HyperXPreferencesStore _hyperXSettings = new();
    private readonly HyperXStartupState _hyperXStartupState;
    private readonly HyperXStartupCoordinator _hyperXStartup;
    private Task _hyperXScheduleTask = Task.CompletedTask;
    private CancellationTokenSource? _hyperXQueryStop;
    private Task _hyperXQueryTask = Task.CompletedTask;
    private readonly CancellationTokenSource _hyperXStop = new();
    private Task _hyperXDiscoveryTask = Task.CompletedTask;
    private Icon _icon;
    private int? _iconPercentage;
    private BatteryMonitoringState? _lastKeyboardState;
    private string _lastTooltip = "";
    private bool _refreshing;
    private bool Closing => _interaction.IsExitRequested;

    public TrayApplicationContext()
    {
        _snapshotPublisher = new(_publishedSnapshots);
        _hyperXStartupState = new(_hyperXSettings);
        var durable = new HyperXDurableTransport(_hyperXTransport, _hyperXStartupState);
        _hyperX = new(new HyperXReceiverDiscovery(), new NgenuityPresenceProbe(), durable, allowWithoutNgenuity: true);
        _hyperXAutomatic = new(_hyperX);
        _hyperXStartup = new(_hyperX, _hyperXAutomatic, token => _hyperXTransport.PrepareAsync(true, token), _hyperXStartupState);
        _dispatcher.CreateControl();
        _hyperXDevices = new(removed =>
        {
            if (Closing) return;
            if (removed) _hyperXAutomatic.ReceiverRemoved();
            DisplayHyperX(); // Arrival never queries directly; the coordinator handles first-read readiness or normal reconnect cadence.
        });
        _hyperXAutomatic.Changed += () =>
        {
            if (!Closing) _dispatcher.BeginInvoke((Action)(() =>
            {
                if (!Closing) { DisplayHyperX(); _popup.SetHyperXQuerying(_hyperXAutomatic.Status.Busy || _hyperXQueryStop is not null); }
            }));
        };
        _icon = BatteryIcon.Create(null);
        _menu.Items.Add("Open Battery Panel", null, (_, _) =>
            _dispatcher.BeginInvoke((Action)(() =>
            {
                _interaction.OpenPanel();
                ApplyPanelVisibility();
            })));
        _menu.Items.Add(_refresh);
        _menu.Items.Add("Advanced settings", null, (_, _) => _dispatcher.BeginInvoke((Action)(() =>
        {
            if (Closing) return;
            _interaction.OpenPanel(); ApplyPanelVisibility(); _popup.SetHyperXAdvancedVisible(true);
        })));
        _menu.Items.Add("LAN preview…", null, (_, _) =>
        {
            if (Closing) return;
            if (_lanSettings is { IsDisposed: false }) { _lanSettings.Activate(); return; }
            _lanSettings = new(_lanPreview, StartLanPreview, StopLanPreviewAsync);
            _lanSettings.Show();
        });
        _menu.Items.Add("Exit Application", null, async (_, _) => await CloseAsync());
        _menu.Opening += (_, e) =>
        {
            if (Closing) { e.Cancel = true; return; }
            _interaction.OpenContextMenu();
            ApplyPanelVisibility();
        };
        _tray = new NotifyIcon
        {
            Icon = _icon, Visible = true, Text = "G915: Unavailable", ContextMenuStrip = _menu
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) TogglePanel(); };
        // NotifyIcon suppresses the second MouseClick when Windows classifies it as a double-click.
        _tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) TogglePanel(); };
        _popup.DismissRequested += (_, _) =>
        {
            _interaction.DismissPanel();
            ApplyPanelVisibility();
        };
        _popup.RefreshRequested += async (_, _) => await RefreshAsync();
        _popup.HyperXQueryRequested += (_, _) =>
        {
            if (!Closing && _hyperX.CanQuery && !_hyperXAutomatic.Status.Suspended && _hyperXQueryTask.IsCompleted) _hyperXQueryTask = RunHyperXOperationAsync();
        };
        _popup.HyperXCancelRequested += (_, _) => { _hyperXStartup.Cancel(); _hyperXQueryStop?.Cancel(); };
        _popup.HyperXMonitoringRequested += ConfigureAutomaticHyperX;
        _popup.HyperXResumeRequested += (_, _) => { _hyperXStartup.Resume(); DisplayHyperX(); };
        DisplayHyperX();
        _refresh.Click += async (_, _) => await RefreshAsync();
        _monitor.Updated += OnUpdated;
        _monitor.Start();
        _hyperXScheduleTask = MonitorHyperXAsync();
        // Explicit local session-only opt-in, also usable without hunting the tray overflow.
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (arguments.Length == 3 && arguments[0] == "--lan-preview" &&
            System.Net.IPAddress.TryParse(arguments[1], out var selectedAddress) &&
            int.TryParse(arguments[2], out var selectedPort) && selectedPort is >= 1024 and <= 65535)
        {
            var result = StartLanPreview(selectedAddress, selectedPort);
            _lanSettings = new(_lanPreview, StartLanPreview, StopLanPreviewAsync);
            _lanSettings.Show();
            if (_lanPreview is null) MessageBox.Show(_lanSettings, result, "LAN preview");
        }
    }

    private void TogglePanel()
    {
        _interaction.TogglePanel();
        ApplyPanelVisibility();
    }

    private void ApplyPanelVisibility()
    {
        if (!Closing && _interaction.IsPanelVisible) _popup.ShowNearTray(Cursor.Position);
        else _popup.Hide();
    }

    private async Task RefreshAsync()
    {
        if (Closing || _refreshing) return;
        _refreshing = true;
        _refresh.Enabled = false;
        _popup.SetRefreshing(true);
        try { await Task.WhenAll(_monitor.RefreshAsync(), RefreshHyperXAsync()); }
        catch (OperationCanceledException) { }
        finally
        {
            _refreshing = false;
            if (!Closing)
            {
                _refresh.Enabled = true;
                _popup.SetRefreshing(false);
            }
        }
    }

    private async Task RunHyperXOperationAsync()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_hyperXStop.Token);
        _hyperXQueryStop = cancel;
        _popup.SetHyperXQuerying(true);
        try
        {
            await _hyperXStartup.QueryManuallyAsync(cancel.Token);
        }
        catch (OperationCanceledException) { }
        catch { _hyperXStartup.Cancel(); }
        finally
        {
            _hyperXQueryStop = null;
            if (!Closing)
            {
                DisplayHyperX();
                _popup.SetHyperXQuerying(false);
            }
        }
    }

    private void DisplayHyperX()
    {
        if (Closing) return;
        _snapshotPublisher.OnHyperXCacheChanged(_hyperX.Current);
        _popup.DisplayHyperX(HyperXPanelView.From(_hyperX));
        _popup.DisplayHyperXMonitoring(_hyperXAutomatic.Status);
        _popup.DisplayHyperXCompact(HyperXCompactView.From(_hyperX, _hyperXAutomatic.Status));
        _popup.DisplayStartupState(_hyperXStartup.Phase, _hyperXStartup.Preferences);
        UpdateTrayTooltip();
    }

    private async void ConfigureAutomaticHyperX(bool enabled, int interval)
    {
        if (Closing) return;
        try
        {
            if (!await _hyperXStartup.ConfigureAsync(enabled, interval, _hyperXStop.Token) && !Closing)
                MessageBox.Show(_popup.DialogOwner, "Monitoring settings could not be saved; querying is stopped safely.",
                    "HyperX preferences", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException) { }
        if (!Closing) DisplayHyperX();
    }

    private async Task MonitorHyperXAsync()
    {
        try
        {
            _hyperXQueryTask = Task.Run(() => _hyperXStartup.StartAsync(_hyperXStop.Token));
            await _hyperXQueryTask;
            if (!Closing) DisplayHyperX();
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5)); // Metadata/state wakeup, not a 5-second battery poll.
            while (await timer.WaitForNextTickAsync(_hyperXStop.Token))
            {
                if (!_hyperXQueryTask.IsCompleted) continue; // Preflight/manual share the same owned work.
                _hyperXQueryTask = Task.Run(() => _hyperXStartup.TickAsync(_hyperXStop.Token));
                await _hyperXQueryTask;
                if (!Closing) { DisplayHyperX(); _popup.SetHyperXQuerying(false); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private Task RefreshHyperXAsync()
    {
        if (Closing || !_hyperXDiscoveryTask.IsCompleted) return _hyperXDiscoveryTask;
        return _hyperXDiscoveryTask = RefreshHyperXCoreAsync();
    }

    private async Task RefreshHyperXCoreAsync()
    {
        // Refresh is an explicit eligible read, not metadata-only. Coordinator owns discovery,
        // preflight, OFF/suspension/backoff gates and single-flight with startup/ticks.
        try { await Task.Run(() => _hyperXStartup.QueryManuallyAsync(_hyperXStop.Token)); }
        catch (OperationCanceledException) { }
        if (!Closing) DisplayHyperX();
    }

    private void OnUpdated(BatteryMonitoringState state)
    {
        // Shutdown waits for monitoring to stop before destroying this dispatcher.
        _dispatcher.BeginInvoke((Action)(() => { if (!Closing) Display(state); }));
    }

    private void Display(BatteryMonitoringState state)
    {
        _snapshotPublisher.OnLogitechCacheChanged(state);
        _lastKeyboardState = state;
        var view = BatteryPanelView.From(state);
        _popup.Display(view); // Hidden panels update without activation or extra database reads.
        var iconValue = view.Percentage;
        if (iconValue != _iconPercentage)
        {
            var next = BatteryIcon.Create(iconValue);
            _tray.Icon = next;
            _icon.Dispose();
            _icon = next;
            _iconPercentage = iconValue;
        }
        UpdateTrayTooltip();
    }

    private void UpdateTrayTooltip()
    {
        if (Closing || _tray is null) return;
        var text = TrayTooltipView.Build(_lastKeyboardState, _hyperX.Current);
        if (text == _lastTooltip) return;
        _tray.Text = text; // UI-thread callers only; pure cached state, no hover handler or I/O.
        _lastTooltip = text;
    }

    private string StartLanPreview(System.Net.IPAddress address, int port)
    {
        if (Closing) return "Application is closing.";
        if (_lanPreview is not null) return $"Already listening at http://{_lanPreview.Endpoint}{LanPreviewServer.Path}; stop before changing it.";
        try
        {
            _lanPreview = new(_publishedSnapshots, address, port);
            return $"http://{_lanPreview.Endpoint}{LanPreviewServer.Path} · No authentication or encryption.";
        }
        catch (Exception e) when (e is ArgumentException or System.Net.Sockets.SocketException)
        { return "Could not bind the selected interface/port. No firewall or network settings changed."; }
    }

    private async Task StopLanPreviewAsync()
    {
        var server = _lanPreview; _lanPreview = null;
        if (server is not null) await server.DisposeAsync();
    }

    private async Task CloseAsync()
    {
        if (Closing) return;
        _interaction.RequestExit();
        _menu.Enabled = false;
        _lanSettings?.Close();
        await StopLanPreviewAsync();
        ApplyPanelVisibility();
        await _hyperXStop.CancelAsync();
        await _hyperXScheduleTask;
        await _hyperXQueryTask;
        await _hyperXStartup.DisposeAsync();
        await _hyperXAutomatic.DisposeAsync();
        await _hyperXDiscoveryTask;
        await _hyperX.DisposeAsync();
        await _monitor.DisposeAsync();
        _monitor.Updated -= OnUpdated;
        _tray.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lanSettings?.Dispose();
            _snapshotPublisher.Dispose();
            _publishedSnapshots.Dispose();
            _hyperXDevices.Dispose();
            _tray.Dispose();
            _popup.Dispose();
            _menu.Dispose();
            _icon.Dispose();
            _dispatcher.Dispose();
        }
        base.Dispose(disposing);
    }
}
