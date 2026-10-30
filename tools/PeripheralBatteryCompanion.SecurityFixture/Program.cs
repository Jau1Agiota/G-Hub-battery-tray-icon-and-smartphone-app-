using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace PeripheralBatteryCompanion.SecurityFixture;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || !FixtureConsent.ValidArguments(args))
        {
            Console.WriteLine("NOT RUN: Windows and exact --confirm-fixture-creation <nonzero GUID N> required.");
            return 2;
        }
        return Fixture.Run(args[1]);
    }
}

internal static class Fixture
{
    private static readonly Dictionary<string, bool> Results = new(StringComparer.Ordinal);
    private static readonly string[] ProofCases = [
        "ordinary unelevated owner process", "fresh operation resources", "non-reparse fixture ancestor",
        "fixture directory protected current-user ownership", "fixture directory exact user-only DACL",
        "reopened user software P256 nonexportable metadata", "native private export rejection observed (not policy-causality proof)",
        "native nonempty key DACL read", "key owner is current user",
        "reviewed key DACL allow principals only user/SYSTEM/administrators", "preexisting key collision refused",
        "collision preserved exact public key", "leaf absent before store write", "exact CurrentUser My leaf association",
        "certificate private key points to owned user CNG name", "offline exact SAN SPKI leaf fields (not TLS trust)",
        "associated key challenge possession", "mismatched key rejected"
    ];
    private static void Require([DoesNotReturnIf(false)] bool condition, string test)
    {
        Results[test] = condition;
        Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: {test}");
        if (!condition) throw new InvalidOperationException("Fixture assertion failed.");
    }

