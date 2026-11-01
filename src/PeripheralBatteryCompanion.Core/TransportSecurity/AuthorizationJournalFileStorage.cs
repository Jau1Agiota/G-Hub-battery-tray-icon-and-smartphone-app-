using Microsoft.Win32.SafeHandles;

namespace PeripheralBatteryCompanion.Core.TransportSecurity;

/// <summary>
/// Real bounded file/rename implementation. Handle validation and directory flush
/// are mandatory platform services, NOT booleans claiming an approved directory.
/// Windows production supplies its held, pinned-directory implementation.
/// </summary>
public sealed class AuthorizationJournalFileStorage(string directory,
    Action<SafeFileHandle, string> validateHandle, Action flushDirectory,
    Func<string, FileMode, FileAccess, FileShare, FileOptions, FileStream> openFile) : IAuthorizationJournalStorage
{
    public IAuthorizationJournalFiles AcquireExclusive() => new Session(directory, validateHandle, flushDirectory, openFile);

    private sealed class Session : IAuthorizationJournalFiles
    {
        private readonly string _directory;
        private readonly Action<SafeFileHandle, string> _validate;
        private readonly Action _flush;
        private readonly Func<string, FileMode, FileAccess, FileShare, FileOptions, FileStream> _open;
        private readonly FileStream _lease;
        private bool _closed;
        public Session(string directory, Action<SafeFileHandle, string> validate, Action flush,
            Func<string, FileMode, FileAccess, FileShare, FileOptions, FileStream> open)
        {
            ArgumentNullException.ThrowIfNull(validate); ArgumentNullException.ThrowIfNull(flush); ArgumentNullException.ThrowIfNull(open);
            if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)) throw new IOException("Journal root unavailable.");
            _directory = Path.GetFullPath(directory); _validate = validate; _flush = flush; _open = open;
            _lease = _open(Name("lease.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, FileOptions.WriteThrough);
            try { _validate(_lease.SafeFileHandle, Name("lease.lock")); if (_lease.Length != 0) throw new IOException("Lease content rejected."); }
            catch { _lease.Dispose(); throw; }
        }
        private string Name(string name)
        {
            if (!DurableAuthorizationJournal.OwnedFileNames.Contains(name, StringComparer.Ordinal)) throw new IOException("Journal name rejected.");
            return Path.Join(_directory, name);
        }
        private void Check() => ObjectDisposedException.ThrowIf(_closed, this);
        public IReadOnlyList<string> Inventory()
        {
            Check();
            // Stop enumeration before an unbounded list or unknown path can be materialized.
            var result = new List<string>();
            foreach (string path in Directory.EnumerateFileSystemEntries(_directory))
            {
                if (result.Count >= DurableAuthorizationJournal.OwnedFileNames.Count) throw new IOException("Journal inventory bound exceeded.");
                string name = Path.GetFileName(path); _ = Name(name); result.Add(name);
            }
            return result;
        }
        public byte[]? Read(string name, int maximumBytes)
        {
            Check(); string path = Name(name);
            if (maximumBytes is < 0 or > DurableAuthorizationJournal.MaximumCiphertextBytes) throw new IOException("Read bound rejected.");
            if (name == "lease.lock") return [];
            try
            {
                using var stream = _open(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.None);
                _validate(stream.SafeFileHandle, path);
                if (stream.Length > maximumBytes) throw new IOException("Journal file bound exceeded.");
                byte[] bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
                if (stream.ReadByte() != -1) throw new IOException("Journal file changed during read.");
                return bytes;
            }
            catch (FileNotFoundException) { return null; }
        }
        public void CreateNewAndFlush(string name, byte[] bytes)
        {
            Check();
            if (name == "lease.lock" || bytes.Length > DurableAuthorizationJournal.MaximumCiphertextBytes) throw new IOException("Write bound rejected.");
            using var stream = _open(Name(name), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, FileOptions.WriteThrough);
            _validate(stream.SafeFileHandle, Name(name));
            stream.Write(bytes); stream.Flush(flushToDisk: true);
        }
        public void Replace(string temporaryName, string destinationName)
        {
            Check();
            if (!((temporaryName == "state.next" && destinationName == "state.bin") ||
                (temporaryName == "checkpoint.next" && destinationName == "checkpoint.bin"))) throw new IOException("Replacement rejected.");
            _ = Read(temporaryName, DurableAuthorizationJournal.MaximumCiphertextBytes) ?? throw new IOException("Temporary file missing.");
            _ = Read(destinationName, DurableAuthorizationJournal.MaximumCiphertextBytes);
            File.Move(Name(temporaryName), Name(destinationName), overwrite: true);
        }
        public void DeleteExisting(string name)
        {
            Check();
            if (name is not ("pending.bin" or "session.bin")) throw new IOException("Evidence deletion rejected.");
            _ = Read(name, DurableAuthorizationJournal.MaximumCiphertextBytes) ?? throw new IOException("Owned marker missing.");
            File.Delete(Name(name));
        }
        public void FlushDirectory() { Check(); _flush(); }
        public void Dispose()
        {
            if (_closed) return;
            try { _lease.Dispose(); }
            catch
            {
                // Best effort only. Failure is still reported; never call it a clean close.
                try { CreateNewAndFlush("stop.bin", "UNCERTAIN"u8.ToArray()); _flush(); } catch { }
                throw;
            }
            finally { _closed = true; }
        }
    }
}
