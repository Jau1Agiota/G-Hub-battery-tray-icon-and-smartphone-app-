using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace PeripheralBatteryCompanion.Core.TransportSecurity;

/// <summary>Exact E3.1 binding data, not evidence that a certificate or TLS peer was verified.</summary>
public sealed class EnrollmentIdentity
{
    public Guid IdentityId { get; }
    public string Endpoint { get; }
    public string SpkiSha256 { get; }
    public ImmutableArray<byte> LeafDer { get; }
    public DateTimeOffset LeafNotBefore { get; }
    public DateTimeOffset LeafNotAfter { get; }

    public EnrollmentIdentity(Guid identityId, string endpoint, string spkiSha256,
        ReadOnlySpan<byte> leafDer, DateTimeOffset leafNotBefore, DateTimeOffset leafNotAfter)
    {
        if (identityId == Guid.Empty || !ValidEndpoint(endpoint) || !IsLowerHexDigest(spkiSha256) ||
            leafDer.Length is < 1 or > 4096 || !WholeUtc(leafNotBefore) || !WholeUtc(leafNotAfter) ||
            leafNotAfter <= leafNotBefore || leafNotAfter - leafNotBefore > TimeSpan.FromDays(90))
            throw new ArgumentException("Invalid enrollment identity binding.");
        IdentityId = identityId; Endpoint = endpoint; SpkiSha256 = spkiSha256;
        LeafDer = ImmutableArray.CreateRange(leafDer.ToArray());
        LeafNotBefore = leafNotBefore; LeafNotAfter = leafNotAfter;
    }

    public bool Matches(EnrollmentIdentity other) => other is not null &&
        IdentityId == other.IdentityId && Endpoint == other.Endpoint && SpkiSha256 == other.SpkiSha256 &&
        LeafNotBefore == other.LeafNotBefore && LeafNotAfter == other.LeafNotAfter &&
        LeafDer.AsSpan().SequenceEqual(other.LeafDer.AsSpan());

    public static bool ValidEndpoint(string? value)
    {
        if (value is null || value.Length is < 15 or > 64 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Port is < 1024 or > 65535 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 ||
            uri.Fragment.Length != 0 || !IPAddress.TryParse(uri.Host, out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] bytes = address.GetAddressBytes();
        bool privateIp = bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
            bytes[0] == 192 && bytes[1] == 168;
        return privateIp && value == $"https://{address}:{uri.Port}";
    }

    internal static bool IsLowerHexDigest(string? value) => value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static bool WholeUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero &&
        value.Ticks % TimeSpan.TicksPerSecond == 0;
    public override string ToString() => "EnrollmentIdentity (redacted; verification required)";
}

/// <summary>Claim binding only; never accepts or stores a raw bearer/bootstrap.</summary>
public sealed class EnrollmentClaim
{
    public Guid EnrollmentId { get; }
    public Guid PhoneId { get; }
    public string PhoneNonce { get; }
    public string TokenSha256 { get; }

    public EnrollmentClaim(Guid enrollmentId, Guid phoneId, string phoneNonce, string tokenSha256)
    {
        if (enrollmentId == Guid.Empty || phoneId == Guid.Empty || !CanonicalNonce(phoneNonce) ||
            !EnrollmentIdentity.IsLowerHexDigest(tokenSha256)) throw new ArgumentException("Invalid enrollment claim.");
        EnrollmentId = enrollmentId; PhoneId = phoneId; PhoneNonce = phoneNonce; TokenSha256 = tokenSha256;
    }

    private static bool CanonicalNonce(string? value)
    {
        if (value is not { Length: 43 } || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) return false;
        Span<byte> bytes = stackalloc byte[32];
        if (!Convert.TryFromBase64String(value.Replace('-', '+').Replace('_', '/') + "=", bytes, out int count) || count != 32) return false;
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') == value;
    }

    public byte[] TranscriptBytes(EnrollmentIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return Encoding.ASCII.GetBytes($"PBC-ENROLL-1\n{identity.Endpoint}\n{identity.SpkiSha256}\n{EnrollmentId:D}\n{PhoneId:D}\n{PhoneNonce}\n{TokenSha256}\n");
    }

    // Public binding digest, not credential creation or native key/certificate execution.
    public string TranscriptSha256(EnrollmentIdentity identity) =>
        Convert.ToHexString(SHA256.HashData(TranscriptBytes(identity))).ToLowerInvariant();
    public override string ToString() => "EnrollmentClaim (redacted)";
}
