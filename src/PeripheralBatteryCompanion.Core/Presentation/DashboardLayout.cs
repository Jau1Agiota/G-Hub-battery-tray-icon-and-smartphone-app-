using System.Drawing;

namespace PeripheralBatteryCompanion.Core.Presentation;

public static class DashboardLayout
{
    public const int Width = 520, Height = 570;
    public static int Scale(int value, int dpi) => checked((int)Math.Round(value * dpi / 96d));
    public static Size NominalSize(int dpi) => new(Scale(Width, dpi), Scale(Height, dpi));
    public static Rectangle Header(int dpi, int width) => new(Scale(16, dpi), Scale(16, dpi), Math.Max(1, width - Scale(32, dpi)), Scale(64, dpi));
    public static Rectangle Card(int index, int dpi, int width)
    {
        if (index is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(index));
        return new(Scale(16, dpi), Scale(90 + index * 142, dpi), Math.Max(1, width - Scale(32, dpi)), Scale(132, dpi));
    }
    public static Rectangle Footer(int dpi, int width) => new(Scale(16, dpi), Scale(516, dpi), Math.Max(1, width - Scale(32, dpi)), Scale(44, dpi));
    public static Rectangle ImageViewport(int dpi, int cardWidth) => new(Scale(12, dpi), Scale(10, dpi),
        Math.Min(Scale(180, dpi), Math.Max(1, (int)(cardWidth * .37))), Scale(112, dpi));
    public static Rectangle TextRegion(int dpi, int cardWidth)
    {
        var image = ImageViewport(dpi, cardWidth); var x = image.Right + Scale(12, dpi);
        return new(x, Scale(10, dpi), Math.Max(1, cardWidth - x - Scale(12, dpi)), Scale(112, dpi));
    }
    public static RectangleF Contain(RectangleF source, RectangleF viewport)
    {
        if (source.Width <= 0 || source.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0) return RectangleF.Empty;
        var ratio = Math.Min(viewport.Width / source.Width, viewport.Height / source.Height);
        var size = new SizeF(source.Width * ratio, source.Height * ratio);
        return new(viewport.X + (viewport.Width - size.Width) / 2, viewport.Y + (viewport.Height - size.Height) / 2, size.Width, size.Height);
    }
}
