using PeripheralBatteryCompanion.Core.Presentation;
using PeripheralBatteryCompanion.Core;

namespace PeripheralBatteryCompanion.Windows;

internal sealed class BatteryLevelBar : Control
{
    private int? _percentage;
    private bool _estimateMode;
    private Color _track = SystemColors.Control, _border = Color.Gray;
    private bool _highContrast;
    public BatteryLevelBar()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Size = new Size(320, 16);
        AccessibleRole = AccessibleRole.ProgressBar;
        AccessibleName = "G915 battery level";
        AccessibleDescription = "Unavailable";
    }

    public void SetPercentage(int? percentage) => SetLogitechReading(percentage, BatteryPrecision.Percentage);

    public void SetLogitechReading(int? percentage, BatteryPrecision precision)
    {
        _estimateMode = false;
        _percentage = percentage is >= 0 and <= 100 ? percentage : null;
        AccessibleDescription = _percentage is int value ? precision switch
        {
            BatteryPrecision.Percentage => $"{value}% last reported",
            BatteryPrecision.Approximate => $"{value}% approximate battery reading",
            _ => $"{value}% battery reading; precision unknown"
        } : "Unavailable; no current battery reading";
        Invalidate();
    }
    internal float FillWidth => BatteryBarGeometry.FillWidth(Width, _percentage);

    public void SetEstimate(int? percentage)
    {
        _estimateMode = true;
        _percentage = percentage is >= 0 and <= 100 ? percentage : null;
        AccessibleName = "HyperX estimated battery level";
        AccessibleDescription = _percentage is { } value
            ? $"{value}% estimated; response source not uniquely verified; headset connectivity unknown"
            : "No current estimate; unknown or stale. Headset connectivity unknown";
        Invalidate();
    }

    internal static Color GetColor(int? percentage) => BatteryPanelView.ColorFor(percentage) switch
    {
        BatteryLevelColor.Green => Color.LimeGreen,
        BatteryLevelColor.Yellow => Color.Gold,
        BatteryLevelColor.Red => Color.IndianRed,
        _ => Color.Gray
    };

    public void SetPalette(Color track, Color border, bool highContrast)
    {
        _track = track; _border = border; _highContrast = highContrast; Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var bounds = new RectangleF(.5f, .5f, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var outline = DashboardDrawing.Rounded(bounds, Height / 2f);
        using var track = new SolidBrush(_track); using var border = new Pen(_border);
        e.Graphics.FillPath(track, outline); e.Graphics.DrawPath(border, outline);
        var fillWidth = FillWidth;
        if (fillWidth <= 0 || Height <= 4) return;
        var color = _highContrast ? SystemColors.Highlight : _estimateMode ? Color.Gold : GetColor(_percentage);
        using var fill = new SolidBrush(color);
        using var path = DashboardDrawing.Rounded(new RectangleF(2, 2, fillWidth, Height - 4), (Height - 4) / 2f);
        e.Graphics.FillPath(fill, path);
    }
}
