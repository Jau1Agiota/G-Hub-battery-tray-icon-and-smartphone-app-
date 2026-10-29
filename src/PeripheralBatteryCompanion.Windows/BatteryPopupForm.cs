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
    private readonly Label _mouseBattery = MakeLabel("Battery: Unknown");
    private readonly BatteryLevelBar _mouseLevel = new() { AccessibleName = "G305 battery status", TabStop = false };
    private readonly Label _mouseStatus = MakeLabel("Waiting for G HUB live status");
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
        Padding = new Padding(14), AccessibleRole = AccessibleRole.Grouping, AccessibleName = "HyperX advanced settings" };
    private readonly Label _hyperXReceiver = MakeLabel("Receiver: Unknown; headset connectivity: Unknown");
    private readonly Label _hyperXBattery = MakeLabel("Battery: Unknown");
    private readonly Label _hyperXObserved = MakeLabel("Last observed: Never");
    private readonly Label _hyperXStatus = MakeLabel("Metadata inspection pending; no battery query.");
    private readonly Label _hyperXBlocker = MakeLabel(PeripheralBatteryCompanion.Core.HyperXBatteryProvider.LifetimeBlocker);
    private readonly Button _hyperXQuery = new() { Text = "Query battery", AutoSize = true, Enabled = false };
    private readonly Button _hyperXCancel = new() { Text = "Cancel HyperX operation", AutoSize = true, Enabled = false };
    private readonly CheckBox _hyperXAutomatic = new() { Text = "Automatic HyperX monitoring", AutoSize = true };
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
    private readonly Font _metaFont;
    private readonly DashboardThemeStore _themeStore = new();
    private readonly ComboBox _themeChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Dashboard theme" };
    private readonly Label _themeStatus = MakeLabel("");
    private DashboardThemeMode _themeMode;
    private bool _settingTheme;
    private readonly List<ProductCard> _cards = [];
    private readonly Form _settingsWindow = new() { Text = "Advanced settings", ShowInTaskbar = false,
        AutoScaleMode = AutoScaleMode.Dpi, AutoScroll = true, MinimizeBox = false, MaximizeBox = false };
    private readonly Label _keyboardDetails = MakeLabel("G915: no report yet.");
    private readonly Panel _header = new();
    private readonly FlowLayoutPanel _footer = new() { WrapContents = false, Margin = Padding.Empty };
    private readonly Label _title = MakeLabel("Peripheral Battery Companion");
    private readonly Label _subtitle = MakeLabel("Your devices, at a glance");
    private readonly DeviceGlyph _appGlyph = new(DeviceGlyphKind.Battery) { TabStop = false };
    private readonly Button _close = new() { Text = "×", AccessibleName = "Close battery panel", TabStop = true };
    private readonly Button _gear = new() { Text = "⚙", AutoSize = true, AccessibleName = "Advanced settings", AccessibleDescription = "Open dashboard theme and HyperX settings" };
    private Point? _trayAnchor;
    private bool _fittingToScreen;
    private readonly Panel _layout;

    public event EventHandler? DismissRequested;
    public event EventHandler? RefreshRequested;
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
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        // The form owns its width; deriving it from an auto-sized, docked child is circular.
        AutoSize = false;
        ClientSize = DashboardLayout.NominalSize(96);
        AutoScroll = true;
        _headingFont = new Font(Font.FontFamily, 10, FontStyle.Bold);
        _appFont = new Font(Font.FontFamily, 12, FontStyle.Bold);
        _metaFont = new Font(Font.FontFamily, 8.25f);
        _battery.Font = _headingFont;
        _hyperXEstimate.Font = _headingFont;

        _layout = new Panel { AutoSize = false, Size = DashboardLayout.NominalSize(96) };
        _title.Font = _appFont;
        _header.Controls.AddRange([_appGlyph, _title, _subtitle, _gear, _close]);
        _layout.Controls.Add(_header);
        _gear.Click += (_, _) => SetHyperXAdvancedVisible(true);
        _close.Click += (_, _) => RequestDismiss();
        _mouseBattery.Font = _headingFont;
        _mouseStatus.AutoEllipsis = true;
        _hyperXLevel.SetEstimate(null); _hyperXLevel.TabStop = false;
        _cards.Add(new("G915 Keyboard", "g915.png", Color.FromArgb(9, 167, 244), _headingFont,
            _battery, _level, _reported, _status));
        _cards.Add(new("G305 Mouse", "g305.png", Color.FromArgb(154, 176, 203), _headingFont,
            _mouseBattery, _mouseLevel, _mouseStatus));
        _cards.Add(new("HyperX Cloud III Wireless", "hyperx-cloud-iii.png", Color.FromArgb(239, 70, 88), _headingFont,
            _hyperXEstimate, _hyperXLevel, _hyperXUpdated, _hyperXCompactStatus));
        foreach (var card in _cards) _layout.Controls.Add(card);
        _hyperXUpdated.Font = _metaFont;
        foreach (var label in new[] { _battery, _reported, _status, _hyperXEstimate, _hyperXUpdated, _hyperXCompactStatus })
            label.AutoEllipsis = true;
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
        AddAdvanced(_keyboardDetails);
        AddAdvanced(_hyperXMonitoring);
        AddAdvanced(_hyperXNote);
        AddAdvanced(_hyperXReceiver);
        AddAdvanced(_hyperXBattery);
        AddAdvanced(_hyperXObserved);
        AddAdvanced(_hyperXStatus);
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
        _hyperXQuery.Click += (_, _) => HyperXQueryRequested?.Invoke(this, EventArgs.Empty);
        _settingsWindow.Controls.Add(_hyperXAdvanced);
        _settingsWindow.FormClosing += (_, e) =>
        {
            if (e.CloseReason != CloseReason.FormOwnerClosing && !Disposing)
            { e.Cancel = true; SetHyperXAdvancedVisible(false); }
        };
        _settingsWindow.KeyPreview = true;
        _settingsWindow.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { SetHyperXAdvancedVisible(false); e.Handled = true; } };
        _settingsWindow.Deactivate += (_, _) => { if (Visible) _dismissTimer.Start(); };
        _footer.Controls.AddRange([_refresh, _hyperXSettings]);
        _layout.Controls.Add(_footer);
        Controls.Add(_layout);
        ResumeLayout(true);
        _level.TabStop = false;
        foreach (var label in new[] { _reported, _status, _hyperXUpdated, _hyperXMonitoring, _hyperXCompactStatus, _hyperXNote }) label.Tag = "muted";
        ApplyDashboardTheme();
        SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
        _refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);


        // Shell activation can precede NotifyIcon.MouseClick. Wait for the pointer release
        // so the second tray click can toggle the still-visible panel, rather than reopen it.
        // WM_TIMER is low priority; native tray event ordering still requires desktop validation.
        Deactivate += (_, _) => { if (Visible) _dismissTimer.Start(); };
        Activated += (_, _) => _dismissTimer.Stop();
        _dismissTimer.Tick += (_, _) =>
        {
            if (MouseButtons != MouseButtons.None || _hyperXInterval.DroppedDown || _themeChoice.DroppedDown) return;
            _dismissTimer.Stop();
            if (_settingsWindow.ContainsFocus) return;
            if (Visible && !ContainsFocus) RequestDismiss();
        };
        VisibleChanged += (_, _) => { if (!Visible) { _dismissTimer.Stop(); if (!Disposing && !IsDisposed) SetHyperXAdvancedVisible(false); } };
        SizeChanged += (_, _) => { if (Visible) PositionNearTray(); };
        FontChanged += (_, _) => { if (IsHandleCreated && Visible) PositionNearTray(); };
        DpiChanged += (_, _) => BeginInvoke((Action)(() => { if (!IsDisposed && Visible) PositionNearTray(); }));
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(480, 0), UseMnemonic = false
    };

    public void SetRefreshing(bool refreshing) => _refresh.Enabled = !refreshing;

    public void Display(BatteryPanelView view)
    {
        _battery.Text = view.BatteryText;
        _reported.Text = $"{(view.IsLiveGateway ? "Observed" : "Last reported")}: {view.LastReported?.ToLocalTime().ToString("HH:mm") ?? "Unknown"}";
        _hyperXTip.SetToolTip(_reported, view.LastReported?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "Unknown");
        _keyboardDetails.Text = view.BatteryText + "\n" + view.StatusText + "\n" +
            (view.LastReported?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "Timestamp unknown");
        _status.Text = view.IsLiveGateway ? view.KeyboardStatusText : view.Percentage.HasValue ? "Stored report · freshness unknown" : view.StatusText.Length == 0 ? "" : view.StatusText.Contains("(cached)")
            ? "Last report cached · see settings" : "Battery temporarily unavailable";
        _hyperXTip.SetToolTip(_status, view.StatusText);
        _status.Visible = view.StatusText.Length > 0;
        _level.SetLogitechReading(view.Percentage, view.KeyboardPrecision);
        _mouseBattery.Text = view.MouseBatteryText;
        _mouseLevel.SetLogitechReading(view.MousePercentage, view.Mouse?.Precision ?? BatteryPrecision.Unknown);
        _mouseBattery.AccessibleDescription = view.MouseStatusText;
        _mouseStatus.Text = view.MouseCompactStatus;
        _hyperXTip.SetToolTip(_mouseStatus, view.MouseStatusText + "\n" + (view.Mouse?.LastReported?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "No current observation"));
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

    public bool HyperXAdvancedVisible => _settingsWindow.Visible;
    internal Control AdvancedSurface => _hyperXAdvanced;
    public IWin32Window DialogOwner => _settingsWindow.Visible ? _settingsWindow : this;

    public void SetHyperXAdvancedVisible(bool expanded)
    {
        if (expanded)
        {
            var work = Screen.FromControl(this).WorkingArea;
            var width = Math.Min(DashboardLayout.Scale(520, DeviceDpi), work.Width);
            var height = Math.Min(DashboardLayout.Scale(650, DeviceDpi), work.Height);
            _settingsWindow.StartPosition = FormStartPosition.Manual;
            _settingsWindow.Bounds = new Rectangle(work.Left + (work.Width - width) / 2,
                work.Top + (work.Height - height) / 2, width, height);
            _hyperXAdvanced.Visible = true;
            foreach (Control control in _hyperXAdvanced.Controls)
                control.MaximumSize = new Size(Math.Max(1, _settingsWindow.ClientSize.Width - 28), 0);
            if (!_settingsWindow.Visible) _settingsWindow.Show(this);
            _settingsWindow.Activate();
        }
        else { _settingsWindow.Hide(); _hyperXAdvanced.Visible = false; if (Visible && !Disposing) _hyperXSettings.Focus(); }
    }

    public void DisplayHyperXCompact(HyperXCompactView view)
    {
        _hyperXEstimate.Text = view.BatteryText;
        _hyperXEstimate.AccessibleDescription = view.AccessibleDescription;
        _hyperXLevel.SetEstimate(view.Percentage);
        _hyperXUpdated.Text = view.UpdatedText + (view.MonitoringText.Length == 0 ? "" : " · " +
            view.MonitoringText.Replace("Auto monitoring · every ", "Every "));
        _hyperXTip.SetToolTip(_hyperXUpdated, view.TimestampDetail + "\n" + view.MonitoringText);
        _hyperXUpdated.AccessibleDescription = "Last estimate observation: " + view.TimestampDetail;

        _hyperXTip.SetToolTip(_hyperXEstimate, view.EstimateNote);
        _hyperXMonitoring.Text = view.MonitoringText;
        _hyperXMonitoring.Visible = view.MonitoringText.Length != 0;
        _hyperXCompactStatus.Text = view.SecondaryText;
        _hyperXTip.SetToolTip(_hyperXCompactStatus, view.StatusText + "\n" + view.EstimateNote);
        _hyperXCompactStatus.AccessibleDescription = view.AccessibleDescription;
        _hyperXCompactStatus.Visible = _hyperXCompactStatus.Text.Length != 0;
        _hyperXNote.Text = view.EstimateNote.Length == 0 ? "" : "ⓘ Estimated battery · details";
        _hyperXNote.AccessibleDescription = view.EstimateNote;
        _hyperXTip.SetToolTip(_hyperXNote, view.EstimateNote);
        _hyperXNote.Visible = view.EstimateNote.Length != 0;
        if (Visible) PositionNearTray();
    }

    public void DisplayStartupState(HyperXStartupPhase phase, HyperXStartupPreferences preferences)
    {
        _displayingHyperX = true;
        try { _hyperXAutomatic.Checked = preferences.UserMonitoringEnabled; }
        finally { _displayingHyperX = false; }
        _hyperXDiagnostics += "\nStartup phase: " + phase + "; persistent safety stop=" + preferences.SafetyLatched;
        if (phase is HyperXStartupPhase.SafetyLatched or HyperXStartupPhase.PreflightPending)
        {
            _hyperXCompactStatus.Text = phase == HyperXStartupPhase.SafetyLatched ? "Safety stop · see settings" : "Preparing safe monitoring";
            _hyperXCompactStatus.Visible = true;
        }
    }

    public void SetHyperXQuerying(bool busy)
    {
        _hyperXQuerying = busy;
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
            var nominal = DashboardLayout.NominalSize(DeviceDpi);
            var cardHeight = _cards.Max(card => card.RequiredHeight);
            var headerExtra = Math.Max(0, _subtitle.Font.Height + DashboardLayout.Scale(41, DeviceDpi) - DashboardLayout.Scale(64, DeviceDpi));
            var cardExtra = cardHeight - DashboardLayout.Scale(132, DeviceDpi);
            var footerExtra = Math.Max(0, Font.Height + DashboardLayout.Scale(16, DeviceDpi) - DashboardLayout.Scale(44, DeviceDpi));
            nominal.Height += headerExtra + cardExtra * 3 + footerExtra;
            MinimumSize = Size.Empty; MaximumSize = work.Size;
            Size = PopupPlacement.FitSize(nominal, work.Size); // Borderless: Size and ClientSize coincide.
            var contentWidth = ClientSize.Width;
            if (nominal.Height > ClientSize.Height) contentWidth = Math.Max(1, contentWidth - SystemInformation.VerticalScrollBarWidth);
            _layout.Size = new(contentWidth, nominal.Height);
            AutoScrollMinSize = new(0, nominal.Height);
            _header.Bounds = DashboardLayout.Header(DeviceDpi, contentWidth);
            _header.Height += headerExtra;
            for (var i = 0; i < _cards.Count; i++)
            {
                var bounds = DashboardLayout.Card(i, DeviceDpi, contentWidth);
                bounds.Y += headerExtra + cardExtra * i; bounds.Height = cardHeight;
                _cards[i].Bounds = bounds;
            }
            var footerBounds = DashboardLayout.Footer(DeviceDpi, contentWidth);
            footerBounds.Y += headerExtra + cardExtra * 3; footerBounds.Height += footerExtra;
            _footer.Bounds = footerBounds;
            int S(int value) => DashboardLayout.Scale(value, DeviceDpi);
            _appGlyph.Bounds = new(0, S(8), S(32), S(44));
            _title.AutoSize = _subtitle.AutoSize = false;
            _title.MaximumSize = _subtitle.MaximumSize = Size.Empty;
            _title.Bounds = new(S(44), S(3), Math.Max(1, _header.Width - S(124)), S(29));
            _subtitle.Bounds = new(S(44), S(35), Math.Max(1, _header.Width - S(124)), Math.Max(S(23), _subtitle.Font.Height + S(2)));
            _title.AutoEllipsis = _subtitle.AutoEllipsis = true;
            _gear.AutoSize = false; _gear.Bounds = new(_header.Width - S(74), S(12), S(34), S(34));
            _close.Bounds = new(_header.Width - S(32), S(12), S(32), S(34));
            _refresh.AutoSize = _hyperXSettings.AutoSize = false;
            _refresh.Size = new(Math.Max(1, (_footer.Width - S(16)) / 2), _footer.Height);
            _hyperXSettings.Size = new(Math.Max(1, _footer.Width - _refresh.Width - S(16)), _footer.Height);
            _refresh.Margin = new(0, 0, S(16), 0); _hyperXSettings.Margin = Padding.Empty;
            MinimumSize = new(Size.Width, 0);
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
            if (control is ProductCard card)
            { background = palette.Surface; card.ApplyPalette(palette, SystemInformation.HighContrast); }
            control.BackColor = control is Label && control.Parent is ProductCard ? Color.Transparent : background;
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
        Apply(_settingsWindow, palette.Background);
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
            _settingsWindow.Dispose();
            _hyperXTip.Dispose();
        }
        base.Dispose(disposing);
        if (disposing) { _headingFont.Dispose(); _appFont.Dispose(); _metaFont.Dispose(); }
    }
}
