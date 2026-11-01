using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeripheralBatteryCompanion.NativeAcceptance;

internal enum Verdict { Passed, Failed, NotRun, Uncertain }
internal sealed record CasePlan(string Id, string Directory, int Dpapi = 0, int Signatures = 0, int Verifications = 0, string? CrashPhase = null);
internal static class AcceptancePlan
{
    internal static IReadOnlyList<string> RequiredArtifacts { get; } = Array.AsReadOnly(new[]
    { "GateE.NativeAcceptance.exe", "GateE.NativeAcceptance.dll", "GateE.NativeAcceptance.deps.json", "GateE.NativeAcceptance.runtimeconfig.json", "PeripheralBatteryCompanion.Core.dll",
      "PeripheralBatteryCompanion.HyperXHelperProtocol.dll", "Microsoft.Data.Sqlite.dll", "SQLitePCLRaw.batteries_v2.dll", "SQLitePCLRaw.core.dll", "SQLitePCLRaw.provider.e_sqlite3.dll", "runtimes/win-x64/native/e_sqlite3.dll" });
    internal static readonly string[] Phases = ["witness", "pending", "state", "checkpoint", "intent-removed", "session-removed"];
    internal static readonly CasePlan[] Cases = Build();
    private static CasePlan[] Build()
    {
        var cases = new List<CasePlan>
        {
            new("prepare", ""), new("identity", "", 0, 8, 16), new("protection", "", 12), new("entropy", "", 2),
            new("filesystem", "filesystem"), new("clean-write", "clean", 4), new("clean-read", "clean", 2),
            new("retire", "clean", 4), new("retired-read", "clean", 2)
        };
        foreach (string phase in Phases)
        { cases.Add(new("crash-" + phase, "crash-" + phase, 6, CrashPhase: phase)); cases.Add(new("read-" + phase, "crash-" + phase, 2)); }
        cases.Add(new("faults", "faults", 32)); cases.Add(new("cleanup", ""));
        return cases.ToArray();
    }
    internal static CasePlan Find(string id) => Cases.Single(c => c.Id == id);
}

