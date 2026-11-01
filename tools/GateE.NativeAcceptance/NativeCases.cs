using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using PeripheralBatteryCompanion.Core.TransportSecurity;
using PeripheralBatteryCompanion.Windows;

namespace PeripheralBatteryCompanion.NativeAcceptance;

internal sealed class NativeCases(ExecutionManifest manifest, CasePlan plan)
{
    private string Owned(string name) => manifest.Owned(name);
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Acceptance expectation failed."); }
    internal static void Denies(Action operation)
    {
        try { operation(); }
        catch (NativeRecordProtectionFailure e) when (e.CleanupUnconfirmed) { throw new InvalidOperationException("Native cleanup uncertain."); }
        catch (NativeDirectoryValidationFailure e) when (e.CleanupUnconfirmed) { throw new InvalidOperationException("Descriptor cleanup uncertain."); }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or IOException or ArgumentException) { return; }
        throw new InvalidOperationException("Expected rejection absent.");
    }
    private byte[] ReadOwned(string name, int maximum)
    {
        string path = Owned(name); using var parent = WindowsAuthorizationDirectory.PinPrivateFixture(Path.GetDirectoryName(path)!);
        using var stream = parent.OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.None);
        if (stream.Length > maximum) throw new IOException("Fixture read bound."); byte[] bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
    }
    private void WriteOwned(string name, byte[] bytes)
    {
        string path = Owned(name); using var parent = WindowsAuthorizationDirectory.PinPrivateFixture(Path.GetDirectoryName(path)!);
        using var stream = parent.OpenFile(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, FileOptions.WriteThrough);
        stream.Write(bytes); stream.Flush(true); // Fixture artifact only; production journal still requires directory Flush.
    }
    private EnrollmentIdentity Identity()
    {
        byte[] der = ReadOwned("public-leaf.der", 4096);
        // Public DER parser is platform-backed only inside the admitted native case.
        using var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(der);
        using var key = cert.GetECDsaPublicKey() ?? throw new InvalidOperationException("Fixture key missing.");
        return new(Guid.Parse(manifest.IdentityId), "https://192.168.100.10:8443", Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant(),
            der, ExecutionManifest.Utc(manifest.CertificateNotBeforeUtc), ExecutionManifest.Utc(manifest.CertificateNotAfterUtc));
    }
    internal Verdict Run()
    {
        using var admission = NativeAdmission.Enter(manifest, plan); WindowsAuthorizationDirectory.VerifyFixtureAccount();
        using var runPin = plan.Id is "prepare" or "cleanup" ? null : WindowsAuthorizationDirectory.PinPrivateFixture(manifest.Root);
        switch (plan.Id)
        {
            case "prepare":
                if (Directory.Exists(manifest.Root)) throw new IOException("Run root already exists.");
                using (WindowsAuthorizationDirectory.CreateFresh(manifest.Root)) { }
                return Verdict.Passed;
            case "identity": IdentityCase(); return Verdict.Passed;
            case "protection": ProtectionCase(); return Verdict.Passed;
            case "entropy": EntropyCase(); return Verdict.Passed;
            case "filesystem": return FilesystemCase();
            case "faults": FaultCases(); return Verdict.Passed;
            case "cleanup": Cleanup(); return Verdict.Passed;
            default: JournalCase(); return Verdict.Passed;
        }
    }
    private void IdentityCase()
    {
        byte[] der;
        using (var key = WindowsPersistentIdentityCustody.CreateExactFixture()) der = key.IssueFixtureCertificate();
        WriteOwned("public-leaf.der", der); var identity = Identity();
        using var reopened = WindowsPersistentIdentityCustody.OpenReviewed(identity);
        var signature = new WindowsEnrolledCertificateSignature();
        Require(EnrolledCertificatePolicy.Validate(identity, der, DateTimeOffset.UtcNow, signature));
        byte[] challenge = Encoding.ASCII.GetBytes($"PBC-GATE-E-ACCEPTANCE-1\n{manifest.RunId}\n");
        byte[] signed = reopened.SignFixtureChallenge(challenge);
        Require(signature.Verify(reopened.ExportPublicSpki(), challenge, signed)); challenge[0] ^= 1;
        Require(!signature.Verify(reopened.ExportPublicSpki(), challenge, signed));
        byte[] changed = der.ToArray(); changed[^1] ^= 1;
        var changedIdentity = new EnrollmentIdentity(identity.IdentityId, identity.Endpoint, identity.SpkiSha256, changed, identity.LeafNotBefore, identity.LeafNotAfter);
        Require(!EnrolledCertificatePolicy.Validate(changedIdentity, changed, DateTimeOffset.UtcNow, signature));
        var wrongPin = new EnrollmentIdentity(identity.IdentityId, identity.Endpoint, new string('0', 64), der, identity.LeafNotBefore, identity.LeafNotAfter);
        Require(!EnrolledCertificatePolicy.Validate(wrongPin, der, DateTimeOffset.UtcNow, signature));
        try { using var refused = WindowsPersistentIdentityCustody.OpenReviewed(wrongPin); throw new IOException("Substituted key binding accepted."); }
        catch (InvalidOperationException) { }
        var wrongIp = new EnrollmentIdentity(identity.IdentityId, "https://192.168.100.11:8443", identity.SpkiSha256, der, identity.LeafNotBefore, identity.LeafNotAfter);
        Require(!EnrolledCertificatePolicy.Validate(wrongIp, der, DateTimeOffset.UtcNow, signature));
        Require(!EnrolledCertificatePolicy.Validate(identity, der, identity.LeafNotAfter, signature));
        WriteOwned("identity.receipt", Encoding.ASCII.GetBytes(manifest.IdentityId + "\n" + identity.SpkiSha256 + "\n"));
    }
    private void ProtectionCase()
    {
        var identity = Identity(); var protection = new WindowsCurrentUserRecordProtection(); byte[] input = "synthetic acceptance payload"u8.ToArray();
        byte[] cipher = protection.ProtectJournalEnvelope(input, identity);
        byte[] output = protection.UnprotectJournalEnvelope(cipher, identity);
        try { Require(input.AsSpan().SequenceEqual(output)); } finally { CryptographicOperations.ZeroMemory(output); }
        byte[] changed = cipher.ToArray(); changed[^1] ^= 1; Denies(() => protection.UnprotectJournalEnvelope(changed, identity));
        var wrong = new EnrollmentIdentity(identity.IdentityId, "https://192.168.100.11:8443", identity.SpkiSha256, identity.LeafDer.AsSpan(), identity.LeafNotBefore, identity.LeafNotAfter);
        Denies(() => protection.UnprotectJournalEnvelope(cipher, wrong));
        var wrongId = new EnrollmentIdentity(Guid.Parse(manifest.RunId), identity.Endpoint, identity.SpkiSha256, identity.LeafDer.AsSpan(), identity.LeafNotBefore, identity.LeafNotAfter);
        Denies(() => protection.UnprotectJournalEnvelope(cipher, wrongId));
        byte[] substitutedLeaf = identity.LeafDer.ToArray(); substitutedLeaf[^1] ^= 1;
        var wrongLeaf = new EnrollmentIdentity(identity.IdentityId, identity.Endpoint, identity.SpkiSha256, substitutedLeaf, identity.LeafNotBefore, identity.LeafNotAfter);
        Denies(() => protection.UnprotectJournalEnvelope(cipher, wrongLeaf));
        Denies(() => protection.ProtectJournalEnvelope([], identity));
        Denies(() => protection.ProtectJournalEnvelope(new byte[DurableAuthorizationJournal.MaximumPlaintextBytes + 1], identity));
        byte[] record = protection.Protect(Record(identity));
        Require(protection.Unprotect(record, identity).Generation == 1);
    }
    private sealed class Delivery : IEphemeralBootstrapDelivery { public void DeliverInMemory(ReadOnlySpan<byte> secret) { if (secret.Length != 32) throw new InvalidOperationException(); } }
    private sealed class SecretStore(NativeCases owner, EnrollmentIdentity identity) : IProtectedProtocolSecretStore
    {
        public void Store(ReadOnlySpan<byte> secret)
        {
            byte[] raw = secret.ToArray();
            try { owner.WriteOwned("secrets/fixture-credential.bin", new WindowsCurrentUserRecordProtection().ProtectJournalEnvelope(raw, identity)); }
            finally { CryptographicOperations.ZeroMemory(raw); }
        }
    }
    private void EntropyCase()
    {
        using (WindowsAuthorizationDirectory.CreateFresh(Owned("secrets"))) { }
        var source = new WindowsProtocolEntropy(); var nonces = new HashSet<string>();
        for (int i = 0; i < 8; i++) { string nonce = source.NewRequestNonce(); Require(nonce.Length == 43 && nonces.Add(nonce)); }
        Require(source.NewBootstrapVerifier(new Delivery()).Length == 32);
        Require(source.NewCredentialVerifier(new SecretStore(this, Identity())).Length == 32);
    }
    private Verdict FilesystemCase()
    {
        string root = Owned("filesystem"); using (WindowsAuthorizationDirectory.CreateFresh(root)) { }
        using var rootPin = WindowsAuthorizationDirectory.PinPrivateFixture(root);
        foreach (string bad in new[] { "relative", @"\\invalid\share", root + ":ads", root + "\\..\\other" }) Require(!WindowsAuthorizationDirectory.ValidPathSyntax(bad));
        foreach (string kind in new[] { "broad", "null", "inherited" })
        {
            string badAcl = Owned("filesystem/acl-" + kind); using (WindowsAuthorizationDirectory.CreateFresh(badAcl)) { }
            using var fixturePin = WindowsAuthorizationDirectory.PinPrivateFixture(badAcl);
            var descriptor = new DirectorySecurity();
            descriptor.SetSecurityDescriptorSddlForm($"O:{manifest.AccountSid}D:P(A;OICI;FA;;;{manifest.AccountSid})");
            if (kind == "broad") descriptor.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-1-0"), FileSystemRights.Read, AccessControlType.Allow));
            if (kind == "null") descriptor.SetSecurityDescriptorSddlForm($"O:{manifest.AccountSid}D:NO_ACCESS_CONTROL");
            if (kind == "inherited") descriptor.SetAccessRuleProtection(false, false);
            new DirectoryInfo(badAcl).SetAccessControl(descriptor);
            Denies(() => { using var refused = WindowsAuthorizationDirectory.OpenExisting(badAcl); });
            var restore = new DirectorySecurity(); restore.SetSecurityDescriptorSddlForm($"O:{manifest.AccountSid}D:P(A;OICI;FA;;;{manifest.AccountSid})");
            new DirectoryInfo(badAcl).SetAccessControl(restore); using (WindowsAuthorizationDirectory.OpenExisting(badAcl)) { }
        }
        using (WindowsAuthorizationDirectory.CreateFresh(Owned("filesystem/target"))) { }
        using (var parent = WindowsAuthorizationDirectory.OpenExisting(root))
        using (var file = parent.OpenFile(Owned("filesystem/target.bin"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, FileOptions.WriteThrough)) file.Flush(true);
        WindowsAuthorizationDirectory.CreateFixtureHardLink(Owned("filesystem/alias.bin"), Owned("filesystem/target.bin"));
        using (var parent = WindowsAuthorizationDirectory.OpenExisting(root))
            Denies(() => { using var file = parent.OpenFile(Owned("filesystem/target.bin"), FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.None); });
        File.Delete(Owned("filesystem/alias.bin")); File.Delete(Owned("filesystem/target.bin"));
        bool symlink = false;
        try { Directory.CreateSymbolicLink(Owned("filesystem/link"), Owned("filesystem/target")); symlink = true; }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { if (new DirectoryInfo(Owned("filesystem/link")).LinkTarget is not null) throw; }
        if (symlink)
        {
            Denies(() => { using var refused = WindowsAuthorizationDirectory.OpenExisting(Owned("filesystem/link")); });
            Require(new DirectoryInfo(Owned("filesystem/link")).LinkTarget == Owned("filesystem/target"));
            Directory.Delete(Owned("filesystem/link"), false);
        }
        WriteOwned("filesystem.results.json", Encoding.ASCII.GetBytes(symlink ?
            "{\"acl\":\"Passed\",\"hardlink\":\"Passed\",\"symlink\":\"Passed\",\"foreignOwner\":\"NotRun\"}" :
            "{\"acl\":\"Passed\",\"hardlink\":\"Passed\",\"symlink\":\"NotRun\",\"foreignOwner\":\"NotRun\"}"));
        // No account provisioning, owner takeover or privilege escalation is part of this manifest.
        return Verdict.NotRun;
    }
    private sealed class Protection(EnrollmentIdentity identity) : IAuthorizationEnvelopeProtection
    {
        public byte[] Protect(byte[] plaintext) => new WindowsCurrentUserRecordProtection().ProtectJournalEnvelope(plaintext, identity);
        public byte[] Unprotect(byte[] ciphertext) => new WindowsCurrentUserRecordProtection().UnprotectJournalEnvelope(ciphertext, identity);
    }
    private (WindowsAuthorizationDirectory Directory, DurableAuthorizationJournal Journal, JournalSeams Seams) Journal(string relative, bool fresh, Action<string>? phase = null, string? fault = null)
    {
        string path = Owned(relative); var directory = fresh ? WindowsAuthorizationDirectory.CreateFresh(path) : WindowsAuthorizationDirectory.OpenExisting(path);
        try
        {
            var storage = new AuthorizationJournalFileStorage(path, directory.ValidateFile, directory.Flush, directory.OpenFile);
            var seams = new JournalSeams(storage, phase ?? (_ => { }), fault);
            var journal = new DurableAuthorizationJournal(Identity(), seams, new Protection(Identity()));
            return (directory, journal, seams);
        }
        catch { directory.Dispose(); throw; }
    }
    private AuthorizationRecord Record(EnrollmentIdentity identity) => new(identity, Guid.Parse(manifest.RunId), Guid.Parse("33333333-3333-4333-8333-333333333333"),
        new string('a', 64), new string('b', 64), 1, ExecutionManifest.Utc(manifest.NotBeforeUtc), identity.LeafNotAfter, false);
    private void JournalCase()
    {
        bool fresh = plan.Id == "clean-write" || plan.CrashPhase is not null;
        bool armed = plan.CrashPhase == "witness";
        void Phase(string phase)
        {
            if (!armed || phase != plan.CrashPhase) return;
            Console.WriteLine("READY:" + phase); Console.Out.Flush();
            using var wait = new ManualResetEventSlim(false); wait.Wait(); // Parent must observe phase then terminate ONLY this retained process.
        }
        var pair = Journal(plan.Directory, fresh, Phase);
        using var directory = pair.Directory; using var journal = pair.Journal;
        if (fresh)
        {
            Require(journal.InitializeFreshByLocalOwner());
            armed = true;
            Require(journal.Commit(0, Record(Identity())) == AuthorizationCommitOutcome.Committed);
            var history = journal.ReadReplay(1)!;
            Require(history.TryAdvance(Guid.Parse(manifest.RunId), 3, SHA256.HashData("public fixture publication"u8), SHA256.HashData("public fixture nonce"u8), out var next, out _));
            Require(journal.CommitReplay(1, history.Version, next!) == AuthorizationCommitOutcome.Committed);
            return;
        }
        var loaded = journal.Read();
        if (plan.Id.StartsWith("read-", StringComparison.Ordinal) && plan.Id != "read-session-removed")
        { Require(loaded.Outcome == AuthorizationLoadOutcome.Uncertain); return; }
        Require(loaded.Outcome == AuthorizationLoadOutcome.VerifiedRecord && loaded.Record is not null);
        if (plan.Id == "retire") Require(journal.Commit(1, loaded.Record!.Tombstone()) == AuthorizationCommitOutcome.Committed);
        else if (plan.Id == "retired-read") Require(loaded.Record!.Revoked);
        else Require(!loaded.Record!.Revoked && journal.ReadReplay(1)?.Version == 1);
    }
    private void FaultCases()
    {
        using (WindowsAuthorizationDirectory.CreateFresh(Owned("faults"))) { }
        foreach (string fault in new[] { "write", "replace", "flush", "close" })
        {
            var pair = Journal("faults/" + fault, true, fault: fault); using var directory = pair.Directory;
            try
            {
                bool success = pair.Journal.InitializeFreshByLocalOwner();
                if (fault != "close") Require(pair.Seams.InjectionFired && !success && pair.Journal.Read().Outcome == AuthorizationLoadOutcome.Uncertain);
                else Require(success);
            }
            finally
            {
                if (fault == "close") { Denies(pair.Journal.Dispose); Require(pair.Seams.InjectionFired); } else pair.Journal.Dispose();
            }
        }
    }
    private void Cleanup()
    {
        // Intentional retention if any earlier case failed/not-run/uncertain. Parent never dispatches here then.
        var identity = Identity();
        Require(Encoding.ASCII.GetString(ReadOwned("identity.receipt", 128)) == manifest.IdentityId + "\n" + identity.SpkiSha256 + "\n");
        using var inventory = CleanupInventory.Attest(manifest); // Attest EVERY name/handle before any deletion, including key deletion.
        using (var key = WindowsPersistentIdentityCustody.OpenReviewed(identity)) key.DeleteConfirmedFixture(identity);
        inventory.RemoveExact();
    }
}
