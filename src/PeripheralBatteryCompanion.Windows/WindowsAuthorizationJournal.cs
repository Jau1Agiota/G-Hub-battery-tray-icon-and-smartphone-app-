using PeripheralBatteryCompanion.Core.TransportSecurity;

namespace PeripheralBatteryCompanion.Windows;

/// <summary>Production composition. All construction is held before identity, ACL, file or DPAPI access.</summary>
internal sealed class WindowsAuthorizationJournal : IAuthorizationJournalSession
{
    private readonly WindowsAuthorizationDirectory _directory;
    private readonly DurableAuthorizationJournal _journal;
    private WindowsAuthorizationJournal(WindowsAuthorizationDirectory directory, EnrollmentIdentity identity)
    {
        _directory = directory;
        _journal = new(identity, new AuthorizationJournalFileStorage(directory.Path,
            directory.ValidateFile, directory.Flush, directory.OpenFile), new EnvelopeProtection(identity));
    }
    internal static WindowsAuthorizationJournal OpenExisting(string path, EnrollmentIdentity identity)
    {
        RequireNativeApproval();
        return new(WindowsAuthorizationDirectory.OpenExisting(path), identity);
    }
    internal static WindowsAuthorizationJournal CreateFreshByLocalOwner(string newPath, EnrollmentIdentity identity)
    {
        RequireNativeApproval();
        var result = new WindowsAuthorizationJournal(WindowsAuthorizationDirectory.CreateFresh(newPath), identity);
        if (result._journal.InitializeFreshByLocalOwner()) return result;
        result.Dispose();
        throw new InvalidOperationException("Protected journal initialization did not establish success; evidence retained.");
    }
    internal static EnrollmentAuthorization OpenCoordinatorForLocalReview(string path, EnrollmentIdentity identity)
    {
        RequireNativeApproval();
        var journal = OpenExisting(path, identity);
        try { return new EnrollmentAuthorization(identity, journal); }
        catch { journal.Dispose(); throw; }
    }
    private static void RequireNativeApproval() => throw new InvalidOperationException(
        "GATE E BLOCKED: authorization journal requires native path, ACL, durability and execution-provenance approval.");
    public AuthorizationLoad Read() => _journal.Read();
    public AuthorizationCommitOutcome Commit(long expectedGeneration, AuthorizationRecord next) => _journal.Commit(expectedGeneration, next);
    public void MarkUncertain() => _journal.MarkUncertain();
    public void Dispose() { try { _journal.Dispose(); } finally { _directory.Dispose(); } }

    private sealed class EnvelopeProtection(EnrollmentIdentity identity) : IAuthorizationEnvelopeProtection
    {
        private readonly WindowsCurrentUserRecordProtection _native = new();
        public byte[] Protect(byte[] plaintext) => _native.ProtectJournalEnvelope(plaintext, identity);
        public byte[] Unprotect(byte[] ciphertext) => _native.UnprotectJournalEnvelope(ciphertext, identity);
    }
}
