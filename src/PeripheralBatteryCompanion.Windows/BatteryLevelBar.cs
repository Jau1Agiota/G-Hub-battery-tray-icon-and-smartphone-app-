using PeripheralBatteryCompanion.Core.Presentation;

namespace PeripheralBatteryCompanion.Windows;

internal sealed class BatteryLevelBar : Control
{
    private int? _percentage;
    private bool _estimateMode;
    public BatteryLevelBar()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Size = new Size(320, 16);
        AccessibleRole = AccessibleRole.ProgressBar;
        AccessibleName = "G915 battery level";
        AccessibleDescription = "Unavailable";
    }

    public void SetPercentage(int? percentage)
    {
        if (_percentage == percentage) return;
        _percentage = percentage;
        AccessibleDescription = percentage is int number ? $"{number}% last reported" : "Unavailable";
        Invalidate();
    }

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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var border = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        var color = _estimateMode && _percentage.HasValue ? Color.Gold : GetColor(_percentage);
        using var outline = new Pen(color);
        e.Graphics.DrawRectangle(outline, border);
        var fillWidth = _percentage is int number ? (Width - 4) * number / 100 : _estimateMode ? 0 : Width - 4;
        using var fill = new SolidBrush(color);
        if (fillWidth > 0 && Height > 4) e.Graphics.FillRectangle(fill, 2, 2, fillWidth, Height - 4);
    }
}
