using System.Drawing;

namespace PeripheralBatteryCompanion.Core.Presentation;

public enum DashboardThemeMode { System, Dark, Light }
public sealed record DashboardPalette(Color Background, Color Surface, Color Border, Color Text, Color Muted,
    Color Accent, Color Track)
{
    public static DashboardPalette Resolve(DashboardThemeMode mode, bool systemDark) =>
        mode == DashboardThemeMode.Dark || mode == DashboardThemeMode.System && systemDark
        ? new(Color.FromArgb(10, 17, 26), Color.FromArgb(17, 28, 42), Color.FromArgb(48, 68, 92),
            Color.FromArgb(238, 244, 252), Color.FromArgb(166, 185, 208), Color.FromArgb(41, 205, 210), Color.FromArgb(35, 50, 70))
        : new(Color.FromArgb(239, 244, 249), Color.White, Color.FromArgb(154, 174, 194),
            Color.FromArgb(20, 34, 51), Color.FromArgb(67, 86, 107), Color.FromArgb(0, 101, 133), Color.FromArgb(222, 231, 240));
}
