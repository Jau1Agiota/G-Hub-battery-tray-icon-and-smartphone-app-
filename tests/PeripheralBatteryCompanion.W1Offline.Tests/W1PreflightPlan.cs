using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace PeripheralBatteryCompanion.W1Offline.Tests;

internal sealed class GateEPermit
{
    private static readonly byte[] OwnerPublicKeySpki = new byte[] {
        0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01, 0x06, 0x08, 0x2A,
        0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07, 0x03, 0x42, 0x00, 0x04, 0x1E, 0xF2, 0x48, 0x0C, 0xF2,
        0x8B, 0xCB, 0x47, 0x08, 0x5E, 0x0E, 0x28, 0x24, 0xF9, 0x4A, 0x5E, 0xA9, 0xED, 0xFB, 0x3D, 0x2F,
        0xF2, 0x95, 0x01, 0x19, 0x4B, 0xE3, 0x46, 0xA3, 0xEA, 0x96, 0x39, 0x48, 0xDE, 0xDA, 0x3A, 0x0F,
        0x8A, 0xDA, 0x1E, 0x4A, 0xC1, 0x25, 0xF8, 0x1D, 0x28, 0x7D, 0xCC, 0x94, 0xA9, 0xAC, 0x3C, 0x98,
        0x9C, 0xCC, 0x6C, 0x84, 0x44, 0xC3, 0xE9, 0xC4, 0xA3, 0x37, 0x26
    };

    private const string PermitPath = @"D:\Projects\GateE.permit";

    private readonly byte[] _authorizedDigest;

    private GateEPermit(byte[] authorizedDigest) => _authorizedDigest = authorizedDigest;

    internal bool Matches(W1PreflightPlan plan)
    {
        if (plan is null) return false;
        byte[] currentDigest = plan.ComputeDigest();
        return CryptographicOperations.FixedTimeEquals(_authorizedDigest, currentDigest);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle hFile,
        [Out] StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }

    private const uint GENERIC_READ = 0x80000000;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;
    private const uint FILE_NAME_NORMALIZED = 0x0;

