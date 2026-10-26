namespace PeripheralBatteryCompanion.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(true, @"Local\PeripheralBatteryCompanion.Stage3", out var created);
        if (!created) return;
        try
        {
            ApplicationConfiguration.Initialize();
            using var context = new TrayApplicationContext();
            Application.Run(context);
        }
        finally { singleInstance.ReleaseMutex(); }
    }
}
