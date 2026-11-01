using System.Formats.Asn1;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;

namespace PeripheralBatteryCompanion.Core.TransportSecurity;

/// <summary>Trusted signature boundary. Production verification must use the parsed leaf's own P-256 key.</summary>
public interface IEnrolledCertificateSignature
{
    bool Verify(ReadOnlyMemory<byte> spki, ReadOnlyMemory<byte> signedBytes, ReadOnlyMemory<byte> signature);
}

/// <summary>Strict offline DER/profile policy; does not establish TLS possession or use OS trust/network retrieval.</summary>
public static class EnrolledCertificatePolicy
{
    private const string SignatureOid = "1.2.840.10045.4.3.2";
    private static readonly BigInteger Order = Unsigned(Convert.FromHexString("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551"));
    private static readonly BigInteger Prime = Unsigned(Convert.FromHexString("FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF"));
    private static readonly BigInteger CurveB = Unsigned(Convert.FromHexString("5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B"));
    private static BigInteger Unsigned(ReadOnlySpan<byte> bytes) => new(bytes, true, true);
    public static bool Validate(EnrollmentIdentity expected, ReadOnlyMemory<byte> supplied, DateTimeOffset now,
        IEnrolledCertificateSignature signatureVerifier)
    {
        try
        {
            if (supplied.Length is < 1 or > 4096 || now.Offset != TimeSpan.Zero || signatureVerifier is null) return false;
            // Snapshot potentially mutable caller data before parsing or crossing the trusted signature boundary.
            byte[] snapshot = supplied.ToArray();
            if (!expected.LeafDer.AsSpan().SequenceEqual(snapshot)) return false;
            var reader = new AsnReader(snapshot, AsnEncodingRules.DER);
            var certificate = reader.ReadSequence(); reader.ThrowIfNotEmpty();
            var signed = certificate.ReadEncodedValue();
            var outerAlgorithm = certificate.ReadEncodedValue(); Algorithm(outerAlgorithm);
            byte[] signature = certificate.ReadBitString(out int unused); certificate.ThrowIfNotEmpty();
            if (unused != 0) return false;
            var sigReader = new AsnReader(signature, AsnEncodingRules.DER); var sig = sigReader.ReadSequence();
            BigInteger r = sig.ReadInteger(), s = sig.ReadInteger(); sig.ThrowIfNotEmpty(); sigReader.ThrowIfNotEmpty();
            if (r <= 0 || s <= 0 || r >= Order || s >= Order) return false;
            var signedReader = new AsnReader(signed, AsnEncodingRules.DER); var tbs = signedReader.ReadSequence(); signedReader.ThrowIfNotEmpty();
            var version = tbs.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true));
            if (version.ReadInteger() != 2) return false; version.ThrowIfNotEmpty();
            var serial = tbs.ReadIntegerBytes();
            if (serial.Length is < 1 or > 20 || new BigInteger(serial.Span, false, true) <= 0) return false;
            if (!tbs.ReadEncodedValue().Span.SequenceEqual(outerAlgorithm.Span)) return false;
            var issuer = Name(tbs); var validity = tbs.ReadSequence();
            var before = Time(validity); var after = Time(validity); validity.ThrowIfNotEmpty();
            if (before != expected.LeafNotBefore || after != expected.LeafNotAfter || after <= before ||
                after - before > TimeSpan.FromDays(90) || now < before || now >= after) return false;
            if (!Name(tbs).Span.SequenceEqual(issuer.Span)) return false;
            var spki = tbs.ReadEncodedValue();
            var keyReader = new AsnReader(spki, AsnEncodingRules.DER); var key = keyReader.ReadSequence(); keyReader.ThrowIfNotEmpty();
            var parameters = key.ReadSequence();
            if (parameters.ReadObjectIdentifier() != "1.2.840.10045.2.1" || parameters.ReadObjectIdentifier() != "1.2.840.10045.3.1.7") return false;
            parameters.ThrowIfNotEmpty(); byte[] point = key.ReadBitString(out unused); key.ThrowIfNotEmpty();
            if (unused != 0 || point.Length != 65 || point[0] != 4) return false;
            var x = Unsigned(point.AsSpan(1, 32)); var y = Unsigned(point.AsSpan(33, 32));
            if (x >= Prime || y >= Prime || (y * y - (x * x * x - 3 * x + CurveB)) % Prime != 0 ||
                Convert.ToHexString(SHA256.HashData(spki.Span)).ToLowerInvariant() != expected.SpkiSha256) return false;
            // No unique IDs, absent extensions, duplicate extensions, or trailing TBSCertificate data.
            var explicitExtensions = tbs.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 3, true)); tbs.ThrowIfNotEmpty();
            var extensions = explicitExtensions.ReadSequence(); explicitExtensions.ThrowIfNotEmpty();
            var found = new HashSet<string>(StringComparer.Ordinal);
            while (extensions.HasData)
            {
                var extension = extensions.ReadSequence(); string oid = extension.ReadObjectIdentifier();
                if (!found.Add(oid)) return false;
                bool critical = false;
                if (extension.HasData && extension.PeekTag().HasSameClassAndValue(Asn1Tag.Boolean))
                { if (!extension.ReadBoolean()) return false; critical = true; } // DER omits the DEFAULT false value.
                byte[] encoded = extension.ReadOctetString(); extension.ThrowIfNotEmpty();
                var value = new AsnReader(encoded, AsnEncodingRules.DER);
                switch (oid)
                {
                    case "2.5.29.19": // Explicit non-CA, no path length.
                        var constraints = value.ReadSequence(); constraints.ThrowIfNotEmpty(); break;
                    case "2.5.29.15":
                        byte[] usage = value.ReadBitString(out unused);
                        if (usage.Length != 1 || usage[0] != 0x80 || unused != 7) return false; break;
                    case "2.5.29.37":
                        var eku = value.ReadSequence(); if (eku.ReadObjectIdentifier() != "1.3.6.1.5.5.7.3.1") return false;
                        eku.ThrowIfNotEmpty(); break;
                    case "2.5.29.17":
                        var san = value.ReadSequence(); byte[] ip = san.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 7)); san.ThrowIfNotEmpty();
                        if (ip.Length != 4 || !ip.AsSpan().SequenceEqual(IPAddress.Parse(new Uri(expected.Endpoint).Host).GetAddressBytes())) return false; break;
                    default:
                        // Narrow initial profile: even unknown noncritical extensions require a reviewed profile amendment.
                        return false;
                }
                value.ThrowIfNotEmpty();
                if ((oid is "2.5.29.19" or "2.5.29.15") && !critical) return false;
            }
            return found.Count == 4 && signatureVerifier.Verify(spki, signed, signature);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { return false; }
    }
    private static void Algorithm(ReadOnlyMemory<byte> bytes)
    {
        var reader = new AsnReader(bytes, AsnEncodingRules.DER); var algorithm = reader.ReadSequence(); reader.ThrowIfNotEmpty();
        if (algorithm.ReadObjectIdentifier() != SignatureOid) throw new AsnContentException();
        algorithm.ThrowIfNotEmpty(); // ECDSA parameters MUST be absent, including NULL.
    }
    private static ReadOnlyMemory<byte> Name(AsnReader parent)
    {
        var bytes = parent.ReadEncodedValue(); var reader = new AsnReader(bytes, AsnEncodingRules.DER); var name = reader.ReadSequence();
        reader.ThrowIfNotEmpty(); if (!name.HasData) throw new AsnContentException();
        while (name.HasData)
        {
            var set = name.ReadSetOf(); if (!set.HasData) throw new AsnContentException();
            while (set.HasData)
            {
                var attribute = set.ReadSequence(); _ = attribute.ReadObjectIdentifier();
                var tag = attribute.PeekTag();
                if (tag.TagClass != TagClass.Universal || tag.TagValue is not (12 or 19 or 20 or 22 or 30)) throw new AsnContentException();
                string text = attribute.ReadCharacterString((UniversalTagNumber)tag.TagValue);
                if (text.Length is < 1 or > 128 || text.Any(char.IsControl)) throw new AsnContentException();
                attribute.ThrowIfNotEmpty();
            }
        }
        return bytes;
    }
    private static DateTimeOffset Time(AsnReader reader)
    {
        bool utc = reader.PeekTag().HasSameClassAndValue(Asn1Tag.UtcTime);
        var result = utc ? reader.ReadUtcTime() : reader.ReadGeneralizedTime();
        if (utc != (result.Year < 2050)) throw new AsnContentException();
        return result;
    }
}