    internal static bool TryAcquireFromFile(W1PreflightPlan plan, DateTimeOffset now, out GateEPermit? permit)
    {
        permit = null;
        if (plan is null) return false;

        // Step 1: Open permit read-only without following the final-component reparse point.
        using var handle = CreateFileW(
            PermitPath,
            GENERIC_READ,
            0, // FileShare.None
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);

        if (handle.IsInvalid) return false;

        // Check file attributes on handle for reparse point
        if (!GetFileInformationByHandle(handle, out var info)) return false;
        if ((info.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) return false;

        // Confirm final physical path matches D:\Projects\GateE.permit
        var sb = new StringBuilder(1024);
        uint pathLen = GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, FILE_NAME_NORMALIZED);
        if (pathLen == 0 || pathLen >= sb.Capacity) return false;
        string finalPath = sb.ToString();
        if (!string.Equals(finalPath, @"\\?\D:\Projects\GateE.permit", StringComparison.OrdinalIgnoreCase)) return false;

        // Do not delete or mark permits for deletion during acquisition.
        // Permit retirement requires a separately reviewed, trusted one-shot
        // protocol. Replay protection must not depend on this file disappearing.

        // Step 2: Stream length check (<= 1024 bytes) and read content
        using var fs = new FileStream(handle, FileAccess.Read);
        if (fs.Length is <= 0 or > 1024) return false;

        byte[] rawBytes = new byte[fs.Length];
        int read = fs.Read(rawBytes, 0, rawBytes.Length);
        if (read != rawBytes.Length) return false;

        // Reject UTF-8 BOM
        if (rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF) return false;

        var utf8Strict = new UTF8Encoding(false, true);
        string text;
        try { text = utf8Strict.GetString(rawBytes); }
        catch { return false; }

        string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        if (lines.Length is < 4 or > 5 || (lines.Length == 5 && lines[4].Length != 0)) return false;

        if (!lines[0].StartsWith("NONCE=", StringComparison.Ordinal) ||
            !lines[1].StartsWith("EXPIRY_UTC=", StringComparison.Ordinal) ||
            !lines[2].StartsWith("PLAN_SHA256=", StringComparison.Ordinal) ||
            !lines[3].StartsWith("SIGNATURE_DER_BASE64=", StringComparison.Ordinal)) return false;

        string nonceHex = lines[0].Substring(6);
        string expiryStr = lines[1].Substring(11);
        string planSha256Hex = lines[2].Substring(12);
        string sigBase64 = lines[3].Substring(21);

        // Validate nonce format: exactly 32 uppercase hex chars
        if (nonceHex.Length != 32) return false;
        foreach (char c in nonceHex)
            if (!(c is >= '0' and <= '9' or >= 'A' and <= 'F')) return false;
        byte[] nonceBytes = Convert.FromHexString(nonceHex);

        // Validate plan SHA256 hex format: exactly 64 uppercase hex chars
        if (planSha256Hex.Length != 64) return false;
        foreach (char c in planSha256Hex)
            if (!(c is >= '0' and <= '9' or >= 'A' and <= 'F')) return false;
        byte[] expectedDigest = Convert.FromHexString(planSha256Hex);

        byte[] sigBytes;
        try { sigBytes = Convert.FromBase64String(sigBase64); }
        catch { return false; }

        // Step 3: Expiry validation
        if (!DateTimeOffset.TryParseExact(expiryStr, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var expiryUtc)) return false;

        if (now >= expiryUtc) return false;
        if (!plan.NotBeforeUtc.HasValue || !plan.NotAfterUtc.HasValue) return false;
        if (expiryUtc < plan.NotBeforeUtc.Value || expiryUtc > plan.NotAfterUtc.Value) return false;
        if (expiryUtc - now < TimeSpan.FromMinutes(2)) return false;

        // Step 4: Compute canonical plan digest from actual plan
        byte[] actualDigest = plan.ComputeDigest();
        if (!CryptographicOperations.FixedTimeEquals(actualDigest, expectedDigest)) return false;

        // Step 5: Verify ECDSA P-256 signature over 73-byte payload
        byte[] payload = new byte[73];
        byte[] prefix = Encoding.ASCII.GetBytes("GATE_E_PERMIT_V1:");
        Buffer.BlockCopy(prefix, 0, payload, 0, 17);
        Buffer.BlockCopy(nonceBytes, 0, payload, 17, 16);
        long expiryUnix = expiryUtc.ToUnixTimeSeconds();
        byte[] expiryBytes = BitConverter.GetBytes(expiryUnix);
        if (!BitConverter.IsLittleEndian) Array.Reverse(expiryBytes);
        Buffer.BlockCopy(expiryBytes, 0, payload, 33, 8);
        Buffer.BlockCopy(actualDigest, 0, payload, 41, 32);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(OwnerPublicKeySpki, out _);
        if (!ecdsa.VerifyData(payload, sigBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            return false;

        // Step 6: Fail closed until a trusted nonce-ledger protocol is approved.
        // Existing paths, ACLs, ancestor reparse points, rollback resistance,
        // and atomic reservation are not yet established.
        // Do not open, create, read, truncate, or append to the ledger.
        return false;
    }
}

internal sealed record W1PreflightPlan(
    string? CanonicalTestIp,
    string? SyntheticSubject,
    string? PublicSerialHex,
    DateTimeOffset? NotBeforeUtc,
    DateTimeOffset? NotAfterUtc,
    bool? ClockConfidenceConfirmed,
    int? ClockUncertaintySeconds,
    bool? SanCritical,
    bool? EkuCritical,
    bool? UsageCritical,
    bool? BasicCritical,
    int? PublicBufferAcceptanceBytes,
    int? MetadataAcceptanceBytes,
    int? ReportAcceptanceBytes,
    string? NewReportDestination)
{
    private static int attempted;

    internal byte[] ComputeDigest()
    {
        var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, utf8Strict, leaveOpen: true);

        // Field 1: CanonicalTestIp
        if (!TryTestIp(CanonicalTestIp, out var ip) || ip is null)
            throw new InvalidOperationException("Invalid CanonicalTestIp");
        byte[] ipBytes = utf8Strict.GetBytes(ip.ToString());
        bw.Write(ipBytes.Length);
        bw.Write(ipBytes);

        // Field 2: SyntheticSubject
        if (SyntheticSubject is null) throw new InvalidOperationException("SyntheticSubject null");
        byte[] subjBytes = utf8Strict.GetBytes(SyntheticSubject);
        bw.Write(subjBytes.Length);
        bw.Write(subjBytes);

        // Field 3: PublicSerialHex
        if (!TrySerial(PublicSerialHex, out _))
            throw new InvalidOperationException("Invalid PublicSerialHex");
        byte[] serialBytes = Encoding.ASCII.GetBytes(PublicSerialHex!);
        bw.Write(serialBytes.Length);
        bw.Write(serialBytes);

        // Field 4: NotBeforeUtc
        if (!NotBeforeUtc.HasValue || NotBeforeUtc.Value.Offset != TimeSpan.Zero ||
            NotBeforeUtc.Value.Ticks % TimeSpan.TicksPerSecond != 0)
            throw new InvalidOperationException("Invalid NotBeforeUtc");
        bw.Write(NotBeforeUtc.Value.ToUnixTimeSeconds());

        // Field 5: NotAfterUtc
        if (!NotAfterUtc.HasValue || NotAfterUtc.Value.Offset != TimeSpan.Zero ||
            NotAfterUtc.Value.Ticks % TimeSpan.TicksPerSecond != 0)
            throw new InvalidOperationException("Invalid NotAfterUtc");
        bw.Write(NotAfterUtc.Value.ToUnixTimeSeconds());

        // Field 6: ClockConfidenceConfirmed
        if (ClockConfidenceConfirmed != true) throw new InvalidOperationException("ClockConfidenceConfirmed must be true");
        bw.Write((byte)0x01);

        // Field 7: ClockUncertaintySeconds
        if (ClockUncertaintySeconds is not (>= 0 and <= 30))
            throw new InvalidOperationException("Invalid ClockUncertaintySeconds");
        bw.Write(ClockUncertaintySeconds.Value);

        // Field 8: SanCritical
        if (SanCritical != false) throw new InvalidOperationException("SanCritical must be false");
        bw.Write((byte)0x00);

        // Field 9: EkuCritical
        if (EkuCritical != false) throw new InvalidOperationException("EkuCritical must be false");
        bw.Write((byte)0x00);

        // Field 10: UsageCritical
        if (UsageCritical != true) throw new InvalidOperationException("UsageCritical must be true");
        bw.Write((byte)0x01);

        // Field 11: BasicCritical
        if (BasicCritical != true) throw new InvalidOperationException("BasicCritical must be true");
        bw.Write((byte)0x01);

        // Field 12: PublicBufferAcceptanceBytes
        if (PublicBufferAcceptanceBytes is not (>= 1024 and <= 65536))
            throw new InvalidOperationException("Invalid PublicBufferAcceptanceBytes");
        bw.Write(PublicBufferAcceptanceBytes.Value);

        // Field 13: MetadataAcceptanceBytes
        if (MetadataAcceptanceBytes is not (>= 24 and <= 64))
            throw new InvalidOperationException("Invalid MetadataAcceptanceBytes");
        bw.Write(MetadataAcceptanceBytes.Value);

        // Field 14: ReportAcceptanceBytes
        if (ReportAcceptanceBytes is not (>= 2048 and <= 8192))
            throw new InvalidOperationException("Invalid ReportAcceptanceBytes");
        bw.Write(ReportAcceptanceBytes.Value);

        // Field 15: NewReportDestination
        if (NewReportDestination is null) throw new InvalidOperationException("NewReportDestination null");
        string normalizedPath = Path.GetFullPath(NewReportDestination).TrimEnd('\\');
        byte[] pathBytes = utf8Strict.GetBytes(normalizedPath);
        bw.Write(pathBytes.Length);
        bw.Write(pathBytes);

        bw.Flush();
        return SHA256.HashData(ms.ToArray());
    }

    internal bool InputsValid(DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero || !TryTestIp(CanonicalTestIp, out _)) return false;
        if (SyntheticSubject is null || SyntheticSubject.Length is < 1 or > 96 ||
            !SyntheticSubject.StartsWith("CN=", StringComparison.Ordinal)) return false;
        if (SyntheticSubject.Length <= 3) return false;
        foreach (char c in SyntheticSubject.AsSpan(3))
            if (!(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or ' ' or '-'))
                return false;
        if (!TrySerial(PublicSerialHex, out _)) return false;
        if (!NotBeforeUtc.HasValue || !NotAfterUtc.HasValue ||
            NotBeforeUtc.Value.Offset != TimeSpan.Zero || NotAfterUtc.Value.Offset != TimeSpan.Zero)
            return false;
        var before = NotBeforeUtc.Value;
        var after = NotAfterUtc.Value;
        if (before.Ticks % TimeSpan.TicksPerSecond != 0 || after.Ticks % TimeSpan.TicksPerSecond != 0 ||
            after <= before || after - before > TimeSpan.FromMinutes(10) ||
            now < before || now > after || after - now < TimeSpan.FromMinutes(2)) return false;
        if (ClockConfidenceConfirmed != true || ClockUncertaintySeconds is not (>= 0 and <= 30)) return false;
        if (SanCritical != false || EkuCritical != false || UsageCritical != true || BasicCritical != true)
            return false;
        if (PublicBufferAcceptanceBytes is not (>= 1024 and <= 65536) ||
            MetadataAcceptanceBytes is not (>= 24 and <= 64) ||
            ReportAcceptanceBytes is not (>= 2048 and <= 8192)) return false;
        if (string.IsNullOrWhiteSpace(NewReportDestination) || NewReportDestination.Length > 240 ||
            !Path.IsPathFullyQualified(NewReportDestination) ||
            !string.Equals(Path.GetExtension(NewReportDestination), ".txt", StringComparison.Ordinal)) return false;
        if (NewReportDestination.Length < 8 || NewReportDestination[0] is not (>= 'A' and <= 'Z') ||
            NewReportDestination[1] != ':' || NewReportDestination[2] != '\\' ||
            NewReportDestination.AsSpan(2).IndexOf(':') >= 0 || NewReportDestination.Contains('/')) return false;
        foreach (string part in NewReportDestination.Substring(3).Split('\\'))
            if (string.IsNullOrEmpty(part) || part is "." or ".." ||
                part.EndsWith(' ') || part.EndsWith('.') || part.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0)
                return false;
        return true;
    }

