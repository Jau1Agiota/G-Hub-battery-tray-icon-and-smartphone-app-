using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace PeripheralBatteryCompanion.W1Offline.Tests;

// Authorization acquisition is deliberately inert. Keep the type and method
// signatures for existing callers, but do not access files, Windows handles,
// certificate stores, or cryptographic providers until a separately approved
// trusted authorization mechanism exists.
internal sealed class GateEPermit
{
    internal bool Matches(W1PreflightPlan plan)
    {
        _ = plan;
        return false;
    }

    internal static bool TryAcquireFromFile(W1PreflightPlan plan, DateTimeOffset now, out GateEPermit? permit)
    {
        _ = plan;
        _ = now;
        permit = null;
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
    // Platform-backed SHA-256 is deliberately kept separate from pure canonical
    // serialization. This method makes no provider-free guarantee and must not be
    // treated as part of preflight validation or authorization acquisition.
    internal byte[] ComputeDigest() => SHA256.HashData(SerializeCanonical());

    // Pure 15-field canonical serialization. Preserve field order and encoding;
    // the frozen digest vector is defined over these exact bytes.
    internal byte[] SerializeCanonical()
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
        if (NewReportDestination is null || !Path.IsPathFullyQualified(NewReportDestination))
            throw new InvalidOperationException("NewReportDestination must be fully qualified");
        string normalizedPath = Path.GetFullPath(NewReportDestination).TrimEnd('\\');
        byte[] pathBytes = utf8Strict.GetBytes(normalizedPath);
        bw.Write(pathBytes.Length);
        bw.Write(pathBytes);

        bw.Flush();
        return ms.ToArray();
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

    // Legacy compatibility shim for the existing lifecycle caller. Authorization
    // and one-shot admission do not belong to the preflight plan. Keep this path
    // unconditionally denied until a separately approved authorization service
    // provides authenticated consent, exact binding, expiry, and trusted atomic
    // one-time nonce consumption. The process-local latch remains in the gate as
    // supplementary defense only.
    internal static bool TryConsume(W1PreflightPlan? plan, GateEPermit? permit, DateTimeOffset now)
    {
        _ = plan;
        _ = permit;
        _ = now;
        return false;
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