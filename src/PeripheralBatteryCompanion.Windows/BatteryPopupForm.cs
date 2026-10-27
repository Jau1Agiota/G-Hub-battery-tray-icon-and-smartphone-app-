using PeripheralBatteryCompanion.Core.Presentation;
using PeripheralBatteryCompanion.Core;
using Microsoft.Win32;

namespace PeripheralBatteryCompanion.Windows;

internal sealed class BatteryPopupForm : Form
{
    private readonly Label _battery = MakeLabel("Battery: Loading…");
    private readonly Label _reported = MakeLabel("Last reported: Unknown");
    private readonly Label _status = MakeLabel("Reading G HUB…");
    private readonly BatteryLevelBar _level = new();
    private readonly Label _hyperXEstimate = MakeLabel("Battery: Unknown");
    private readonly BatteryLevelBar _hyperXLevel = new();
    private readonly Label _hyperXUpdated = MakeLabel("Updated: Never");
    private readonly Label _hyperXMonitoring = MakeLabel("");
    private readonly Label _hyperXCompactStatus = MakeLabel("Automatic monitoring off");
    private readonly Label _hyperXNote = MakeLabel("");
    private readonly ToolTip _hyperXTip = new();
    private readonly Button _hyperXSettings = new() { Text = "Advanced &settings", AutoSize = true,
        AccessibleName = "Advanced settings", AccessibleDescription = "Expand or collapse experimental controls and diagnostics" };
    private readonly TableLayoutPanel _hyperXAdvanced = new() { ColumnCount = 1, AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Visible = false,
        AccessibleRole = AccessibleRole.Grouping, AccessibleName = "HyperX advanced settings" };
    private readonly Label _hyperXReceiver = MakeLabel("Receiver: Unknown; headset connectivity: Unknown");
    private readonly Label _hyperXBattery = MakeLabel("Battery: Unknown");
    private readonly Label _hyperXObserved = MakeLabel("Last observed: Never");
    private readonly Label _hyperXStatus = MakeLabel("Metadata inspection pending; no battery query.");
    private readonly Label _hyperXBlocker = MakeLabel(PeripheralBatteryCompanion.Core.HyperXBatteryProvider.LifetimeBlocker);
    private readonly CheckBox _hyperXExperimental = new() { Text = "Experimental HyperX queries", AutoSize = true };
    private readonly Button _hyperXQuery = new() { Text = "Query battery (experimental)", AutoSize = true, Enabled = false };
    private readonly Button _hyperXCancel = new() { Text = "Cancel HyperX operation", AutoSize = true, Enabled = false };
    private readonly CheckBox _hyperXAutomatic = new() { Text = "Automatic HyperX monitoring (save preference)", AutoSize = true };
    private readonly ComboBox _hyperXInterval = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _hyperXSchedule = MakeLabel("Automatic monitoring off.");
    private readonly Button _hyperXResume = new() { Text = "Resume HyperX monitoring", AutoSize = true, Enabled = false };
    private bool _hyperXQuerying, _hyperXAllowed;
    private bool _displayingHyperX;
    private string _hyperXDiagnostics = "HyperX: no inspection yet; no battery query.";
    private readonly Button _refresh = new() { Text = "Refresh", AutoSize = true };
    private readonly System.Windows.Forms.Timer _dismissTimer = new() { Interval = 50 };
    private readonly Font _headingFont;
    private readonly Font _appFont;
    private readonly DashboardThemeStore _themeStore = new();
    private readonly ComboBox _themeChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Dashboard theme" };
    private readonly Label _themeStatus = MakeLabel("");
    private DashboardThemeMode _themeMode;
    private bool _settingTheme;
    private readonly List<DashboardCard> _cards = [];
    private readonly List<TableLayoutPanel> _cardContents = [];
    private readonly Button _gear = new() { Text = "⚙", AutoSize = true, AccessibleName = "Advanced settings", AccessibleDescription = "Open dashboard theme and HyperX settings" };
    private Point? _trayAnchor;
    private bool _fittingToScreen;
    private readonly TableLayoutPanel _layout;

