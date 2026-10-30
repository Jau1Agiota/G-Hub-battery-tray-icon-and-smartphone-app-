using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PeripheralBatteryCompanion.Core.TransportSecurity;

public enum PublicCertificateStatus
{
    PublicFieldsMatch, InvalidExpectation, MalformedCertificate, PinMismatch, SanMismatch,
    OutsideValidity, NotServerLeaf, MissingServerAuth, MissingSigningUsage
}
public sealed record PublicCertificateAssessment(PublicCertificateStatus Status, ImmutableArray<byte> SpkiSha256,
    DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil);

/// <summary>Offline public fields only. NOT chain trust, private-key association/protection, revocation or a TLS validation callback.</summary>
public static class PublicServerCertificateInspector
{
    public static bool ValidExpectation(string dnsName, ImmutableArray<byte> pin) =>
        !string.IsNullOrEmpty(dnsName) && dnsName.Length <= 253 && Uri.CheckHostName(dnsName) == UriHostNameType.Dns &&
        !pin.IsDefault && pin.Length == 32 && pin.Any(b => b != 0);

    public static PublicCertificateAssessment Inspect(X509Certificate2 certificate, string dnsName,
        ImmutableArray<byte> expectedSpkiSha256, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (!ValidExpectation(dnsName, expectedSpkiSha256)) return Failed(PublicCertificateStatus.InvalidExpectation);
        try
        {
            // A SHA1 certificate thumbprint is not a pin; hash DER SubjectPublicKeyInfo, matching Android PublicKey.encoded.
            var pin = ImmutableArray.CreateRange(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));
            var from = new DateTimeOffset(certificate.NotBefore.ToUniversalTime());
            var until = new DateTimeOffset(certificate.NotAfter.ToUniversalTime());
            PublicCertificateAssessment Result(PublicCertificateStatus status) => new(status,pin,from,until);
            foreach (var oid in new[] { "2.5.29.17", "2.5.29.19", "2.5.29.37", "2.5.29.15" })
                if (certificate.Extensions.Cast<X509Extension>().Count(e => e.Oid?.Value == oid) != 1) return Result(PublicCertificateStatus.MalformedCertificate);
            if (!CryptographicOperations.FixedTimeEquals(pin.AsSpan(), expectedSpkiSha256.AsSpan())) return Result(PublicCertificateStatus.PinMismatch);
            if (!certificate.MatchesHostname(dnsName, allowWildcards:false, allowCommonName:false)) return Result(PublicCertificateStatus.SanMismatch);
            if (now < from || now >= until || until <= from) return Result(PublicCertificateStatus.OutsideValidity);
            var basic = new X509BasicConstraintsExtension(certificate.Extensions["2.5.29.19"]!,false);
            if (basic.CertificateAuthority) return Result(PublicCertificateStatus.NotServerLeaf);
            var eku = new X509EnhancedKeyUsageExtension(certificate.Extensions["2.5.29.37"]!,false);
            if (!eku.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == "1.3.6.1.5.5.7.3.1")) return Result(PublicCertificateStatus.MissingServerAuth);
            var usage = new X509KeyUsageExtension(certificate.Extensions["2.5.29.15"]!,false);
            if ((usage.KeyUsages & X509KeyUsageFlags.DigitalSignature) == 0) return Result(PublicCertificateStatus.MissingSigningUsage);
            return Result(PublicCertificateStatus.PublicFieldsMatch);
        }
        catch (Exception error) when (error is CryptographicException or ArgumentException or InvalidOperationException)
        { return Failed(PublicCertificateStatus.MalformedCertificate); }
    }
    private static PublicCertificateAssessment Failed(PublicCertificateStatus status) => new(status,ImmutableArray<byte>.Empty,null,null);
}
