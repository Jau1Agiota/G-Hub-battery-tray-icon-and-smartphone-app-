using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PeripheralBatteryCompanion.Windows;

/// <summary>Held production-only directory/handle validation. No startup or test bypass exists.</summary>
internal sealed class WindowsAuthorizationDirectory : IDisposable
{
    private readonly List<SafeFileHandle> _pins = [];
    private readonly SecurityIdentifier _owner;
    internal string Path { get; }

    internal static bool ValidPathSyntax(string? path)
    {
        if (path is null || path.Length is < 4 or > 220 || !System.IO.Path.IsPathFullyQualified(path) ||
            path[0] is not (>= 'A' and <= 'Z') || path[1] != ':' || path[2] != '\\' || path.Contains('/') ||
            path.AsSpan(2).IndexOf(':') >= 0 || path.EndsWith('\\')) return false;
        foreach (string part in path[3..].Split('\\'))
            if (part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
                part.Any(char.IsControl) || part.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0 || ReservedName(part)) return false;
        return string.Equals(System.IO.Path.GetFullPath(path), path, StringComparison.Ordinal);
    }
    private static bool ReservedName(string part)
    {
        string name = part.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" ||
            (name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) &&
                (name[3] is >= '1' and <= '9' or '¹' or '²' or '³'));
    }

    private WindowsAuthorizationDirectory(string path, bool requirePrivate)
    {
        RequireNativeApproval();
        if (!ValidPathSyntax(path)) throw new IOException("Journal path syntax rejected.");
        var volume = new DriveInfo(System.IO.Path.GetPathRoot(path)!);
        if (volume.DriveType != DriveType.Fixed || volume.DriveFormat != "NTFS") throw new IOException("Journal volume outside initial supported scope.");
        Path = path;
        using var current = WindowsIdentity.GetCurrent();
        _owner = current.User ?? throw new IOException("Current user unavailable.");
        try
        {
            string segment = System.IO.Path.GetPathRoot(path)!;
            var segments = new List<string> { segment };
            foreach (string part in path[3..].Split('\\')) { segment = System.IO.Path.Join(segment, part); segments.Add(segment); }
            foreach (string item in segments)
            {
                bool target = item == path;
                // No delete sharing: pinned ancestors cannot be renamed during this session.
                uint access = 0x00020080u | (target && requirePrivate ? 0x40000000u : 0u);
                var handle = CreateFileW(item, access, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw new IOException("Journal ancestor open rejected."); }
                _pins.Add(handle);
                ValidateMetadata(handle, item, directory: true);
                if (target && requirePrivate) ValidateOwner(handle, protectedDacl: true);
            }
        }
        catch { Dispose(); throw; }
    }

    internal static WindowsAuthorizationDirectory OpenExisting(string path)
    {
        RequireNativeApproval();
        return new(path, requirePrivate: true);
    }
    internal static WindowsAuthorizationDirectory CreateFresh(string path)
    {
        RequireNativeApproval();
        if (!ValidPathSyntax(path)) throw new IOException("Journal path syntax rejected.");
        string parent = System.IO.Path.GetDirectoryName(path)!;
        // Parent must be an explicit non-root reviewed directory, not implicitly created ancestry.
        using var parentPins = new WindowsAuthorizationDirectory(parent, requirePrivate: false);
        using var current = WindowsIdentity.GetCurrent();
        string sid = current.User?.Value ?? throw new IOException("Current user unavailable.");
        var descriptor = new RawSecurityDescriptor($"O:{sid}D:P(A;OICI;FA;;;{sid})");
        byte[] bytes = new byte[descriptor.BinaryLength]; descriptor.GetBinaryForm(bytes, 0);
        GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = pin.AddrOfPinnedObject() };
            if (!CreateDirectoryW(path, ref attributes)) throw new IOException("Fresh private directory creation refused; no overwrite.");
        }
        finally { pin.Free(); }
        // Preserve any partially initialized directory on failure. No broad cleanup or retry.
        return new(path, requirePrivate: true);
    }

    internal void ValidateFile(SafeFileHandle handle, string path)
    {
        RequireNativeApproval();
        if (!string.Equals(System.IO.Path.GetDirectoryName(path), Path, StringComparison.Ordinal)) throw new IOException("Journal file escaped directory.");
        ValidateMetadata(handle, path, directory: false);
        ValidateOwner(handle, protectedDacl: false);
    }
    internal FileStream OpenFile(string path, FileMode mode, FileAccess access, FileShare share, FileOptions options)
    {
        RequireNativeApproval();
        if (!string.Equals(System.IO.Path.GetDirectoryName(path), Path, StringComparison.Ordinal)) throw new IOException("Journal open escaped directory.");
        uint disposition = mode switch { FileMode.CreateNew => 1u, FileMode.Open => 3u, FileMode.OpenOrCreate => 4u,
            _ => throw new IOException("Journal open mode rejected.") };
        uint desired = 0x00020080u | (access == FileAccess.Read ? 0x80000000u : access == FileAccess.ReadWrite ? 0xC0000000u :
            throw new IOException("Journal access rejected."));
        if (share is not (FileShare.None or FileShare.Read) || options is not (FileOptions.None or FileOptions.WriteThrough))
            throw new IOException("Journal open flags rejected.");
        uint flags = 0x00200000u | (options == FileOptions.WriteThrough ? 0x80000000u : 0u);
        var handle = CreateFileW(path, desired, (uint)share, IntPtr.Zero, disposition, flags, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error(); handle.Dispose();
            if (mode == FileMode.Open && error == 2) throw new FileNotFoundException("Journal file absent.");
            throw new IOException("Journal non-following file open rejected.");
        }
        try { ValidateFile(handle, path); return new FileStream(handle, access, 4096, false); }
        catch { handle.Dispose(); throw; }
    }
    internal void Flush()
    {
        RequireNativeApproval();
        // Unsupported directory flush is a hard failure, never a successful fallback.
        if (_pins.Count == 0 || !FlushFileBuffers(_pins[^1])) throw new IOException("Journal directory durability unconfirmed.");
    }
    private static void ValidateMetadata(SafeFileHandle handle, string expectedPath, bool directory)
    {
        if (GetFileType(handle) != 1 || !GetFileInformationByHandle(handle, out var info) ||
            (info.Attributes & 0x400) != 0 || ((info.Attributes & 0x10) != 0) != directory ||
            (!directory && info.Links != 1)) throw new IOException("Journal handle metadata rejected.");
        var actual = new StringBuilder(512);
        uint count = GetFinalPathNameByHandleW(handle, actual, (uint)actual.Capacity, 0);
        if (count == 0 || count >= actual.Capacity || !string.Equals(actual.ToString().TrimEnd('\\'),
            (@"\\?\" + expectedPath).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new IOException("Journal path-to-handle binding rejected.");
    }
    private void ValidateOwner(SafeFileHandle handle, bool protectedDacl)
    {
        uint error = GetSecurityInfo(handle, 1, 5, out _, out _, out _, out _, out var descriptor);
        if (error != 0 || descriptor == IntPtr.Zero) throw new IOException("Journal security descriptor unavailable.");
        bool accepted = false, cleanupConfirmed = false;
        try
        {
            uint length = GetSecurityDescriptorLength(descriptor);
            if (length is > 0 and <= 8192)
            {
                byte[] bytes = new byte[(int)length]; Marshal.Copy(descriptor, bytes, 0, bytes.Length);
                accepted = OwnerOnly(new RawSecurityDescriptor(bytes, 0), _owner, protectedDacl);
            }
        }
        catch (ArgumentException) { accepted = false; }
        finally { cleanupConfirmed = LocalFree(descriptor) == IntPtr.Zero; }
        if (!accepted || !cleanupConfirmed) throw new IOException("Journal descriptor validation or cleanup unconfirmed.");
    }
    internal static bool OwnerOnly(RawSecurityDescriptor security, SecurityIdentifier owner, bool protectedDacl)
    {
        if (security.Owner != owner || !security.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclPresent) ||
            security.DiscretionaryAcl is not { Count: 1 } dacl ||
            (protectedDacl && !security.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected))) return false;
        return dacl[0] is CommonAce ace && !ace.IsCallback && ace.AceQualifier == AceQualifier.AccessAllowed &&
            ace.SecurityIdentifier == owner && ace.AccessMask == (int)FileSystemRights.FullControl &&
            (ace.AceFlags & AceFlags.InheritOnly) == 0;
    }
    public void Dispose()
    {
        // Disposal returning is not independently verified native handle-release evidence.
        foreach (var handle in _pins.AsEnumerable().Reverse()) handle.Dispose();
        _pins.Clear();
    }
    private static void RequireNativeApproval() => throw new InvalidOperationException(
        "GATE E BLOCKED: protected journal directory operations require separately approved native validation.");

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfo
    {
        public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr attributes, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(string path, ref SecurityAttributes attributes);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);
    [DllImport("kernel32.dll")] private static extern uint GetFileType(SafeFileHandle handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern uint GetSecurityInfo(SafeFileHandle handle, uint type,
        uint requested, out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);
    [DllImport("advapi32.dll")] private static extern uint GetSecurityDescriptorLength(IntPtr descriptor);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushFileBuffers(SafeFileHandle handle);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
