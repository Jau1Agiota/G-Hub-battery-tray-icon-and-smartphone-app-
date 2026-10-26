using PeripheralBatteryCompanion.Core;

namespace PeripheralBatteryCompanion.Windows;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly Control _dispatcher = new();
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _battery = new("Battery: Loading…");
    private readonly ToolStripMenuItem _reported = new("Last reported: Unknown");
    private readonly ToolStripMenuItem _status = new("Reading G HUB database…");
    private readonly ToolStripMenuItem _access = new("Last database access: None");
    private readonly ToolStripMenuItem _refresh = new("Refresh");
    private readonly BatteryMonitoringService _monitor = new(new GHubSqliteBatteryProvider());
    private Icon _icon;
    private int? _iconPercentage;
    private bool _closing;

    public TrayApplicationContext()
    {
        _dispatcher.CreateControl();
        _icon = BatteryIcon.Create(null);
        AddLabel("Peripheral Battery Companion");
        _menu.Items.Add(new ToolStripSeparator());
        AddLabel("G915 Keyboard");
        foreach (var item in new[] { _battery, _reported, _status, _access })
        {
            item.Enabled = false;
            _menu.Items.Add(item);
        }
        AddLabel("Connectivity: Unknown");
        _menu.Items.Add(new ToolStripSeparator());
        AddLabel(BatteryDevice.UnsupportedMouse.Name);
        AddLabel("Battery: Unknown — Not yet supported");
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_refresh);
        _menu.Items.Add("Exit", null, async (_, _) => await CloseAsync());
        _tray = new NotifyIcon
        {
            Icon = _icon, Visible = true, Text = "G915: Unavailable", ContextMenuStrip = _menu
        };
        _tray.MouseClick += (_, e) =>
        {
            if (!_closing && e.Button == MouseButtons.Left) _menu.Show(Cursor.Position);
        };
        _refresh.Click += async (_, _) =>
        {
            _refresh.Enabled = false;
            try { await _monitor.RefreshAsync(); }
            catch (OperationCanceledException) { }
            finally { if (!_closing) _refresh.Enabled = true; }
        };
        _monitor.Updated += OnUpdated;
        _monitor.Start();
    }

    private void AddLabel(string text) => _menu.Items.Add(new ToolStripMenuItem(text) { Enabled = false });

    private void OnUpdated(BatteryMonitoringState state)
    {
        // Dispose waits for monitoring to stop before destroying this dispatcher.
        _dispatcher.BeginInvoke((Action)(() => { if (!_closing) Display(state); }));
    }

    private void Display(BatteryMonitoringState state)
    {
        var keyboard = state.Current.Keyboard;
        var available = keyboard.Availability == BatteryAvailability.Available;
        var shown = available ? keyboard : state.LastKnownKeyboard;
        _battery.Text = shown?.Percentage is int percentage
            ? $"Battery: {percentage}%{(available ? " (last reported)" : " (cached; unavailable)")}"
            : "Battery: Unavailable";
        _reported.Text = $"Last reported: {FormatTime(shown?.LastReported)}";
        _status.Text = keyboard.Status;
        _access.Text = $"Last database access: {FormatTime(state.LastSuccessfulDatabaseAccess)}";
        var iconValue = available ? keyboard.Percentage : null;
        if (iconValue != _iconPercentage)
        {
            var next = BatteryIcon.Create(iconValue);
            _tray.Icon = next;
            _icon.Dispose();
            _icon = next;
            _iconPercentage = iconValue;
        }
        _tray.Text = available ? $"G915: {keyboard.Percentage}% (last reported)" : "G915: Unavailable";
    }

    private static string FormatTime(DateTimeOffset? time) =>
        time?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "Unknown";

    private async Task CloseAsync()
    {
        if (_closing) return;
        _closing = true;
        _menu.Enabled = false;
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
            _menu.Dispose();
            _icon.Dispose();
            _dispatcher.Dispose();
        }
        base.Dispose(disposing);
    }
}
