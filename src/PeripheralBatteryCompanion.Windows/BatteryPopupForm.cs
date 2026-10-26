using PeripheralBatteryCompanion.Core.Presentation;

namespace PeripheralBatteryCompanion.Windows;

internal sealed class BatteryPopupForm : Form
{
    private readonly Label _battery = MakeLabel("Battery: Loading…");
    private readonly Label _reported = MakeLabel("Last reported: Unknown");
    private readonly Label _status = MakeLabel("Reading G HUB…");
    private readonly BatteryLevelBar _level = new();
    private readonly Button _refresh = new() { Text = "Refresh", AutoSize = true };
    private readonly System.Windows.Forms.Timer _dismissTimer = new() { Interval = 50 };
    private readonly Font _headingFont;
    private Point? _trayAnchor;

    public event EventHandler? DismissRequested;
    public event EventHandler? RefreshRequested;

    public BatteryPopupForm()
    {
        Text = "Peripheral Battery Companion";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AutoScroll = true;
        _headingFont = new Font(Font, FontStyle.Bold);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14), Dock = DockStyle.Top
        };
        void Add(Control control)
        {
            var row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Margin = new Padding(0, 0, 0, 8);
            layout.Controls.Add(control, 0, row);
        }
        Add(new Label { Text = "G915 Keyboard", Font = _headingFont, AutoSize = true });
        Add(_battery);
        Add(_level);
        Add(_reported);
        Add(_status);
        Add(new Label { Text = "G305 Mouse", Font = _headingFont, AutoSize = true });
        Add(MakeLabel("Battery: Unknown"));
        Add(MakeLabel("Battery monitoring is not yet supported."));
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var close = new Button { Text = "Close", AutoSize = true };
        actions.Controls.Add(_refresh);
        actions.Controls.Add(close);
        Add(actions);
        Controls.Add(layout);
        _refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        close.Click += (_, _) => RequestDismiss();

        // Shell activation can precede NotifyIcon.MouseClick. Wait for the pointer release
        // so the second tray click can toggle the still-visible panel, rather than reopen it.
        // WM_TIMER is low priority; native tray event ordering still requires desktop validation.
        Deactivate += (_, _) => { if (Visible) _dismissTimer.Start(); };
        Activated += (_, _) => _dismissTimer.Stop();
        _dismissTimer.Tick += (_, _) =>
        {
            if (MouseButtons != MouseButtons.None) return;
            _dismissTimer.Stop();
            if (Visible && !ContainsFocus) RequestDismiss();
        };
        VisibleChanged += (_, _) => { if (!Visible) _dismissTimer.Stop(); };
        SizeChanged += (_, _) => { if (Visible) PositionNearTray(); };
        DpiChanged += (_, _) => BeginInvoke((Action)(() => { if (!IsDisposed && Visible) PositionNearTray(); }));
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(320, 0), UseMnemonic = false
    };

    public void SetRefreshing(bool refreshing) => _refresh.Enabled = !refreshing;

    public void Display(BatteryPanelView view)
    {
        _battery.Text = view.BatteryText;
        _reported.Text = $"Last reported: {view.LastReported?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "Unknown"}";
        _status.Text = view.StatusText;
        _status.Visible = view.StatusText.Length > 0;
        _level.SetPercentage(view.Percentage);
    }

    public void ShowNearTray(Point anchor)
    {
        _trayAnchor = anchor;
        PositionNearTray();
        if (!Visible) Show();
        // Activate only on explicit user requests; background updates never show/focus the form.
        Activate();
    }

    private void PositionNearTray()
    {
        if (_trayAnchor is not Point anchor) return;
        var screen = Screen.FromPoint(anchor);
        if (MaximumSize != screen.WorkingArea.Size) MaximumSize = screen.WorkingArea.Size;
        Location = PopupPlacement.NearTray(screen.Bounds, screen.WorkingArea, Size, anchor,
            Math.Max(4, 8 * DeviceDpi / 96));
    }

    private void RequestDismiss()
    {
        _dismissTimer.Stop();
        DismissRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { RequestDismiss(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            RequestDismiss();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _dismissTimer.Dispose();
        base.Dispose(disposing);
        if (disposing) _headingFont.Dispose();
    }
}
