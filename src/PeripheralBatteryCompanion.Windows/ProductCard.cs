using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PeripheralBatteryCompanion.Core.Presentation;

namespace PeripheralBatteryCompanion.Windows;

internal sealed class ProductCard : Panel
{
    private readonly Bitmap _image;
    private readonly Rectangle _alphaBounds;
    private readonly Color _accent;
    private Color _surface, _edge;
    private bool _light, _highContrast;
    private readonly Control[] _content;
    internal Rectangle ImageBounds => DashboardLayout.ImageViewport(DeviceDpi, Width);
    internal Rectangle SourceBounds => _alphaBounds;

    public ProductCard(string name, string resource, Color accent, Font headingFont, params Control[] content)
    {
        DoubleBuffered = true; ResizeRedraw = true; AutoSize = false; TabStop = false;
        AccessibleRole = AccessibleRole.Grouping; AccessibleName = name;
        _accent = accent;
        using var stream = typeof(ProductCard).Assembly.GetManifestResourceStream("Devices." + resource)
            ?? throw new InvalidOperationException("Required embedded product image is missing: " + resource);
        using var decoded = Image.FromStream(stream);
        _image = new Bitmap(decoded.Width, decoded.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(_image)) g.DrawImageUnscaled(decoded, 0, 0);
        _alphaBounds = FindAlphaBounds(_image);
        var title = new Label { Text = name, AutoEllipsis = true, Font = headingFont, AccessibleName = name,
            UseMnemonic = false, BackColor = Color.Transparent };
        _content = [title, .. content];
        foreach (var control in _content)
        {
            control.AutoSize = false; control.Dock = DockStyle.None; control.Margin = Padding.Empty;
            control.MaximumSize = Size.Empty;
            Controls.Add(control);
        }
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_content is null) return;
        var text = DashboardLayout.TextRegion(DeviceDpi, Width);
        var y = 0;
        for (var i = 0; i < _content.Length; i++)
        {
            var height = RowHeight(i);
            _content[i].Bounds = new(text.X, text.Y + y, text.Width, height);
            y += height + (i == 0 ? DashboardLayout.Scale(2, DeviceDpi) : i < 3 ? DashboardLayout.Scale(6, DeviceDpi) : DashboardLayout.Scale(1, DeviceDpi));
        }
    }
    private int RowHeight(int i)
    {
        var baseline = DashboardLayout.Scale(new[] { 25, 22, 12, 18, 20 }[Math.Min(i, 4)], DeviceDpi);
        return _content[i] is BatteryLevelBar ? baseline : Math.Max(baseline, _content[i].Font.Height + DashboardLayout.Scale(2, DeviceDpi));
    }
    internal int RequiredHeight
    {
        get
        {
            var height = DashboardLayout.Scale(20, DeviceDpi);
            for (var i = 0; i < _content.Length; i++)
            {
                height += RowHeight(i);
                if (i + 1 < _content.Length) height += DashboardLayout.Scale(i == 0 ? 2 : i < 3 ? 6 : 1, DeviceDpi);
            }
            return Math.Max(DashboardLayout.Scale(132, DeviceDpi), height);
        }
    }
    internal void ApplyPalette(DashboardPalette palette, bool highContrast)
    {
        _surface = palette.Surface; _edge = palette.Border;
        _light = palette.Background.GetBrightness() > .5; _highContrast = highContrast;
        BackColor = _surface; Invalidate();
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var background = new SolidBrush(Parent?.BackColor ?? BackColor)) g.FillRectangle(background, ClientRectangle);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(.5f, .5f, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = DashboardDrawing.Rounded(bounds, DashboardLayout.Scale(14, DeviceDpi));
        var saved = g.Save(); g.SetClip(path);
        var tint = _highContrast ? _surface : Blend(_surface, _accent, _light ? .06f : .13f);
        using (var gradient = new LinearGradientBrush(bounds, tint, _surface, 0f)) g.FillPath(gradient, path);
        if (!_highContrast)
        {
            var image = ImageBounds;
            using var halo = new GraphicsPath();
            halo.AddEllipse(image.X - image.Width * .3f, image.Y - image.Height * .4f, image.Width * 1.6f, image.Height * 1.8f);
            using var glow = new PathGradientBrush(halo) { CenterColor = Color.FromArgb(_light ? 25 : 70, _accent),
                SurroundColors = [Color.FromArgb(0, _accent)] };
            var imageSide = new Rectangle(0, 0, image.Right + DashboardLayout.Scale(5, DeviceDpi), Height);
            g.FillRectangle(glow, imageSide);
        }
        using (var strip = new LinearGradientBrush(bounds, _accent, Blend(_accent, _surface, .5f), 90f))
            g.FillRectangle(strip, 0, 0, DashboardLayout.Scale(5, DeviceDpi), Height);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var destination = DashboardLayout.Contain(_alphaBounds, ImageBounds);
        g.DrawImage(_image, destination, _alphaBounds, GraphicsUnit.Pixel);
        g.Restore(saved);
        using var edge = new Pen(_edge); g.DrawPath(edge, path);
    }
    private static Color Blend(Color a, Color b, float ratio) => Color.FromArgb(
        (int)(a.R * (1 - ratio) + b.R * ratio), (int)(a.G * (1 - ratio) + b.G * ratio), (int)(a.B * (1 - ratio) + b.B * ratio));
    private static Rectangle FindAlphaBounds(Bitmap image)
    {
        var data = image.LockBits(new(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[image.Width * 4]; var left = image.Width; var top = image.Height; var right = -1; var bottom = -1;
            for (var y = 0; y < image.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                for (var x = 0; x < image.Width; x++) if (row[x * 4 + 3] != 0)
                { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            }
            if (right < left) throw new InvalidDataException("Product image has no visible pixels.");
            return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }
        finally { image.UnlockBits(data); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) _image.Dispose();
        base.Dispose(disposing);
    }
}
