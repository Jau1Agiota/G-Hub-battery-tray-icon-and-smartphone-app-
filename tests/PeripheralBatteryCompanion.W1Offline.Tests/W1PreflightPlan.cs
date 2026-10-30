using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace PeripheralBatteryCompanion.W1Offline.Tests;

// Deliberately NO issuer, factory, deserializer, environment toggle or public
// constructor. Gate I consent cannot produce a Gate E capability. A future permit
// ingress/verification design needs independent source/binary/host/owner review.
internal sealed class GateEPermit
{
    private GateEPermit() { }
    internal bool Matches(W1PreflightPlan plan) => false;
}

// No owner input has a usable default. Objects carry no executable native state.
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

    // Pure input checks: no filesystem reads/writes, environment probes, native
    // crypto or OS/profile/provider inspection. now is an explicit UTC input.
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
        // Values must be explicitly supplied and match the reviewed extension profile.
        if (SanCritical != false || EkuCritical != false || UsageCritical != true || BasicCritical != true)
            return false;
        if (PublicBufferAcceptanceBytes is not (>= 1024 and <= 65536) ||
            MetadataAcceptanceBytes is not (>= 24 and <= 64) ||
            ReportAcceptanceBytes is not (>= 2048 and <= 8192)) return false;
        if (string.IsNullOrWhiteSpace(NewReportDestination) || NewReportDestination.Length > 240 ||
            !Path.IsPathFullyQualified(NewReportDestination) ||
            !string.Equals(Path.GetExtension(NewReportDestination), ".txt", StringComparison.Ordinal)) return false;
        // Local drive paths only: reject UNC/device paths and alternate streams.
        // This is lexical validation, NOT proof against reparse points/sync folders.
        if (NewReportDestination.Length < 8 || NewReportDestination[0] is not (>= 'A' and <= 'Z') ||
            NewReportDestination[1] != ':' || NewReportDestination[2] != '\\' ||
            NewReportDestination.AsSpan(2).IndexOf(':') >= 0 || NewReportDestination.Contains('/')) return false;
        foreach (string part in NewReportDestination.Substring(3).Split('\\'))
            if (string.IsNullOrEmpty(part) || part is "." or ".." ||
                part.EndsWith(' ') || part.EndsWith('.') || part.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0)
                return false;
        // Destination trust/location/existence requires later independent owner review;
        // no directory discovery, overwrite, destination default or file probe here.
        return true;
    }

    internal static bool TryConsume(W1PreflightPlan? plan, GateEPermit? permit, DateTimeOffset now)
    {
        // An attempted invocation, even invalid, is consumed; no in-process retry.
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