    internal static bool TryConsume(W1PreflightPlan? plan, GateEPermit? permit, DateTimeOffset now)
    {
        if (Interlocked.CompareExchange(ref attempted, 1, 0) != 0) return false;
        if (plan is null || permit is null || !permit.Matches(plan)) return false;
        return plan.InputsValid(now);
    }

    internal static bool TryTestIp(string? text, out IPAddress? address)
    {
        address = null;
        if (text is null || text.Length > 45 || text.Contains('%') ||
            !IPAddress.TryParse(text, out var parsed) ||
            !string.Equals(parsed.ToString(), text, StringComparison.Ordinal)) return false;
        byte[] bytes = parsed.GetAddressBytes();
        bool allowed = parsed.AddressFamily == AddressFamily.InterNetwork
            ? bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
              (bytes[0] == 192 && bytes[1] == 168)
            : parsed.AddressFamily == AddressFamily.InterNetworkV6 && parsed.ScopeId == 0 &&
              !parsed.IsIPv4MappedToIPv6 && (bytes[0] & 0xFE) == 0xFC;
        if (!allowed) return false;
        address = parsed;
        return true;
    }

    internal static bool TrySerial(string? hex, out byte[]? bytes)
    {
        bytes = null;
        if (hex is null || hex.Length is < 2 or > 32 || hex.Length % 2 != 0) return false;
        foreach (char c in hex)
            if (!(c is >= '0' and <= '9' or >= 'A' and <= 'F')) return false;
        byte[] decoded = Convert.FromHexString(hex);
        if (decoded[0] == 0 || decoded[0] >= 0x80) return false;
        bytes = decoded;
        return true;
    }
}