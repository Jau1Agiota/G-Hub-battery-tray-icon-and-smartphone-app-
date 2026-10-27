using System.Drawing.Drawing2D;
using System.ComponentModel;
using PeripheralBatteryCompanion.Core.Presentation;

namespace PeripheralBatteryCompanion.Windows;

internal static class DashboardDrawing
{
    public static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0) return path;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); return path;
    }
}

internal sealed class DashboardCard : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color AccentColor { get; set; }
    public DashboardCard()
    {
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12); Margin = new Padding(0, 0, 0, 12);
        DoubleBuffered = true; ResizeRedraw = true;
        AccessibleRole = AccessibleRole.Grouping;
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = DashboardDrawing.Rounded(new RectangleF(.5f, .5f, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 10 * DeviceDpi / 96f);
        using var fill = new SolidBrush(BackColor); using var border = new Pen(BorderColor);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
        using var accent = new Pen(AccentColor, Math.Max(2, 3 * DeviceDpi / 96f));
        e.Graphics.DrawLine(accent, 2, 14 * DeviceDpi / 96f, 2, Math.Max(15, Height - 14 * DeviceDpi / 96f));
    }
}

internal enum DeviceGlyphKind { Battery, Keyboard, Mouse, Headset }
internal sealed class DeviceGlyph(DeviceGlyphKind kind) : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Ink { get; set; } = Color.Teal;
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var state = e.Graphics.Save();
        e.Graphics.ScaleTransform(Width / 40f, Height / 40f);
        using var pen = new Pen(Ink, 2) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        switch (kind)
        {
            case DeviceGlyphKind.Keyboard:
                e.Graphics.DrawRectangle(pen, 2, 10, 36, 22);
                for (var y = 15; y <= 23; y += 8)
                    for (var x = 7; x <= 31; x += 8) e.Graphics.DrawLine(pen, x, y, x + 2, y);
                e.Graphics.DrawLine(pen, 12, 28, 28, 28); break;
            case DeviceGlyphKind.Mouse:
                e.Graphics.DrawEllipse(pen, 9, 3, 22, 34); e.Graphics.DrawLine(pen, 20, 4, 20, 17);
                e.Graphics.DrawLine(pen, 11, 17, 29, 17); break;
            case DeviceGlyphKind.Headset:
                e.Graphics.DrawArc(pen, 5, 3, 30, 30, 180, 180);
                e.Graphics.DrawRectangle(pen, 4, 18, 7, 15); e.Graphics.DrawRectangle(pen, 29, 18, 7, 15);
                e.Graphics.DrawLine(pen, 5, 32, 14, 37); e.Graphics.DrawLine(pen, 14, 37, 21, 37); break;
            default:
                e.Graphics.DrawRectangle(pen, 10, 6, 20, 30); e.Graphics.DrawLine(pen, 16, 2, 24, 2);
                e.Graphics.DrawLines(pen, [new Point(22, 11), new Point(16, 22), new Point(24, 20), new Point(18, 31)]); break;
        }
        e.Graphics.Restore(state);
    }
}
