using PeripheralBatteryCompanion.Core;

namespace PeripheralBatteryCompanion.Windows;

// Versioned user choice plus durable safety marker; no protocol payload, device identifier or session consent is persisted.
internal sealed class HyperXPreferencesStore : IHyperXStartupStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PeripheralBatteryCompanion", "hyperx-monitoring.json");
    private string Marker => _path + ".safety-stop";
    public HyperXStartupPreferences Load()
    {
        try
        {
            var value = !File.Exists(_path) || new FileInfo(_path).Length > 4096 ? new HyperXStartupPreferences() : HyperXStartupPreferences.Parse(File.ReadAllText(_path));
            return value with { SafetyLatched = value.SafetyLatched || File.Exists(Marker) };
        }
        catch (IOException) { return new(SafetyLatched: true); }
        catch (UnauthorizedAccessException) { return new(SafetyLatched: true); }
    }
    public bool Save(HyperXStartupPreferences value)
    {
        var temporary = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            if (value.SafetyLatched) File.WriteAllText(Marker, "Review required: native operation may not have completed.");
            File.WriteAllText(temporary, value.ToJson());
            File.Move(temporary, _path, true);
            if (!value.SafetyLatched) File.Delete(Marker);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
