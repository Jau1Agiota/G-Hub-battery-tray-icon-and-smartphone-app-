using System.Drawing;

namespace PeripheralBatteryCompanion.Core.Presentation;

public enum DashboardThemeMode { System, Dark, Light }
public sealed record DashboardPalette(Color Background, Color Surface, Color Border, Color Text, Color Muted,
    Color Accent, Color Track)
{
    public static DashboardPalette Resolve(DashboardThemeMode mode, bool systemDark) =>
        mode == DashboardThemeMode.Dark || mode == DashboardThemeMode.System && systemDark
        ? new(Color.FromArgb(9, 18, 30), Color.FromArgb(16, 29, 44), Color.FromArgb(43, 63, 85),
            Color.FromArgb(240, 245, 253), Color.FromArgb(145, 168, 197), Color.FromArgb(9, 167, 244), Color.FromArgb(38, 57, 78))
        : new(Color.FromArgb(239, 244, 249), Color.White, Color.FromArgb(154, 174, 194),
            Color.FromArgb(20, 34, 51), Color.FromArgb(67, 86, 107), Color.FromArgb(0, 101, 133), Color.FromArgb(222, 231, 240));
}