    public event EventHandler? DismissRequested;
    public event EventHandler? RefreshRequested;
    public event Action<bool>? HyperXOptInRequested;
    public event EventHandler? HyperXQueryRequested;
    public event EventHandler? HyperXCancelRequested;
    public event Action<bool, int>? HyperXMonitoringRequested;
    public event EventHandler? HyperXResumeRequested;

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
        _headingFont = new Font(Font.FontFamily, 11, FontStyle.Bold);
        _appFont = new Font(Font.FontFamily, 12, FontStyle.Bold);
        _battery.Font = _headingFont;
        _hyperXEstimate.Font = _headingFont;

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
        var header = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Top };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new DeviceGlyph(DeviceGlyphKind.Battery) { Size = new Size(30, 36), Ink = Color.MediumTurquoise,
            AccessibleName = "Peripheral Battery Companion", TabStop = false }, 0, 0);
        var title = MakeLabel("Peripheral Battery Companion"); title.Font = _appFont; title.Tag = "app-title"; title.Dock = DockStyle.Top;
        header.Controls.Add(title, 1, 0);
        header.Controls.Add(_gear, 2, 0);
        _gear.Click += (_, _) => SetHyperXAdvancedVisible(!_hyperXAdvanced.Visible);
        Add(header);
        var subtitle = MakeLabel("Your devices, at a glance"); subtitle.Tag = "muted"; Add(subtitle);

        TableLayoutPanel NewCard(string name, DeviceGlyphKind glyph, Color accent)
        {
            var card = new DashboardCard { AccessibleName = name, AccentColor = accent, Tag = accent, Dock = DockStyle.Top };
            var content = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top, Margin = Padding.Empty };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var cardHeader = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top };
            cardHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            cardHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            cardHeader.Controls.Add(new DeviceGlyph(glyph) { Size = new Size(30, 32), Ink = accent, TabStop = false,
                AccessibleName = name + " illustration" }, 0, 0);
            var heading = MakeLabel(name); heading.Font = _headingFont; heading.Dock = DockStyle.Top;
            cardHeader.Controls.Add(heading, 1, 0);
            AddRow(content, cardHeader);
            card.Controls.Add(content); _cards.Add(card); _cardContents.Add(content); Add(card);
            return content;
        }
        static void AddRow(TableLayoutPanel panel, Control control)
        {
            var row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Dock = DockStyle.Top; control.Margin = new Padding(0, 0, 0, 6);
            panel.Controls.Add(control, 0, row);
        }
        var keyboardCard = NewCard("G915 Keyboard", DeviceGlyphKind.Keyboard, Color.DeepSkyBlue);
        AddRow(keyboardCard, _battery);
        AddRow(keyboardCard, _level);
        AddRow(keyboardCard, _reported);
        AddRow(keyboardCard, _status);
        var mouseCard = NewCard("G305 Mouse", DeviceGlyphKind.Mouse, Color.SlateGray);
        var mouseBattery = MakeLabel("Battery: Unknown"); mouseBattery.Font = _headingFont;
        AddRow(mouseCard, mouseBattery);
        var mouseLevel = new BatteryLevelBar { AccessibleName = "G305 battery unavailable", TabStop = false };
        AddRow(mouseCard, mouseLevel);
        AddRow(mouseCard, MakeLabel("Battery monitoring is not yet supported."));
        var headsetCard = NewCard("HyperX Cloud III Wireless", DeviceGlyphKind.Headset, Color.IndianRed);
        AddRow(headsetCard, _hyperXEstimate);
        _hyperXLevel.SetEstimate(null);
        _hyperXLevel.TabStop = false;
        AddRow(headsetCard, _hyperXLevel);
        AddRow(headsetCard, _hyperXUpdated);
        AddRow(headsetCard, _hyperXMonitoring);
        AddRow(headsetCard, _hyperXCompactStatus);
        _hyperXNote.ForeColor = SystemColors.GrayText;
        AddRow(headsetCard, _hyperXNote);

        _hyperXSettings.Click += (_, _) => SetHyperXAdvancedVisible(!_hyperXAdvanced.Visible);
        _hyperXAdvanced.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void AddAdvanced(Control control)
        {
            var row = _hyperXAdvanced.RowCount++;
            _hyperXAdvanced.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Dock = DockStyle.Top;
            control.Margin = new Padding(0, 0, 0, 8);
            _hyperXAdvanced.Controls.Add(control, 0, row);
        }
        AddAdvanced(MakeLabel("Advanced settings"));
        AddAdvanced(MakeLabel("Theme"));
        _themeChoice.Items.AddRange(["System", "Dark", "Light"]);
        AddAdvanced(_themeChoice);
        AddAdvanced(_themeStatus);
        _themeMode = _themeStore.Load();
        _themeChoice.SelectedIndex = (int)_themeMode;
        _themeChoice.SelectedIndexChanged += (_, _) =>
        {
            if (_settingTheme) return;
            _themeMode = (DashboardThemeMode)_themeChoice.SelectedIndex;
            _themeStatus.Text = _themeStore.Save(_themeMode) ? "" : "Theme could not be saved; using it for this session.";
            ApplyDashboardTheme();
        };
        AddAdvanced(_hyperXReceiver);
        AddAdvanced(_hyperXBattery);
        AddAdvanced(_hyperXObserved);
        AddAdvanced(_hyperXStatus);
        AddAdvanced(_hyperXExperimental);
        AddAdvanced(_hyperXAutomatic);
        _hyperXInterval.Items.AddRange(["60 seconds", "120 seconds", "300 seconds"]);
        _hyperXInterval.SelectedIndex = 1;
        AddAdvanced(_hyperXInterval);
        AddAdvanced(_hyperXSchedule);
        AddAdvanced(_hyperXResume);
        void MonitoringChanged()
        {
            if (!_displayingHyperX) HyperXMonitoringRequested?.Invoke(_hyperXAutomatic.Checked,
                new[] { 60, 120, 300 }[_hyperXInterval.SelectedIndex]);
        }
        _hyperXAutomatic.CheckedChanged += (_, _) => MonitoringChanged();
        _hyperXInterval.SelectedIndexChanged += (_, _) => MonitoringChanged();
        _hyperXResume.Click += (_, _) => HyperXResumeRequested?.Invoke(this, EventArgs.Empty);
        AddAdvanced(_hyperXQuery);
        AddAdvanced(_hyperXCancel);
        _hyperXCancel.Visible = false;
        _hyperXResume.Visible = false;
        _hyperXCancel.Click += (_, _) => HyperXCancelRequested?.Invoke(this, EventArgs.Empty);
        AddAdvanced(_hyperXBlocker);
        var copyHyperX = new Button { Text = "Copy HyperX diagnostics", AutoSize = true };
        AddAdvanced(copyHyperX);
        copyHyperX.Click += (_, _) =>
        {
            try { Clipboard.SetText(_hyperXDiagnostics); }
            catch (System.Runtime.InteropServices.ExternalException) { _hyperXStatus.Text = "Clipboard unavailable; no query attempted."; }
        };
        _hyperXExperimental.CheckedChanged += (_, _) =>
        {
            if (!_displayingHyperX) HyperXOptInRequested?.Invoke(_hyperXExperimental.Checked);
        };
        _hyperXQuery.Click += (_, _) => HyperXQueryRequested?.Invoke(this, EventArgs.Empty);
        Add(_hyperXAdvanced);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        var close = new Button { Text = "Close", AutoSize = true };
        actions.Controls.Add(_refresh);
        actions.Controls.Add(_hyperXSettings);
        actions.Controls.Add(close);
        Add(actions);
        Controls.Add(_layout);
        ResumeLayout(true);
        _level.TabStop = false;
        foreach (var label in new[] { _reported, _status, _hyperXUpdated, _hyperXMonitoring, _hyperXCompactStatus, _hyperXNote }) label.Tag = "muted";
        ApplyDashboardTheme();
        SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
        _refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        close.Click += (_, _) => RequestDismiss();

        // Shell activation can precede NotifyIcon.MouseClick. Wait for the pointer release
        // so the second tray click can toggle the still-visible panel, rather than reopen it.
        // WM_TIMER is low priority; native tray event ordering still requires desktop validation.
        Deactivate += (_, _) => { if (Visible) _dismissTimer.Start(); };
        Activated += (_, _) => _dismissTimer.Stop();
        _dismissTimer.Tick += (_, _) =>
        {
            if (MouseButtons != MouseButtons.None || _hyperXInterval.DroppedDown || _themeChoice.DroppedDown) return;
            _dismissTimer.Stop();
            if (Visible && !ContainsFocus) RequestDismiss();
        };
        VisibleChanged += (_, _) => { if (!Visible) { _dismissTimer.Stop(); if (!Disposing && !IsDisposed) SetHyperXAdvancedVisible(false); } };
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

    public void DisplayHyperX(HyperXPanelView view)
    {
        _hyperXReceiver.Text = view.ReceiverText;
        _hyperXBattery.Text = view.BatteryText;
        _hyperXObserved.Text = view.ObservedText;
        // Full sanitized stage/error details are copied only on an explicit diagnostics click.
        _hyperXStatus.Text = view.Details;
        _hyperXDiagnostics = "HyperX Cloud III Wireless (03F0:05B7 / MI_03 / COL01 / FF13:0001)\n" + view.ReceiverText + "\n" + view.BatteryText + "\n" + view.ObservedText + "\n" + view.Details;
        _hyperXBlocker.Text = view.QueryBlocker;
        _hyperXAllowed = view.CanQuery;
        _hyperXQuery.Enabled = view.CanQuery && !_hyperXQuerying;
        _displayingHyperX = true;
        try { _hyperXExperimental.Checked = view.ExperimentalEnabled; }
        finally { _displayingHyperX = false; }
        if (Visible) PositionNearTray();
    }

    public void DisplayHyperXMonitoring(HyperXMonitoringStatus status)
    {
        static string Format(DateTimeOffset? value) => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "None";
        _displayingHyperX = true;
        try
        {
            _hyperXAutomatic.Checked = status.Enabled;
            _hyperXInterval.SelectedIndex = status.IntervalSeconds switch { 60 => 0, 300 => 2, _ => 1 };
        }
        finally { _displayingHyperX = false; }
        _hyperXResume.Visible = status.Suspended;
        _hyperXResume.Enabled = status.Suspended && !status.Busy;
        _hyperXSchedule.Text = $"{status.State}\nLast checked: {Format(status.LastChecked)}\nNext check: {Format(status.NextCheck)}";
        _hyperXDiagnostics += $"\nOrigin={status.Origin}; automatic={status.Enabled}; interval={status.IntervalSeconds}s; " +
            $"last checked={Format(status.LastChecked)}; next check/retry={Format(status.NextCheck)}; failures={status.ConsecutiveFailures}; paused={status.Suspended}; state={status.State}";
        _hyperXAllowed &= !status.Suspended;
        _hyperXQuery.Enabled = _hyperXAllowed && !_hyperXQuerying && !status.Busy;
        if (Visible) PositionNearTray();
    }

    public bool HyperXAdvancedVisible => _hyperXAdvanced.Visible;

    public void SetHyperXAdvancedVisible(bool expanded)
    {
        _hyperXAdvanced.Visible = expanded;
        _hyperXSettings.Text = expanded ? "Hide Advanced &settings" : "Advanced &settings";
        _hyperXSettings.AccessibleDescription = expanded ? "Expanded; activate to collapse" : "Collapsed; activate to expand";
        if (!expanded) { AutoScrollPosition = Point.Empty; _hyperXSettings.Focus(); }
        if (Visible) PositionNearTray();
    }

    public void DisplayHyperXCompact(HyperXCompactView view)
    {
        _hyperXEstimate.Text = view.BatteryText;
        _hyperXEstimate.AccessibleDescription = view.AccessibleDescription;
        _hyperXLevel.SetEstimate(view.Percentage);
        _hyperXUpdated.Text = view.UpdatedText;
        _hyperXUpdated.AccessibleDescription = "Last estimate observation: " + view.TimestampDetail;
        _hyperXTip.SetToolTip(_hyperXUpdated, view.TimestampDetail);
        _hyperXTip.SetToolTip(_hyperXEstimate, view.EstimateNote);
        _hyperXMonitoring.Text = view.MonitoringText;
        _hyperXMonitoring.Visible = view.MonitoringText.Length != 0;
        _hyperXCompactStatus.Text = view.StatusText;
        _hyperXCompactStatus.Visible = view.StatusText.Length != 0;
        _hyperXNote.Text = view.EstimateNote.Length == 0 ? "" : "ⓘ Estimated battery · details";
        _hyperXNote.AccessibleDescription = view.EstimateNote;
        _hyperXTip.SetToolTip(_hyperXNote, view.EstimateNote);
        _hyperXNote.Visible = view.EstimateNote.Length != 0;
        if (Visible) PositionNearTray();
    }

    public void SetHyperXQuerying(bool busy)
    {
        _hyperXQuerying = busy;
        _hyperXExperimental.Enabled = !busy;
        _hyperXInterval.Enabled = !busy;
        _hyperXResume.Enabled &= !busy;
        _hyperXQuery.Enabled = !busy && _hyperXAllowed;
        _hyperXCancel.Enabled = busy;
        _hyperXCancel.Visible = busy;
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
        ApplyDashboardTheme();
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
            var cardWidth = Math.Max(1, textWidth - _cards[0].Padding.Horizontal);
            foreach (var label in Descendants(_layout).OfType<Label>())
            {
                var withinCard = _cards.Any(card => card.Contains(label));
                var heading = label.Parent is TableLayoutPanel parent && parent.ColumnCount == 2;
                var headerTitle = label.Tag as string == "app-title";
                label.MaximumSize = new Size(Math.Max(1, (withinCard ? cardWidth : textWidth) -
                    (heading ? 40 * DeviceDpi / 96 : headerTitle ? 80 * DeviceDpi / 96 : 0)), 0);
            }
            foreach (var content in _cardContents) { content.Width = cardWidth; content.PerformLayout(); }
            foreach (var control in _hyperXAdvanced.Controls.OfType<Control>())
                control.MaximumSize = new Size(textWidth, 0);
            _hyperXAdvanced.Width = textWidth;
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

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private void OnSystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke((Action)(() => { if (!IsDisposed) ApplyDashboardTheme(); })); }
        catch (InvalidOperationException) { } // Disposal may race an OS preference notification.
    }

    internal DashboardThemeMode ThemeMode => _themeMode;
    internal void SelectTheme(DashboardThemeMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        // Rendering/testing selection is deliberately non-persisting. Only the user's ComboBox edit saves.
        _settingTheme = true;
        try { _themeMode = mode; _themeChoice.SelectedIndex = (int)mode; ApplyDashboardTheme(); }
        finally { _settingTheme = false; }
    }

    private void ApplyDashboardTheme()
    {
        var systemDark = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            systemDark = key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        var palette = SystemInformation.HighContrast
            ? new DashboardPalette(SystemColors.Window, SystemColors.Window, SystemColors.WindowText, SystemColors.WindowText,
                SystemColors.WindowText, SystemColors.Highlight, SystemColors.Control)
            : DashboardPalette.Resolve(_themeMode, systemDark);
        void Apply(Control control, Color background)
        {
            if (control is DashboardCard card)
            {
                background = palette.Surface; card.BorderColor = palette.Border;
                card.AccentColor = SystemInformation.HighContrast ? SystemColors.WindowText : card.Tag is Color accent ? accent : palette.Accent;
            }
            control.BackColor = background;
            control.ForeColor = control.Tag as string == "muted" ? palette.Muted : palette.Text;
            if (control is Button button)
            {
                button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = palette.Border;
                button.FlatAppearance.MouseOverBackColor = palette.Track;
                button.UseVisualStyleBackColor = false;
            }
            if (control is BatteryLevelBar bar) bar.SetPalette(palette.Track, palette.Border, SystemInformation.HighContrast);
            if (control is DeviceGlyph glyph) glyph.Ink = SystemInformation.HighContrast ? palette.Text : palette.Accent;
            foreach (Control child in control.Controls) Apply(child, background);
            control.Invalidate();
        }
        Apply(this, palette.Background);
    }

    private void RequestDismiss()
    {
        _dismissTimer.Stop();
        _hyperXAdvanced.Visible = false;
        _hyperXSettings.Text = "Advanced &settings";
        _hyperXSettings.AccessibleDescription = "Collapsed; activate to expand";
        DismissRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            if (_hyperXAdvanced.Visible) SetHyperXAdvancedVisible(false);
            else RequestDismiss();
            return true;
        }
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
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
            _hyperXTip.Dispose();
        }
        base.Dispose(disposing);
        if (disposing) { _headingFont.Dispose(); _appFont.Dispose(); }
    }
}
