using PeripheralBatteryCompanion.Core.Presentation;

namespace PeripheralBatteryCompanion.Windows;

// Separate from HyperX preferences: theme changes cannot reset consent, mode or cadence.
internal sealed class DashboardThemeStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PeripheralBatteryCompanion", "dashboard-theme.txt");
    public DashboardThemeMode Load()
    {
        try
        {
            return new FileInfo(_path).Length <= 20 && Enum.TryParse<DashboardThemeMode>(File.ReadAllText(_path), out var mode) && Enum.IsDefined(mode)
                ? mode : DashboardThemeMode.System;
        }
        catch (IOException) { return DashboardThemeMode.System; }
        catch (UnauthorizedAccessException) { return DashboardThemeMode.System; }
    }
    public bool Save(DashboardThemeMode mode)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); File.WriteAllText(_path, mode.ToString()); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
