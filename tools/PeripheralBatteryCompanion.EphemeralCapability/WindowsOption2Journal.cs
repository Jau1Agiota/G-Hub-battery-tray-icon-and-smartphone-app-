using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace PeripheralBatteryCompanion.EphemeralCapability;

[SupportedOSPlatform("windows")]
internal sealed class WindowsOption2Journals : IO2Journals
{
    public IO2Journal CreateFresh(O2Plan plan)
    {
        if (!O2Permit.ValidId(plan.Operation)) throw new InvalidOperationException();
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new InvalidOperationException();
        if (!new WindowsCapabilityApi().IsOrdinaryUnelevatedUser()) throw new InvalidOperationException();
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PBC-A22C6-Option2-Evidence");
        if (root.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(root)!).DriveType != DriveType.Fixed) throw new InvalidOperationException();
        string directory = Path.Combine(root, plan.Operation);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException();
        NonReparseAncestors(directory);
        var security = new DirectorySecurity(); security.SetOwner(owner); security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        if (!Directory.Exists(root)) AtomicDirectory(root, security);
        DirectoryAcl(root, owner); // Existing namespace accepted only with exact restricted owner ACL; never repaired.
        AtomicDirectory(directory, security); // Must be NEW, never Directory.CreateDirectory adoption.
        DirectoryAcl(directory, owner); NonReparseAncestors(directory);
        FileStream? stream = null;
        try {
            stream = new FileStream(Path.Combine(directory, "ownership.jsonl"), FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 4096, FileOptions.WriteThrough);
            FileAcl(stream, owner);
            return new Journal(plan, root, directory, owner, stream);
        } catch { stream?.Dispose(); throw; } // Retain only own fresh files for review; no automatic deletion/recovery.
    }
    private static void NonReparseAncestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException();
    }
    private static void Acl(FileSystemSecurity security, SecurityIdentifier owner, bool protectedRequired)
    {
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
        var rule = rules.Count == 1 ? rules[0] as FileSystemAccessRule : null;
        var actual = new O2AclEvidence(security.GetOwner(typeof(SecurityIdentifier))?.Equals(owner) == true,
            security.AreAccessRulesProtected, rules.Count, rule?.IdentityReference.Equals(owner) == true,
            rule?.AccessControlType == AccessControlType.Allow,
            rule is not null && (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl);
        if (!Option2JournalSecurity.Restricted(actual, protectedRequired)) throw new InvalidOperationException();
    }
    private static void DirectoryAcl(string path, SecurityIdentifier owner) => Acl(new DirectoryInfo(path).GetAccessControl(), owner, true);
    private static void FileAcl(FileStream stream, SecurityIdentifier owner) => Acl(stream.GetAccessControl(), owner, false);
    [StructLayout(LayoutKind.Sequential)]
    private struct Attributes { public int Length; public IntPtr Security; public int Inherit; }
    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryNative(string path, ref Attributes attributes);
    private static void AtomicDirectory(string path, DirectorySecurity security)
    {
        byte[] descriptor = security.GetSecurityDescriptorBinaryForm();
        var pinned = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
        try {
            var attributes = new Attributes { Length = Marshal.SizeOf<Attributes>(), Security = pinned.AddrOfPinnedObject() };
            if (!CreateDirectoryNative(path, ref attributes)) throw new InvalidOperationException();
        } finally { pinned.Free(); }
    }
    private sealed class Journal(O2Plan plan, string root, string directory, SecurityIdentifier owner, FileStream stream) : IO2Journal
    {
        private const int MaxBytes = 65536;
        private readonly List<byte> expected = [];
        private byte[]? bound;
        private O2Stage? stage;
        private bool broken, disposed;
        public bool? ReleaseSucceeded { get; private set; }
        public void Append(O2Stage next, byte[]? publicBinding = null)
        {
            if (disposed || broken || !Option2JournalProtocol.Allowed(stage, next, publicBinding)) throw new InvalidOperationException();
            try {
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
                    version = 1, contract = O2Result.Contract, operation = plan.Operation, keyName = plan.KeyName,
                    provider = O2Plan.Provider, algorithm = O2Plan.Algorithm, requestedPolicy = 0, sequence = expected.Count,
                    stage = next.ToString(), publicSpki = next == O2Stage.Bound ? Convert.ToBase64String(publicBinding!) : null
                }) + "\n");
                if (bytes.Length > MaxBytes - expected.Count) throw new InvalidOperationException();
                stream.Position = stream.Length; stream.Write(bytes); stream.Flush(flushToDisk: true);
                expected.AddRange(bytes); stage = next;
                if (next == O2Stage.Bound) bound = publicBinding!.ToArray();
            } catch { broken = true; throw; }
        }
        public bool Verify(O2Plan selected, ReadOnlySpan<byte> binding)
        {
            if (disposed || broken || selected != plan || bound is null || !bound.AsSpan().SequenceEqual(binding)) return false;
            try {
                NonReparseAncestors(directory);
                if ((File.GetAttributes(Path.Combine(directory, "ownership.jsonl")) & FileAttributes.ReparsePoint) != 0) return false;
                DirectoryAcl(root, owner); DirectoryAcl(directory, owner); FileAcl(stream, owner);
                if (stream.Length != expected.Count || stream.Length > MaxBytes) return false;
                stream.Position = 0; var bytes = new byte[expected.Count]; stream.ReadExactly(bytes);
                return bytes.AsSpan().SequenceEqual(expected.ToArray()); // Public journal only, no private output.
            } catch { broken = true; return false; }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            try { stream.Dispose(); ReleaseSucceeded = true; } catch { ReleaseSucceeded = false; }
        }
    }
}
