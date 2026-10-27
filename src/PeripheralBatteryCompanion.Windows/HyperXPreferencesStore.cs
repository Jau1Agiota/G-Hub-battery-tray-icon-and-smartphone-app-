using PeripheralBatteryCompanion.Core;

namespace PeripheralBatteryCompanion.Windows;

// No protocol data, hardware IDs, or acknowledgement is persisted. Old/missing/corrupt settings default off.
internal sealed class HyperXPreferencesStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PeripheralBatteryCompanion", "hyperx-monitoring.json");
    public HyperXMonitoringPreferences Load()
    {
        try { return new FileInfo(_path).Length <= 1024 ? HyperXMonitoringPreferences.Parse(File.ReadAllText(_path)) : new(); }
        catch (IOException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
    }
    public bool Save(HyperXMonitoringPreferences value)
    {
        var temporary = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(temporary, value.ToJson());
            File.Move(temporary, _path, true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
