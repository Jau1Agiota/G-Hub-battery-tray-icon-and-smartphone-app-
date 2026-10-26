using System.Runtime.InteropServices;

namespace PeripheralBatteryCompanion.Windows;

internal static class BatteryIcon
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon Create(int? percentage)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        var color = percentage switch
        {
            > 50 => Color.LimeGreen, > 20 => Color.Gold, >= 0 => Color.IndianRed, _ => Color.Gray
        };
        using var outline = new Pen(color, 3);
        using var fill = new SolidBrush(color);
        graphics.DrawRectangle(outline, 3, 7, 24, 18);
        graphics.FillRectangle(fill, 28, 12, 3, 8);
        if (percentage is int value)
        {
            // Preserve a visible mark even at zero; the outline's color indicates low battery.
            graphics.FillRectangle(fill, 6, 10, Math.Max(1, 18 * value / 100), 12);
        }
        else
        {
            using var font = new Font(FontFamily.GenericSansSerif, 11, FontStyle.Bold, GraphicsUnit.Pixel);
            graphics.DrawString("?", font, fill, 11, 9);
        }
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally { DestroyIcon(handle); }
    }
}