// A resource/provenance description, NEVER a bearer permit. CLI admission is independently blocked.
internal sealed record ExecutionManifest(int Schema, string RunId, string IdentityId, string AccountSid, string LocalAppData,
    string Executable, string ReviewedCommit, Dictionary<string, string> Artifacts, string NotBeforeUtc, string ExpiresUtc,
    string CertificateNotBeforeUtc, string CertificateNotAfterUtc)
{
    internal string Root => Path.Join(LocalAppData, "PBC.GateE.Acceptance", RunId);
    internal string KeyName => "PBC.Identity." + IdentityId;
    internal static ExecutionManifest Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 1 or > 16384) throw new InvalidDataException("Manifest bound.");
        using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 4 });
        RejectDuplicates(document.RootElement);
        var value = JsonSerializer.Deserialize<ExecutionManifest>(bytes, new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Manifest missing.");
        value.Validate(); return value;
    }
    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        { if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate field."); RejectDuplicates(property.Value); }
    }
    internal static DateTimeOffset Utc(string value)
    {
        if (!DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)) throw new InvalidDataException("UTC field.");
        return parsed;
    }
    internal void Validate()
    {
        if (Schema != 1 || !Guid.TryParseExact(RunId, "D", out var run) || run == Guid.Empty || run.ToString("D") != RunId ||
            !Guid.TryParseExact(IdentityId, "D", out var identity) || identity == Guid.Empty || identity.ToString("D") != IdentityId || run == identity ||
            AccountSid is null || !System.Text.RegularExpressions.Regex.IsMatch(AccountSid, @"\AS-1-5-21-(?:[0-9]{1,10}-){2}[0-9]{1,10}-[0-9]{1,10}\z") ||
            !Windows.WindowsAuthorizationDirectory.ValidPathSyntax(LocalAppData) || !Windows.WindowsAuthorizationDirectory.ValidPathSyntax(Root) || Root.Length > 180 ||
            !Windows.WindowsAuthorizationDirectory.ValidPathSyntax(Executable) || Path.GetExtension(Executable) != ".exe" ||
            ReviewedCommit is not { Length: 40 } || !Hex(ReviewedCommit) || Artifacts is null || Artifacts.Count is < 4 or > 16 ||
            Path.GetFileName(Executable) != "GateE.NativeAcceptance.exe" ||
            !Artifacts.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(AcceptancePlan.RequiredArtifacts) ||
            Artifacts.Any(p => p.Value is null || p.Value.Length != 64 || !Hex(p.Value)))
            throw new InvalidDataException("Manifest scope.");
        var start = Utc(NotBeforeUtc); var end = Utc(ExpiresUtc); var before = Utc(CertificateNotBeforeUtc); var after = Utc(CertificateNotAfterUtc);
        if (end <= start || end - start > TimeSpan.FromHours(1) || before > start || after <= end || after - before > TimeSpan.FromHours(24))
            throw new InvalidDataException("Manifest lifetime.");
        if (AcceptancePlan.Cases.Length > 24 || AcceptancePlan.Cases.Sum(c => c.Dpapi) > 128 ||
            AcceptancePlan.Cases.Sum(c => c.Signatures) > 8 || AcceptancePlan.Cases.Sum(c => c.Verifications) > 16) throw new InvalidDataException("Plan budget.");
    }
    private static bool Hex(string value) => value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal string Owned(string relative)
    {
        if (relative.Length > 128 || Path.IsPathRooted(relative) || relative.Split('/', '\\').Any(p => p.Length == 0 || p is "." or ".." || p.Contains(':')))
            throw new InvalidDataException("Owned path.");
        string path = Path.GetFullPath(Path.Join(Root, relative));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException("Escape.");
        return path;
    }
    internal IDisposable PinArtifacts(Func<FileStream, string, IDisposable?>? validate = null)
    {
        var pins = new List<IDisposable>();
        try
        {
            ValidateArtifactInventory(Path.GetDirectoryName(Executable)!, "", new int[1]);
            foreach (var artifact in Artifacts)
            {
                string path = Path.GetFullPath(Path.Join(Path.GetDirectoryName(Executable)!, artifact.Key));
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Artifact link.");
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); pins.Add(stream);
                if (validate?.Invoke(stream, path) is { } directoryPin) pins.Add(directoryPin);
                if (stream.Length > 64 * 1024 * 1024 || Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() != artifact.Value)
                    throw new InvalidDataException("Artifact mismatch.");
            }
            return new ArtifactPins(pins);
        }
        catch { foreach (var pin in pins) pin.Dispose(); throw; }
    }
    private static void ValidateArtifactInventory(string root, string relative, int[] count)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(Path.Join(root, relative)))
        {
            if (++count[0] > 32 || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Artifact inventory bound/link.");
            string name = relative.Length == 0 ? Path.GetFileName(path) : relative + "/" + Path.GetFileName(path);
            if (Directory.Exists(path))
            {
                if (!AcceptancePlan.RequiredArtifacts.Any(n => n.StartsWith(name + "/", StringComparison.Ordinal))) throw new InvalidDataException("Unexpected artifact directory.");
                ValidateArtifactInventory(root, name, count);
            }
            else if (!AcceptancePlan.RequiredArtifacts.Contains(name, StringComparer.Ordinal) && name is not
                ("execution-manifest.json" or "GateE.NativeAcceptance.pdb" or "PeripheralBatteryCompanion.Core.pdb" or "PeripheralBatteryCompanion.HyperXHelperProtocol.pdb"))
                throw new InvalidDataException("Unexpected artifact file.");
        }
    }
    internal void VerifyArtifacts() { using var pins = PinArtifacts(); }
    private sealed class ArtifactPins(List<IDisposable> streams) : IDisposable { public void Dispose() { foreach (var stream in streams) stream.Dispose(); } }
}

