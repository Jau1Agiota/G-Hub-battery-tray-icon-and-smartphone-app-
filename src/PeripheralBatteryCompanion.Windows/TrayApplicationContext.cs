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
    // Deliberately NO active transport: POC process-exit reclamation is not a tray lifetime guarantee.
    private readonly HyperXBatteryProvider _hyperX = new(new HyperXReceiverDiscovery(), new NgenuityPresenceProbe());
    private readonly CancellationTokenSource _hyperXStop = new();
    private Task _hyperXDiscoveryTask = Task.CompletedTask;
    private Icon _icon;
    private int? _iconPercentage;
    private bool _refreshing;
    private bool Closing => _interaction.IsExitRequested;

    public TrayApplicationContext()
    {
        _dispatcher.CreateControl();
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
        _popup.HyperXQueryRequested += async (_, _) =>
        {
            if (Closing) return;
            try { await _hyperX.QueryOnceAsync(_hyperXStop.Token); } // Production lifetime gate always refuses.
            catch (OperationCanceledException) { }
            if (!Closing) _popup.DisplayHyperX(HyperXPanelView.From(_hyperX));
        };
        _popup.DisplayHyperX(HyperXPanelView.From(_hyperX));
        _refresh.Click += async (_, _) => await RefreshAsync();
        _monitor.Updated += OnUpdated;
        _monitor.Start();
        _ = RefreshHyperXAsync();
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
        var acknowledged = requested && MessageBox.Show(_popup,
            "Experimental HyperX queries may compete with NGENUITY for responses or disrupt battery reporting. " +
            "Response ownership/freshness is unknown. Active queries remain blocked in this build until native lifetime safety is validated. Enable the session-only experimental preference?",
            "Experimental HyperX", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        _hyperX.ConfigureExperimentalQueries(requested, acknowledged);
        _popup.DisplayHyperX(HyperXPanelView.From(_hyperX));
    }

    private Task RefreshHyperXAsync()
    {
        if (Closing || !_hyperXDiscoveryTask.IsCompleted) return _hyperXDiscoveryTask;
        return _hyperXDiscoveryTask = RefreshHyperXCoreAsync();
    }

    private async Task RefreshHyperXCoreAsync()
    {
        try { await _hyperX.ReadDeviceAsync(_hyperXStop.Token); }
        catch (OperationCanceledException) { }
        if (!Closing) _popup.DisplayHyperX(HyperXPanelView.From(_hyperX));
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
            _tray.Dispose();
            _popup.Dispose();
            _menu.Dispose();
            _icon.Dispose();
            _dispatcher.Dispose();
        }
        base.Dispose(disposing);
    }
}