    internal static int Run(string operation)
    {
        Results.Clear();
        string keyName = "PBC-A22C5-Fixture-" + operation;
        string dns = "fixture-" + operation + ".invalid"; // Offline test SAN only, never production mapping.
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PBC-A22C5-Fixtures");
        string directory = Path.Combine(root, operation);
        CngKey? createdKey = null;
        X509Certificate2? leaf = null;
        FileStream? journal = null;
        byte[]? expectedSpki = null;
        byte[]? expectedDer = null;
        var journalBytes = new List<byte>();
        bool certificatePlanned = false, passed = false, clean = true, creationAttempted = false, operationFailed = false;
        var cleanup = CleanupOutcome.NoCreationAttempted;
        var activeOperation = FixtureOperation.Preflight;
        string? activeProof = null;
        void Begin(FixtureOperation operationId, string? proof = null)
        {
            activeOperation = operationId; activeProof = proof;
            Console.WriteLine("CHECK: operation=" + operationId);
        }
        Console.WriteLine("Operation: " + operation); // Anonymous operation identifier only.
        Console.WriteLine("Scope: CurrentUser software-CNG fixture key, CurrentUser/My leaf, isolated local manifest. No production identity.");
        void Record(object value)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value) + "\n");
            journal!.Write(bytes); journal.Flush(flushToDisk: true); journalBytes.AddRange(bytes);
        }
        bool JournalMatches()
        {
            if (journal is null || journal.Length != journalBytes.Count) return false;
            journal.Position = 0;
            var bytes = new byte[journalBytes.Count];
            journal.ReadExactly(bytes);
            return bytes.AsSpan().SequenceEqual(journalBytes.ToArray());
        }
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User ?? throw new InvalidOperationException();
            Require(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), "ordinary unelevated owner process");
            Require(!Directory.Exists(directory) && !File.Exists(directory) &&
                !CngKey.Exists(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider), "fresh operation resources");
            // Reject reparse ancestors before creating test data. No automatic repair/adoption.
            for (var parent = new DirectoryInfo(directory); parent is not null; parent = parent.Parent)
                if (parent.Exists) Require((parent.Attributes & FileAttributes.ReparsePoint) == 0, "non-reparse fixture ancestor");
            var acl = new DirectorySecurity();
            acl.SetOwner(sid); acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.CreateDirectory(root);
            CreateFreshDirectory(directory, acl);
            var actualAcl = new DirectoryInfo(directory).GetAccessControl();
            Require(actualAcl.AreAccessRulesProtected && actualAcl.GetOwner(typeof(SecurityIdentifier)) == sid,
                "fixture directory protected current-user ownership");
            var rules = actualAcl.GetAccessRules(true, true, typeof(SecurityIdentifier));
            Require(rules.Count == 1 && rules[0] is FileSystemAccessRule rule && rule.IdentityReference == sid &&
                rule.AccessControlType == AccessControlType.Allow && !rule.IsInherited &&
                (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl,
                "fixture directory exact user-only DACL");
            journal = new FileStream(Path.Combine(directory, "ownership.jsonl"), FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough);
            Record(new { version = 1, operation, keyName, provider = "Microsoft Software Key Storage Provider", scope = "CurrentUser", state = "planned" });
            var parameters = new CngKeyCreationParameters {
                Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider, ExportPolicy = CngExportPolicies.None,
                KeyUsage = CngKeyUsages.Signing, KeyCreationOptions = CngKeyCreationOptions.None
            };
            // CreateNew semantics: CNG refuses existing key; NEVER OverwriteExistingKey.
            Begin(FixtureOperation.CreateKey);
            creationAttempted = true;
            cleanup = CleanupOutcome.CreationOutcomeUncertain;
            createdKey = CngKey.Create(CngAlgorithm.ECDsaP256, keyName, parameters);
            cleanup = CleanupOutcome.CreatedCleanupUnconfirmed;
            using var signing = new ECDsaCng(createdKey);
            expectedSpki = signing.ExportSubjectPublicKeyInfo();
            Record(new { state = "key-created", spki = Convert.ToBase64String(expectedSpki) });
            Begin(FixtureOperation.ReopenKey, "reopened user software P256 nonexportable metadata");
            using (var reopened = CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.Silent)) {
                Require(!reopened.IsMachineKey && reopened.Provider == CngProvider.MicrosoftSoftwareKeyStorageProvider &&
                    reopened.AlgorithmGroup == CngAlgorithmGroup.ECDsa && reopened.KeySize == 256 && reopened.ExportPolicy == CngExportPolicies.None,
                    "reopened user software P256 nonexportable metadata");
                Begin(FixtureOperation.ExportEccPrivate, "native private export rejection observed (not policy-causality proof)");
                byte[]? exported = null;
                var observation = ExportObservation.UnexpectedFailure;
                try {
                    exported = reopened.Export(CngKeyBlobFormat.EccPrivateBlob);
                    observation = FixtureDiagnostics.ClassifyExport(returnedBytes: true, hresult: null);
                }
                catch (CryptographicException error) {
                    observation = FixtureDiagnostics.ClassifyExport(returnedBytes: false, hresult: error.HResult);
                    Console.WriteLine("DIAGNOSTIC: " + FixtureDiagnostics.Failure(activeOperation, error));
                }
                finally { if (exported is not null) CryptographicOperations.ZeroMemory(exported); }
                Console.WriteLine("EXPORT OBSERVATION: " + observation + "; export-policy-causality=NOT PROVEN");
                Require(FixtureDiagnostics.IsRejectionObserved(observation), "native private export rejection observed (not policy-causality proof)");
                Begin(FixtureOperation.ReadKeyDacl, "native nonempty key DACL read");
                var descriptor = reopened.GetProperty("Security Descr", (CngPropertyOptions)4).GetValue() ?? throw new InvalidOperationException(); // DACL_SECURITY_INFORMATION
                var security = new RawSecurityDescriptor(descriptor, 0);
                Require(security.DiscretionaryAcl is not null && security.DiscretionaryAcl.Count > 0, "native nonempty key DACL read");
                // Owner is retrieved separately with OWNER_SECURITY_INFORMATION; never infer from file ACL.
                Begin(FixtureOperation.ReadKeyOwner, "key owner is current user");
                var owner = new RawSecurityDescriptor(reopened.GetProperty("Security Descr", (CngPropertyOptions)1).GetValue() ?? throw new InvalidOperationException(), 0).Owner;
                Require(owner == sid, "key owner is current user");
                Begin(FixtureOperation.ReadKeyDacl, "reviewed key DACL allow principals only user/SYSTEM/administrators");
                bool safeAcl = true, userAllowed = false;
                foreach (GenericAce ace in security.DiscretionaryAcl!) {
                    if (ace is not CommonAce entry || entry.IsCallback) { safeAcl = false; continue; }
                    var role = entry.SecurityIdentifier == sid ? "CurrentUser" :
                        entry.SecurityIdentifier.IsWellKnown(WellKnownSidType.LocalSystemSid) ? "SYSTEM" :
                        entry.SecurityIdentifier.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) ? "Administrators" : "Other";
                    Console.WriteLine($"ACL: role={role}; type={entry.AceQualifier}; mask=0x{entry.AccessMask:X}; inherited={entry.IsInherited}");
                    if (entry.AceQualifier == AceQualifier.AccessAllowed && role == "Other") safeAcl = false;
                    if (entry.AceQualifier == AceQualifier.AccessAllowed && role == "CurrentUser") userAllowed = true;
                }
                Console.WriteLine("ACL: control=" + security.ControlFlags);
                Require(safeAcl && userAllowed, "reviewed key DACL allow principals only user/SYSTEM/administrators");
            }
            // Duplicate native create must refuse, without overwrite flag.
            Begin(FixtureOperation.DuplicateKeyCreate, "preexisting key collision refused");
            bool collisionDenied = false;
            try { using var duplicate = CngKey.Create(CngAlgorithm.ECDsaP256, keyName, parameters); }
            catch (CryptographicException error) when (error.HResult == unchecked((int)0x8009000F)) { collisionDenied = true; }
            Require(collisionDenied, "preexisting key collision refused");
            Require(signing.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(expectedSpki), "collision preserved exact public key");
            Begin(FixtureOperation.CreateLeaf);
            var request = new CertificateRequest("CN=PBC isolated fixture", signing, HashAlgorithmName.SHA256);
            var san = new SubjectAlternativeNameBuilder(); san.AddDnsName(dns);
            request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
            var now = DateTimeOffset.UtcNow;
            leaf = request.CreateSelfSigned(now.AddMinutes(-1), now.AddHours(1));
            expectedDer = leaf.RawData;
            Begin(FixtureOperation.OpenCertificateStore);
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
            Require(CountExact(store, leaf.Thumbprint, expectedDer) == 0, "leaf absent before store write");
            Record(new { state = "certificate-planned", thumbprint = leaf.Thumbprint, publicDer = Convert.ToBase64String(expectedDer) });
            certificatePlanned = true;
            Begin(FixtureOperation.AssociateLeaf, "exact CurrentUser My leaf association");
            store.Add(leaf);
            using var selected = FindExact(store, leaf.Thumbprint, expectedDer);
            Require(selected is not null, "exact CurrentUser My leaf association");
            using var privateKey = selected!.GetECDsaPrivateKey();
            Require(privateKey is ECDsaCng associated && associated.Key.KeyName == keyName && !associated.Key.IsMachineKey,
                "certificate private key points to owned user CNG name");
            var pin = ImmutableArray.CreateRange(SHA256.HashData(expectedSpki));
            Require(Core.TransportSecurity.PublicServerCertificateInspector.Inspect(selected, dns, pin, now).Status ==
                Core.TransportSecurity.PublicCertificateStatus.PublicFieldsMatch, "offline exact SAN SPKI leaf fields (not TLS trust)");
            var challenge = RandomNumberGenerator.GetBytes(32);
            try {
                Begin(FixtureOperation.SignChallenge, "associated key challenge possession");
                var signature = privateKey!.SignData(challenge, HashAlgorithmName.SHA256);
                using var publicKey = selected.GetECDsaPublicKey();
                Require(publicKey!.VerifyData(challenge, signature, HashAlgorithmName.SHA256), "associated key challenge possession");
                using var mismatch = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                Require(!mismatch.VerifyData(challenge, signature, HashAlgorithmName.SHA256), "mismatched key rejected");
            } finally { CryptographicOperations.ZeroMemory(challenge); }
            Begin(FixtureOperation.RecordEvidence);
            Record(new { state = "proof-complete" });
            passed = true;
        }
        catch (Exception error) {
            operationFailed = true;
            if (activeProof is not null && (!Results.TryGetValue(activeProof, out var prior) || !prior)) Results[activeProof] = false;
            Console.WriteLine("FAIL: isolated fixture halted; " + FixtureDiagnostics.Failure(activeOperation, error) + ". Details withheld; no automatic adoption/repair.");
        }
        finally
        {
            // Live journal + exact public identity are mandatory. Never prefix-based cleanup or startup recovery.
            try {
                if (createdKey is not null) {
                    Begin(FixtureOperation.CleanupValidateOwnership);
                    if (!JournalMatches() || expectedSpki is null) throw new InvalidOperationException();
                    using var current = CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.Silent);
                    using var currentPublic = new ECDsaCng(current);
                    if (!FixtureConsent.CanDeleteKey(operation, current.KeyName, expectedSpki,
                        currentPublic.ExportSubjectPublicKeyInfo(), JournalMatches())) throw new InvalidOperationException();
                    if (certificatePlanned && leaf is not null && expectedDer is not null) {
                        Begin(FixtureOperation.CleanupRemoveLeaf);
                        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                        store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
                        using var exact = FindExact(store, leaf.Thumbprint, expectedDer);
                        if (exact is not null) store.Remove(exact);
                        if (CountExact(store, leaf.Thumbprint, expectedDer) != 0) throw new InvalidOperationException();
                    }
                    Begin(FixtureOperation.CleanupDeleteKey);
                    current.Delete();
                    if (CngKey.Exists(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider)) throw new InvalidOperationException();
                    Begin(FixtureOperation.CleanupRecordEvidence);
                    Record(new { state = "owned-key-and-leaf-cleaned", certificatePlanned });
                    cleanup = CleanupOutcome.CreatedThenCleaned;
                    Console.WriteLine("PASS: cleanup=" + FixtureDiagnostics.Cleanup(cleanup, certificatePlanned));
                } else if (creationAttempted) { clean = false; Console.WriteLine("FAIL: creation outcome uncertain; review operation orphan locally"); }
                else Console.WriteLine("PASS: no key or certificate creation attempted; any manifest retained locally");
            } catch (Exception error) {
                clean = false;
                Console.WriteLine("FAIL: CLEANUP NOT CONFIRMED; " + FixtureDiagnostics.Failure(activeOperation, error) +
                    "; preserve operation journal for manual review; no broad cleanup");
            }
            leaf?.Dispose(); createdKey?.Dispose(); journal?.Dispose();
        }
        foreach (var test in ProofCases) if (!Results.ContainsKey(test)) Console.WriteLine("NOT RUN: " + test);
        Console.WriteLine($"Proof checks: passed={Results.Count(p => p.Value)}, failed={Results.Count(p => !p.Value)}, not-run={ProofCases.Count(p => !Results.ContainsKey(p))}; operation-errors={(operationFailed ? 1 : 0)}; cleanup={FixtureDiagnostics.Cleanup(cleanup, certificatePlanned)}");
        Console.WriteLine("NOT RUN: cross-user denial, separate-process/session restart, PFX export, DPAPI, TLS, pairing, physical devices");
        Console.WriteLine("LIMIT: ACL evidence does not exclude privileged administrators/SYSTEM or same-user tampering.");
        Console.WriteLine(passed && clean ? "RESULT: PASS (isolated proof only); STOP for review" : "RESULT: FAIL; STOP for review");
        return passed && clean ? 0 : 1;
    }

    // Atomic native create with the DACL already applied: an existing directory is never adopted.
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int InheritHandle; }
    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryNative(string path, ref SecurityAttributes attributes);
    private static void CreateFreshDirectory(string path, DirectorySecurity acl)
    {
        var bytes = acl.GetSecurityDescriptorBinaryForm();
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = pinned.AddrOfPinnedObject() };
            if (!CreateDirectoryNative(path, ref attributes)) throw new Win32Exception(Marshal.GetLastWin32Error());
        } finally { pinned.Free(); }
    }

    private static int CountExact(X509Store store, string thumbprint, byte[] der)
    {
        using var match = FindExact(store, thumbprint, der);
        return match is null ? 0 : 1;
    }
    private static X509Certificate2? FindExact(X509Store store, string thumbprint, byte[] der)
    {
        var all = store.Certificates;
        X509Certificate2? selected = null;
        try {
            foreach (var cert in all) if (cert.Thumbprint == thumbprint) {
                if (!FixtureConsent.CertificateMatchesOwnedIdentity(thumbprint, cert.Thumbprint, der, cert.RawData) ||
                    selected is not null) throw new InvalidOperationException();
                selected = cert;
            }
            return selected;
        } catch { selected?.Dispose(); throw; }
        finally { foreach (var cert in all) if (!ReferenceEquals(cert, selected)) cert.Dispose(); }
    }
}
