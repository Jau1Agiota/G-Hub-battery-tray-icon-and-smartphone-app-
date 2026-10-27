using PeripheralBatteryCompanion.Core;
using PeripheralBatteryCompanion.Core.Presentation;

namespace PeripheralBatteryCompanion.Windows;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly Control _dispatcher = new();
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _refresh = new("Refresh");
    private readonly BatteryPopupForm _popup = new(); // One instance, retained until application exit.
    private readonly TrayPanelState _interaction = new();
    private readonly BatteryMonitoringService _monitor = new(new GHubSqliteBatteryProvider());
    private readonly HyperXHelperTransport _hyperXTransport = new(new HyperXHelperProcessFactory(), allowRecoverableFailures: true);
    private readonly HyperXBatteryProvider _hyperX;
    private readonly HyperXAutomaticMonitor _hyperXAutomatic;
    private readonly HyperXDeviceNotifications _hyperXDevices;
    private readonly HyperXPreferencesStore _hyperXSettings = new();
    private HyperXMonitoringPreferences _hyperXPreferences;
    private Task _hyperXScheduleTask = Task.CompletedTask;
    private bool _hyperXAcknowledged;
    private CancellationTokenSource? _hyperXQueryStop;
    private Task _hyperXQueryTask = Task.CompletedTask;
    private readonly CancellationTokenSource _hyperXStop = new();
    private Task _hyperXDiscoveryTask = Task.CompletedTask;
    private Icon _icon;
    private int? _iconPercentage;
    private bool _refreshing;
    private bool Closing => _interaction.IsExitRequested;

    public TrayApplicationContext()
    {
        _hyperX = new(new HyperXReceiverDiscovery(), new NgenuityPresenceProbe(), _hyperXTransport, allowWithoutNgenuity: true);
        _hyperXAutomatic = new(_hyperX);
        _hyperXPreferences = _hyperXSettings.Load();
        _hyperXAutomatic.Configure(_hyperXPreferences.Enabled, _hyperXPreferences.IntervalSeconds);
        _dispatcher.CreateControl();
        _hyperXDevices = new(removed =>
        {
            if (Closing) return;
            if (removed) _hyperXAutomatic.ReceiverRemoved();
            DisplayHyperX(); // Arrival never queries; the next metadata wakeup rediscovers and schedules a full interval.
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
        _popup.HyperXOptInRequested += ConfigureHyperX;
        _popup.HyperXQueryRequested += (_, _) =>
        {
            if (!Closing && _hyperX.CanQuery && !_hyperXAutomatic.Status.Suspended && _hyperXQueryTask.IsCompleted) _hyperXQueryTask = RunHyperXOperationAsync(false);
        };
        _popup.HyperXCancelRequested += (_, _) => { _hyperXAutomatic.Cancel(); _hyperXQueryStop?.Cancel(); };
        _popup.HyperXMonitoringRequested += ConfigureAutomaticHyperX;
        _popup.HyperXResumeRequested += (_, _) => { _hyperXAutomatic.Resume(); DisplayHyperX(); };
        DisplayHyperX();
        _refresh.Click += async (_, _) => await RefreshAsync();
        _monitor.Updated += OnUpdated;
        _monitor.Start();
        _ = RefreshHyperXAsync();
        _hyperXScheduleTask = MonitorHyperXAsync();
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

    private void ConfigureHyperX(bool requested)
    {
        if (Closing || !_hyperXQueryTask.IsCompleted) return;
        var acknowledged = requested && (_hyperXAcknowledged || MessageBox.Show(_popup,
            "Experimental HyperX queries can compete with NGENUITY or disrupt battery reporting. " +
            "A result is an ambiguous candidate, never confirmed fresh. Enable queries for this session? " +
            "A query-free helper containment check runs first. Automatic monitoring is separately optional; " +
            "when selected, one request runs after each scheduled interval, including with NGENUITY closed.",
            "Experimental HyperX", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes);
        _hyperXAcknowledged |= acknowledged;
        _hyperX.ConfigureExperimentalQueries(requested, acknowledged);
        _hyperXAutomatic.Configure(_hyperXPreferences.Enabled, _hyperXPreferences.IntervalSeconds);
        if (!requested) { _hyperXAutomatic.Cancel(); _hyperXQueryStop?.Cancel(); }
        DisplayHyperX();
        if (acknowledged) _hyperXQueryTask = RunHyperXOperationAsync(true);
    }

    private async Task RunHyperXOperationAsync(bool preflight)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_hyperXStop.Token);
        _hyperXQueryStop = cancel;
        _popup.SetHyperXQuerying(true);
        try
        {
            if (preflight)
            {
                await _hyperXTransport.PrepareAsync(true, cancel.Token);
                _hyperXAutomatic.Configure(_hyperXPreferences.Enabled, _hyperXPreferences.IntervalSeconds);
            }
            else await _hyperXAutomatic.QueryManuallyAsync(cancel.Token);
        }
        catch (OperationCanceledException) { }
        catch { _hyperX.ConfigureExperimentalQueries(false); }
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
        _popup.DisplayHyperX(HyperXPanelView.From(_hyperX));
        _popup.DisplayHyperXMonitoring(_hyperXAutomatic.Status);
    }

    private void ConfigureAutomaticHyperX(bool enabled, int interval)
    {
        if (Closing) return;
        // Only an explicit UI edit persists enabled mode. Loading never acknowledges native queries.
        _hyperXPreferences = new(enabled, interval);
        var saved = _hyperXSettings.Save(_hyperXPreferences);
        _hyperXAutomatic.Configure(enabled, interval);
        if (!saved) MessageBox.Show(_popup, "Monitoring preferences could not be saved; this choice applies only to this session.",
            "HyperX preferences", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DisplayHyperX();
    }

    private async Task MonitorHyperXAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5)); // Metadata/state wakeup, not a 5-second battery poll.
            while (await timer.WaitForNextTickAsync(_hyperXStop.Token))
            {
                if (!_hyperXQueryTask.IsCompleted) continue; // Preflight/manual share the same owned work.
                _hyperXQueryTask = Task.Run(() => _hyperXAutomatic.TickAsync(_hyperXStop.Token));
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
        try { await _hyperXAutomatic.RefreshMetadataAsync(_hyperXStop.Token); }
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
        _tray.Text = iconValue.HasValue ? $"G915: {iconValue}% (last reported)" : "G915: Unavailable";
    }

    private async Task CloseAsync()
    {
        if (Closing) return;
        _interaction.RequestExit();
        _menu.Enabled = false;
        ApplyPanelVisibility();
        await _hyperXStop.CancelAsync();
        await _hyperXScheduleTask;
        await _hyperXQueryTask;
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
