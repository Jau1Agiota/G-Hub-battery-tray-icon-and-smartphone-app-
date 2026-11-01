using PeripheralBatteryCompanion.Core.TransportSecurity;
using PeripheralBatteryCompanion.Windows;

namespace PeripheralBatteryCompanion.NativeAcceptance;

internal sealed class CleanupInventory : IDisposable
{
    private readonly Dictionary<string, WindowsAuthorizationDirectory> _pins = new(StringComparer.Ordinal);
    private readonly List<string> _files = [];
    private readonly ExecutionManifest _manifest;
    private CleanupInventory(ExecutionManifest manifest) => _manifest = manifest;
    internal static CleanupInventory Attest(ExecutionManifest manifest)
    {
        if (NativeAdmission.Current.Plan.Id != "cleanup") throw new InvalidOperationException("Cleanup scope.");
        var inventory = new CleanupInventory(manifest);
        try { inventory.Visit(manifest.Root, ""); return inventory; } catch { inventory.Dispose(); throw; }
    }
    private void Visit(string directory, string relative)
    {
        if (_pins.Count >= 24) throw new IOException("Directory budget.");
        _pins.Add(directory, WindowsAuthorizationDirectory.PinPrivateFixture(directory));
        long total = 0;
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (_files.Count + _pins.Count >= 512) throw new IOException("Cleanup inventory bound.");
            string name = Path.GetFileName(entry); string child = relative.Length == 0 ? name : relative + "/" + name;
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Unconfirmed link ownership; retain.");
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                if (!AllowedDirectory(child)) throw new IOException("Unexpected directory; retain."); Visit(entry, child);
            }
            else
            {
                if (!AllowedFile(child)) throw new IOException("Unexpected file; retain.");
                using var stream = _pins[directory].OpenFile(entry, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.None);
                if (stream.Length > 16384 || (total += stream.Length) > 65536) throw new IOException("Cleanup file/byte bound.");
                _files.Add(entry);
            }
        }
    }
    private static bool AllowedDirectory(string name) => name is "clean" or "filesystem" or "secrets" or "faults" or
        "filesystem/acl-broad" or "filesystem/acl-null" or "filesystem/acl-inherited" or "filesystem/target" or
        "faults/write" or "faults/replace" or "faults/flush" or "faults/close" || AcceptancePlan.Phases.Any(p => name == "crash-" + p);
    private static bool AllowedFile(string name)
    {
        if (name is "public-leaf.der" or "identity.receipt" or "filesystem.results.json" or "secrets/fixture-credential.bin") return true;
        int separator = name.LastIndexOf('/');
        if (separator < 0 || !AllowedDirectory(name[..separator])) return false;
        string directory = name[..separator];
        return (directory == "clean" || directory.StartsWith("crash-", StringComparison.Ordinal) || directory.StartsWith("faults/", StringComparison.Ordinal)) &&
            DurableAuthorizationJournal.OwnedFileNames.Contains(name[(separator + 1)..], StringComparer.Ordinal);
    }
    internal void RemoveExact()
    {
        foreach (string file in _files)
        {
            if (!file.StartsWith(_manifest.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Cleanup changed; retain.");
            File.Delete(file); if (File.Exists(file)) throw new IOException("File deletion unconfirmed.");
        }
        foreach (string directory in _pins.Keys.OrderByDescending(p => p.Length).ToArray())
        {
            _pins[directory].Dispose(); _pins.Remove(directory);
            Directory.Delete(directory, recursive: false);
            if (Directory.Exists(directory)) throw new IOException("Directory deletion unconfirmed.");
        }
    }
    public void Dispose() { foreach (var pin in _pins.Values) pin.Dispose(); _pins.Clear(); }
}
