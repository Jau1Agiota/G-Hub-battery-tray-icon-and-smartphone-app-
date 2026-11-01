using System.Security.Cryptography;
using System.Text;

namespace PeripheralBatteryCompanion.Core.TransportSecurity;

public interface IAuthorizationJournalSession : IAuthorizationJournal, IDisposable
{
    void MarkUncertain();
}

/// <summary>Platform protection, not an encryption bypass or plaintext fallback.</summary>
public interface IAuthorizationEnvelopeProtection
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}

public interface IAuthorizationJournalStorage
{
    IAuthorizationJournalFiles AcquireExclusive();
}

/// <summary>Owned, exclusive directory session. Flush must fail if durability cannot be established.</summary>
public interface IAuthorizationJournalFiles : IDisposable
{
    IReadOnlyList<string> Inventory();
    byte[]? Read(string name, int maximumBytes);
    void CreateNewAndFlush(string name, byte[] bytes);
    void Replace(string temporaryName, string destinationName);
    void DeleteExisting(string name);
    void FlushDirectory();
}

/// <summary>
/// Production transaction/restart protocol. Windows composition supplies guarded DPAPI
/// and a pinned private directory. No network, implicit initialization, retry or recovery.
/// Ordinary file tests cannot attest the native protection/durability implementations.
/// </summary>
public sealed class DurableAuthorizationJournal(EnrollmentIdentity identity, IAuthorizationJournalStorage storage,
    IAuthorizationEnvelopeProtection protection) : IAuthorizationJournalSession
{
    public const int MaximumCiphertextBytes = 16384;
    public const int MaximumPlaintextBytes = AuthorizationRecordCodec.MaxBytes + 64;
    public const int MaximumDirectoryBytes = 65536;
    public static IReadOnlyList<string> OwnedFileNames { get; } = Array.AsReadOnly(new[]
    { "lease.lock", "initialized.bin", "state.bin", "checkpoint.bin", "pending.bin", "session.bin", "stop.bin", "state.next", "checkpoint.next" });
    private readonly object _gate = new();
    private IAuthorizationJournalFiles? _files;
    private AuthorizationRecord? _record;
    private bool _loaded, _poisoned, _disposed, _ownsSession;
    private AuthorizationLoadOutcome _outcome = AuthorizationLoadOutcome.Missing;

    public AuthorizationLoad Read()
    {
        lock (_gate)
        {
            if (_disposed || _poisoned) return new(AuthorizationLoadOutcome.Uncertain, null);
            if (_loaded) return new(_outcome, _record);
            try
            {
                _files ??= storage.AcquireExclusive();
                if (!CleanInventory() || Exists("pending.bin") || Exists("session.bin") || Exists("stop.bin") ||
                    Exists("state.next") || Exists("checkpoint.next")) return Denied();
                byte[]? witness = _files.Read("initialized.bin", 32);
                byte[]? state = _files.Read("state.bin", MaximumCiphertextBytes);
                byte[]? checkpoint = _files.Read("checkpoint.bin", 64);
                if (witness is null && state is null && checkpoint is null)
                { _loaded = true; return new(AuthorizationLoadOutcome.Missing, null); }
                if (witness is null || !witness.AsSpan().SequenceEqual(Binding()) || state is null || checkpoint is null ||
                    !TryRestore(state, checkpoint, out _record)) return Denied();
                _files.CreateNewAndFlush("session.bin", Binding());
                _files.FlushDirectory();
                _ownsSession = true; _loaded = true;
                _outcome = _record is null ? AuthorizationLoadOutcome.VerifiedEmpty : AuthorizationLoadOutcome.VerifiedRecord;
                return new(_outcome, _record);
            }
            catch (Exception error) when (Ordinary(error)) { return Denied(); }
        }
    }

    /// <summary>Only for a separately owner-reviewed NEW directory; never clears evidence or reinitializes missing trust.</summary>
    public bool InitializeFreshByLocalOwner()
    {
        lock (_gate)
        {
            if (_disposed || _poisoned || _loaded) return false;
            try
            {
                _files ??= storage.AcquireExclusive();
                if (_files.Inventory().Any(name => name != "lease.lock")) { _ = Denied(); return false; }
                // Witness first: interruption can never be reclassified as fresh storage.
                _files.CreateNewAndFlush("initialized.bin", Binding());
                _files.FlushDirectory();
                if (!Publish(null, initializing: true)) return false;
                _files.CreateNewAndFlush("session.bin", Binding());
                _files.FlushDirectory();
                _ownsSession = true; _loaded = true; _outcome = AuthorizationLoadOutcome.VerifiedEmpty;
                return true;
            }
            catch (Exception error) when (Ordinary(error)) { _ = Denied(); return false; }
        }
    }

    public AuthorizationCommitOutcome Commit(long expectedGeneration, AuthorizationRecord next)
    {
        lock (_gate)
        {
            if (!_loaded || !_ownsSession || _disposed || _poisoned || next is null)
                return AuthorizationCommitOutcome.Uncertain;
            long current = _record?.Generation ?? 0;
            if (current == long.MaxValue || current != expectedGeneration || next.Generation != current + 1 ||
                !identity.Matches(next.Identity) || !ValidTransition(next)) return AuthorizationCommitOutcome.NotCommitted;
            if (!Publish(next, initializing: false)) return AuthorizationCommitOutcome.Uncertain;
            _record = next; _outcome = AuthorizationLoadOutcome.VerifiedRecord;
            return AuthorizationCommitOutcome.Committed;
        }
    }

    private bool ValidTransition(AuthorizationRecord next)
    {
        if (_record is null) return !next.Revoked;
        if (_record.Revoked) return !next.Revoked && next.EnrollmentId != _record.EnrollmentId;
        return next.Revoked && next.EnrollmentId == _record.EnrollmentId && next.PhoneId == _record.PhoneId &&
            next.TokenSha256 == _record.TokenSha256 && next.TranscriptSha256 == _record.TranscriptSha256 &&
            next.IssuedAt == _record.IssuedAt && next.ExpiresAt == _record.ExpiresAt;
    }

    private bool Publish(AuthorizationRecord? next, bool initializing)
    {
        byte[]? plaintext = null;
        try
        {
            if (_files is null || !CleanInventory()) throw new IOException("Journal inventory rejected.");
            plaintext = Envelope(next);
            byte[] ciphertext = protection.Protect(plaintext);
            if (ciphertext.Length is < 1 or > MaximumCiphertextBytes) throw new IOException("Protected record bound rejected.");
            byte[] checkpoint = Checkpoint(next?.Generation ?? 0, ciphertext);
            _files.CreateNewAndFlush("pending.bin", checkpoint);
            _files.FlushDirectory();
            _files.CreateNewAndFlush("state.next", ciphertext);
            _files.Replace("state.next", "state.bin");
            _files.CreateNewAndFlush("checkpoint.next", checkpoint);
            _files.Replace("checkpoint.next", "checkpoint.bin");
            _files.FlushDirectory();
            if (!_files.Read("state.bin", MaximumCiphertextBytes)!.AsSpan().SequenceEqual(ciphertext) ||
                !_files.Read("checkpoint.bin", 64)!.AsSpan().SequenceEqual(checkpoint)) throw new IOException("Journal readback rejected.");
            _files.DeleteExisting("pending.bin");
            _files.FlushDirectory();
            if (initializing) _record = null;
            return true;
        }
        catch (Exception error) when (Ordinary(error)) { _ = Denied(); return false; }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
    }

    private byte[] Envelope(AuthorizationRecord? record)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write("PBCJNL01"u8); writer.Write(Binding()); writer.Write(record?.Generation ?? 0);
        byte[] payload = record is null ? [] : AuthorizationRecordCodec.Encode(record);
        try { writer.Write(payload.Length); writer.Write(payload); writer.Flush(); return stream.ToArray(); }
        finally { CryptographicOperations.ZeroMemory(payload); }
    }

    private bool TryRestore(byte[] ciphertext, byte[] checkpoint, out AuthorizationRecord? record)
    {
        record = null; byte[]? plaintext = null;
        try
        {
            plaintext = protection.Unprotect(ciphertext);
            if (plaintext.Length is < 52 or > MaximumPlaintextBytes) return false;
            using var stream = new MemoryStream(plaintext, false); using var reader = new BinaryReader(stream);
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("PBCJNL01"u8) || !reader.ReadBytes(32).AsSpan().SequenceEqual(Binding())) return false;
            long generation = reader.ReadInt64(); int length = reader.ReadInt32();
            if (generation < 0 || length < 0 || length > AuthorizationRecordCodec.MaxBytes || stream.Length - stream.Position != length ||
                !checkpoint.AsSpan().SequenceEqual(Checkpoint(generation, ciphertext))) return false;
            if (length == 0) return generation == 0;
            return AuthorizationRecordCodec.TryDecode(reader.ReadBytes(length), identity, out record) &&
                record is not null && record.Generation == generation;
        }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
    }

    private byte[] Binding()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write("PBC-JOURNAL-BINDING-1"); writer.Write(identity.IdentityId.ToString("D"));
        writer.Write(identity.Endpoint); writer.Write(identity.SpkiSha256);
        writer.Write(identity.LeafNotBefore.ToUnixTimeSeconds()); writer.Write(identity.LeafNotAfter.ToUnixTimeSeconds());
        writer.Write(identity.LeafDer.Length); writer.Write(identity.LeafDer.AsSpan()); writer.Flush();
        return SHA256.HashData(stream.ToArray());
    }
    private static byte[] Checkpoint(long generation, byte[] ciphertext)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("PBCCHK01"u8); writer.Write(generation); writer.Write(SHA256.HashData(ciphertext));
        return stream.ToArray();
    }
    private bool CleanInventory()
    {
        var names = _files!.Inventory();
        if (names.Count > OwnedFileNames.Count || names.Distinct(StringComparer.Ordinal).Count() != names.Count ||
            names.Any(name => !OwnedFileNames.Contains(name, StringComparer.Ordinal))) return false;
        int total = 0;
        foreach (string name in names) total = checked(total + (_files.Read(name, MaximumCiphertextBytes)?.Length ?? 0));
        return total <= MaximumDirectoryBytes;
    }
    private bool Exists(string name) => _files!.Read(name, MaximumCiphertextBytes) is not null;
    private AuthorizationLoad Denied()
    {
        _poisoned = true;
        try
        {
            if (_files is not null && !Exists("stop.bin")) { _files.CreateNewAndFlush("stop.bin", "UNCERTAIN"u8.ToArray()); _files.FlushDirectory(); }
        }
        catch (Exception error) when (Ordinary(error)) { /* Existing session/intent markers are never cleared. */ }
        return new(AuthorizationLoadOutcome.Uncertain, null);
    }
    public void MarkUncertain()
    {
        lock (_gate)
        {
            if (_disposed || _poisoned) return;
            try { _files ??= storage.AcquireExclusive(); }
            catch (Exception error) when (Ordinary(error)) { _poisoned = true; return; }
            _ = Denied();
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            bool closeFailed = false;
            try
            {
                if (!_poisoned && _ownsSession) { _files!.FlushDirectory(); _files.DeleteExisting("session.bin"); _files.FlushDirectory(); }
            }
            catch (Exception error) when (Ordinary(error)) { closeFailed = true; _ = Denied(); }
            finally
            {
                try { _files?.Dispose(); }
                catch (Exception error) when (Ordinary(error)) { closeFailed = true; _ = Denied(); }
                _disposed = true;
            }
            if (closeFailed) throw new IOException("Journal close unconfirmed; denial evidence retained where writable.");
        }
    }
    private static bool Ordinary(Exception error) => error is not OutOfMemoryException and not StackOverflowException;
    public override string ToString() => "DurableAuthorizationJournal (redacted; platform proof required)";
}
