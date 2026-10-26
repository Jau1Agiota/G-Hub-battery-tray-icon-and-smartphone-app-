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
    private bool _fittingToScreen;
    private readonly TableLayoutPanel _layout;

    public event EventHandler? DismissRequested;
    public event EventHandler? RefreshRequested;

    public BatteryPopupForm()
    {
        SuspendLayout();
        Text = "Peripheral Battery Companion";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        // The form owns its width; deriving it from an auto-sized, docked child is circular.
        AutoSize = false;
        ClientSize = new Size(PopupPlacement.ClientWidthForDpi(96), 320);
        AutoScroll = true;
        _headingFont = new Font(Font, FontStyle.Bold);

        _layout = new TableLayoutPanel
        {
            ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14), Dock = DockStyle.Top
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control control)
        {
            var row = _layout.RowCount++;
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Margin = new Padding(0, 0, 0, 8);
            control.Dock = DockStyle.Top;
            _layout.Controls.Add(control, 0, row);
        }
        Add(new Label { Text = "G915 Keyboard", Font = _headingFont, AutoSize = true });
        Add(_battery);
        Add(_level);
        Add(_reported);
        Add(_status);
        Add(new Label { Text = "G305 Mouse", Font = _headingFont, AutoSize = true });
        Add(MakeLabel("Battery: Unknown"));
        Add(MakeLabel("Battery monitoring is not yet supported."));
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        var close = new Button { Text = "Close", AutoSize = true };
        actions.Controls.Add(_refresh);
        actions.Controls.Add(close);
        Add(actions);
        Controls.Add(_layout);
        ResumeLayout(true);
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
        if (Visible) PositionNearTray();
    }

    public void ShowNearTray(Point anchor)
    {
        _trayAnchor = anchor;
        PositionNearTray();
        if (!Visible) Show();
        // Activate only on explicit user requests; background updates never show/focus the form.
        Activate();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        PositionNearTray(); // Handle creation may have changed the monitor DPI/layout.
    }

    private void PositionNearTray()
    {
        if (_fittingToScreen || _trayAnchor is not Point anchor) return;
        _fittingToScreen = true;
        try
        {
            var screen = Screen.FromPoint(anchor);
            var work = screen.WorkingArea;
            // Include the actual DPI-dependent caption/borders when fitting outer bounds.
            var chrome = SizeFromClientSize(Size.Empty);
            var desiredWidth = PopupPlacement.ClientWidthForDpi(DeviceDpi) + chrome.Width;
            var width = Math.Min(desiredWidth, work.Width);
            var clientWidth = Math.Max(1, width - chrome.Width);
            var textWidth = Math.Max(1, clientWidth - _layout.Padding.Horizontal);

            // Clear a previous monitor's minimum before applying a smaller working area.
            MinimumSize = Size.Empty;
            MaximumSize = work.Size;
            _layout.Width = clientWidth;
            foreach (var label in _layout.Controls.OfType<Label>())
                label.MaximumSize = new Size(textWidth, 0);
            _level.Width = textWidth;
            _layout.PerformLayout();
            var contentHeight = _layout.GetPreferredSize(new Size(clientWidth, 0)).Height;
            var preferred = new Size(width, contentHeight + chrome.Height);
            MinimumSize = new Size(width, 0);
            Size = PopupPlacement.FitSize(preferred, work.Size);
            PerformLayout();
            Location = PopupPlacement.NearTray(screen.Bounds, work, Size, anchor,
                Math.Max(4, 8 * DeviceDpi / 96));
        }
        finally { _fittingToScreen = false; }
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