internal sealed class OperationBudget(CasePlan plan)
{
    private int _dpapi, _signatures, _verifications, _files, _nonces, _bootstrap, _credentials, _keys;
    internal void Charge(string operation)
    {
        int remaining = operation switch
        {
            "dpapi" => plan.Dpapi - Interlocked.Increment(ref _dpapi),
            "sign" => plan.Signatures - Interlocked.Increment(ref _signatures),
            "verify" => plan.Verifications - Interlocked.Increment(ref _verifications),
            "file" => 20000 - Interlocked.Increment(ref _files),
            "nonce" => 8 - Interlocked.Increment(ref _nonces),
            "bootstrap" => 1 - Interlocked.Increment(ref _bootstrap),
            "credential" => 1 - Interlocked.Increment(ref _credentials),
            "key" => 1 - Interlocked.Increment(ref _keys),
            _ => throw new InvalidOperationException("Unknown operation.")
        };
        if (remaining < 0) throw new InvalidOperationException("Operation budget exhausted.");
    }
}

internal static class HarnessExecutionHold
{
    internal static void Demand() => throw new InvalidOperationException("NATIVE ACCEPTANCE BLOCKED: no execution authorized; frozen resource manifest and revision require owner review.");
}

internal static class NativeAdmission
{
    [ThreadStatic] private static Scope? _scope;
    internal sealed class Scope : IDisposable
    {
        internal ExecutionManifest Manifest { get; }
        internal CasePlan Plan { get; }
        internal OperationBudget Budget { get; }
        private readonly long _started = Environment.TickCount64;
        private DateTimeOffset _lastUtc = DateTimeOffset.UtcNow;
        internal Scope(ExecutionManifest manifest, CasePlan plan) { Manifest = manifest; Plan = plan; Budget = new(plan); }
        internal void Check()
        {
            var now = DateTimeOffset.UtcNow;
            if (Environment.TickCount64 - _started is < 0 or >= 30000 || now < _lastUtc || now < ExecutionManifest.Utc(Manifest.NotBeforeUtc) ||
                now >= ExecutionManifest.Utc(Manifest.ExpiresUtc)) throw new InvalidOperationException("Deadline.");
            _lastUtc = now;
        }
        public void Dispose() { if (ReferenceEquals(_scope, this)) _scope = null; }
    }
    internal static Scope Enter(ExecutionManifest manifest, CasePlan plan)
    {
        HarnessExecutionHold.Demand(); // Remains unconditional; no flag, token, reflection or environment admission.
        if (_scope is not null) throw new InvalidOperationException("Nested admission.");
        return _scope = new(manifest, plan);
    }
    internal static Scope Current => _scope ?? throw new InvalidOperationException("Isolated case admission absent.");
    internal static void Demand(string primitive, string operation = "")
    {
        var scope = Current; scope.Check();
        switch (primitive)
        {
            case "directory": scope.Budget.Charge("file"); break;
            case "dpapi": scope.Budget.Charge("dpapi"); break;
            case "custody" when scope.Plan.Id == "identity" && operation is "CreateByLocalOwner" or "OpenReviewed" or "ExportPublicSpki" or "CreateExactFixture":
                if (operation == "CreateByLocalOwner") scope.Budget.Charge("key"); break;
            case "custody" when scope.Plan.Id == "cleanup" && operation == "OpenReviewed": break;
            case "protocol" when operation == "Verify": scope.Budget.Charge("verify"); break;
            case "protocol" when scope.Plan.Id == "entropy" && operation is "NewRequestNonce" or "NewBootstrapVerifier" or "NewCredentialVerifier":
                scope.Budget.Charge(operation == "NewRequestNonce" ? "nonce" : operation == "NewBootstrapVerifier" ? "bootstrap" : "credential"); break;
            default: throw new InvalidOperationException("Operation outside case.");
        }
    }
}
