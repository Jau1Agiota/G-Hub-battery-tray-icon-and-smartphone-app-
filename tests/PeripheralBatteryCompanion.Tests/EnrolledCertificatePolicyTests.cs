using System.Formats.Asn1;
using System.Numerics;
using System.Security.Authentication;
using System.Security.Cryptography;
using PeripheralBatteryCompanion.Core.TransportSecurity;
using PeripheralBatteryCompanion.Core.SnapshotService;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class EnrolledCertificatePolicyTests
{
    [Fact]
    public async Task CertificatePolicyFeedsPairingAndRequestReleaseWithRealJournalTransactions()
    {
        var certificate = new CertificateFixture(); using var f = new SecurityProtocolFixture(false, certificate.Identity);
        var observation = certificate.Channel;
        f.Proof.Check = (identity, peer, now) => Tls13ChannelPolicy.Validate(identity, peer, observation, now, certificate);
        using var pairing = f.Pairing();
        Assert.True(pairing.OpenByLocalOwner(SecurityProtocolFixture.EnrollmentId, SHA256.HashData(SecurityProtocolFixture.Bootstrap)));
        Assert.True(pairing.TryClaim(f.Peer, SecurityProtocolFixture.BootstrapHeader, f.Claim));
        Assert.True(pairing.ConfirmByLocalOwner(f.Claim.TranscriptSha256(f.Identity)));
        Assert.True(f.Authorization.ApproveExposureByLocalOwner());
        using var server = f.Server();
        Assert.Equal(200, server.TryPrepare(f.Peer, "GET", SnapshotRequestCore.SnapshotPath, 0,
            SecurityProtocolFixture.RequestHeaders(SecurityProtocolFixture.Nonce(1)), out var prepared));
        using var response = prepared!;
        observation = observation with { ApplicationProtocol = "h2" }; bool wrote = false;
        Assert.False(await response.SendAsync((_, _) => { wrote = true; return Task.CompletedTask; }));
        Assert.False(wrote); Assert.Equal(0, server.ActiveResponses);
    }
    [Fact]
    public void ExactProfileReachesSignatureBoundaryWithExactSignedBytes()
    {
        var f = new CertificateFixture();
        Assert.True(EnrolledCertificatePolicy.Validate(f.Identity, f.Der, CertificateFixture.Now, f));
        Assert.Equal(1, f.Calls);
        f.Allow = false;
        Assert.False(EnrolledCertificatePolicy.Validate(f.Identity, f.Der, CertificateFixture.Now, f));
    }
    [Theory]
    [InlineData("ca")]
    [InlineData("usage")]
    [InlineData("eku")]
    [InlineData("dns")]
    [InlineData("ip")]
    [InlineData("two-san")]
    [InlineData("curve")]
    [InlineData("point")]
    [InlineData("pin")]
    [InlineData("signature-algorithm")]
    [InlineData("signature-parameters")]
    [InlineData("inner-algorithm")]
    [InlineData("issuer")]
    [InlineData("serial")]
    [InlineData("duplicate")]
    [InlineData("unknown-critical")]
    [InlineData("unknown-optional")]
    [InlineData("missing")]
    [InlineData("noncritical-usage")]
    [InlineData("explicit-false")]
    [InlineData("signature-zero")]
    [InlineData("trailing")]
    public void MalformedOrSubstitutedProfilesDenyBeforeSignature(string variant)
    {
        var f = new CertificateFixture(variant);
        Assert.False(EnrolledCertificatePolicy.Validate(f.Identity, f.Der, CertificateFixture.Now, f));
        Assert.Equal(0, f.Calls);
    }
    [Theory]
    [InlineData(-2)]
    [InlineData(32)]
    public void CertificateValidityIsNotWaivedByPinOrSignature(int days)
    {
        var f = new CertificateFixture();
        Assert.False(EnrolledCertificatePolicy.Validate(f.Identity, f.Der, CertificateFixture.Now.AddDays(days), f));
        Assert.Equal(0, f.Calls);
    }
    [Fact]
    public void LeafSubstitutionAndSignatureFailureDeny()
    {
        var f = new CertificateFixture(); byte[] altered = f.Der.ToArray(); altered[^1] ^= 1;
        Assert.False(EnrolledCertificatePolicy.Validate(f.Identity, altered, CertificateFixture.Now, f));
        var bound = new EnrollmentIdentity(f.Identity.IdentityId, f.Identity.Endpoint, f.Identity.SpkiSha256, altered, f.Identity.LeafNotBefore, f.Identity.LeafNotAfter);
        Assert.False(EnrolledCertificatePolicy.Validate(bound, altered, CertificateFixture.Now, f));
    }
    [Theory]
    [InlineData("tls12")]
    [InlineData("unauthenticated")]
    [InlineData("clear")]
    [InlineData("unsigned")]
    [InlineData("alpn")]
    [InlineData("origin")]
    [InlineData("leaf")]
    [InlineData("identity")]
    public void LiveChannelPolicyRejectsBadObservations(string variant)
    {
        var f = new CertificateFixture(); var observation = f.Channel;
        observation = variant switch
        {
            "tls12" => observation with { Protocol = SslProtocols.Tls12 },
            "unauthenticated" => observation with { Authenticated = false },
            "clear" => observation with { Encrypted = false },
            "unsigned" => observation with { Signed = false },
            "alpn" => observation with { ApplicationProtocol = "h2" },
            "origin" => observation with { ServerEndpoint = "https://192.168.100.11:8443" },
            "leaf" => observation with { ServerLeaf = new byte[] { 1, 2, 3 } },
            _ => observation
        };
        var peer = new PeerBinding(f.Identity.Endpoint, variant == "identity" ? Guid.Empty : f.Identity.IdentityId, f.Der);
        Assert.False(Tls13ChannelPolicy.Validate(f.Identity, peer, observation, CertificateFixture.Now, f));
    }
    [Fact]
    public void SyntheticTlsObservationDoesNotMintProductionPeerEvidence()
    {
        var f = new CertificateFixture(); var peer = new PeerBinding(f.Identity.Endpoint, f.Identity.IdentityId, f.Der);
        Assert.True(Tls13ChannelPolicy.Validate(f.Identity, peer, f.Channel, CertificateFixture.Now, f));
        Assert.False(new DenyUnverifiedPeer().IsAuthenticatedPeer(f.Identity, peer, CertificateFixture.Now));
    }
}

/// <summary>
/// Deterministic PUBLIC fixture: P-256 generator point, publicly known scalar=nonce=1.
/// Test-only integer formula builds reproducible DER; no key provider, RNG, real credential or signing API.
/// Signature callback checks fixed bytes, NOT a substitute for native ECDSA verification acceptance.
/// </summary>
internal sealed class CertificateFixture : IEnrolledCertificateSignature
{
    internal static readonly DateTimeOffset Now = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private const string SignatureOid = "1.2.840.10045.4.3.2";
    private static readonly byte[] Point = Convert.FromHexString("046B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C2964FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5");
    private readonly byte[] _spki, _tbs, _signature;
    internal byte[] Der { get; }
    internal EnrollmentIdentity Identity { get; }
    internal bool Allow = true;
    internal int Calls;
    internal Tls13ChannelObservation Channel => new(true, true, true, true, SslProtocols.Tls13, "http/1.1", Identity.Endpoint, Der);
    private static byte[] Sequence(Action<AsnWriter> body)
    { var w = new AsnWriter(AsnEncodingRules.DER); w.PushSequence(); body(w); w.PopSequence(); return w.Encode(); }
    private static byte[] Algorithm(string oid = SignatureOid, bool parameters = false) => Sequence(w => { w.WriteObjectIdentifier(oid); if (parameters) w.WriteNull(); });
    private static byte[] Name(string text) => Sequence(w =>
    { w.PushSetOf(); w.WriteEncodedValue(Sequence(a => { a.WriteObjectIdentifier("2.5.4.3"); a.WriteCharacterString(UniversalTagNumber.UTF8String, text); })); w.PopSetOf(); });
    internal CertificateFixture(string variant = "")
    {
        _spki = Sequence(w =>
        {
            w.WriteEncodedValue(Sequence(a => { a.WriteObjectIdentifier("1.2.840.10045.2.1"); a.WriteObjectIdentifier(variant == "curve" ? "1.3.132.0.34" : "1.2.840.10045.3.1.7"); }));
            byte[] point = Point.ToArray(); if (variant == "point") point[^1] ^= 1; w.WriteBitString(point);
        });
        byte[] algorithm = Algorithm(variant == "signature-algorithm" ? "1.2.840.113549.1.1.11" : SignatureOid, variant == "signature-parameters");
        _tbs = Sequence(w =>
        {
            var v = new Asn1Tag(TagClass.ContextSpecific, 0, true); w.PushSequence(v); w.WriteInteger(2); w.PopSequence(v);
            w.WriteInteger(variant == "serial" ? 0 : 1);
            w.WriteEncodedValue(variant == "inner-algorithm" ? Algorithm("1.2.840.113549.1.1.11") : algorithm);
            w.WriteEncodedValue(Name("PBC public test fixture"));
            w.WriteEncodedValue(Sequence(a => { a.WriteUtcTime(Now.AddDays(-1)); a.WriteUtcTime(Now.AddDays(31)); }));
            w.WriteEncodedValue(Name(variant == "issuer" ? "Other test fixture" : "PBC public test fixture")); w.WriteEncodedValue(_spki);
            var e = new Asn1Tag(TagClass.ContextSpecific, 3, true); w.PushSequence(e); w.PushSequence();
            void Extension(string oid, bool critical, byte[] value)
            {
                w.WriteEncodedValue(Sequence(a => { a.WriteObjectIdentifier(oid); if (critical || variant == "explicit-false") a.WriteBoolean(critical); a.WriteOctetString(value); }));
            }
            Extension("2.5.29.19", true, Sequence(a => { if (variant == "ca") a.WriteBoolean(true); }));
            var bits = new AsnWriter(AsnEncodingRules.DER); bits.WriteBitString(new byte[] { variant == "usage" ? (byte)0xA0 : (byte)0x80 }, variant == "usage" ? 5 : 7);
            Extension("2.5.29.15", variant != "noncritical-usage", bits.Encode());
            Extension("2.5.29.37", false, Sequence(a => a.WriteObjectIdentifier(variant == "eku" ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1")));
            byte[] san = Sequence(a =>
            {
                if (variant == "dns") a.WriteCharacterString(UniversalTagNumber.IA5String, "example.invalid", new Asn1Tag(TagClass.ContextSpecific, 2));
                else a.WriteOctetString(new byte[] { 192, 168, 100, variant == "ip" ? (byte)11 : (byte)10 }, new Asn1Tag(TagClass.ContextSpecific, 7));
                if (variant == "two-san") a.WriteOctetString(new byte[] { 192, 168, 100, 10 }, new Asn1Tag(TagClass.ContextSpecific, 7));
            });
            if (variant != "missing") Extension("2.5.29.17", false, san);
            if (variant == "duplicate") Extension("2.5.29.17", false, san);
            if (variant.StartsWith("unknown-", StringComparison.Ordinal)) Extension("1.2.3.4", variant == "unknown-critical", new byte[] { 5, 0 });
            w.PopSequence(); w.PopSequence(e);
        });
        var order = new BigInteger(Convert.FromHexString("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551"), true, true);
        var r = new BigInteger(Point.AsSpan(1, 32), true, true);
        var s = (new BigInteger(SHA256.HashData(_tbs), true, true) + r) % order;
        _signature = Sequence(w => { w.WriteInteger(variant == "signature-zero" ? BigInteger.Zero : r); w.WriteInteger(s); });
        Der = Sequence(w => { w.WriteEncodedValue(_tbs); w.WriteEncodedValue(algorithm); w.WriteBitString(_signature); });
        if (variant == "trailing") Der = Der.Concat(new byte[] { 0 }).ToArray();
        Identity = new(Guid.Parse("12345678-1234-4234-8234-123456789012"), "https://192.168.100.10:8443",
            variant == "pin" ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(_spki)).ToLowerInvariant(), Der, Now.AddDays(-1), Now.AddDays(31));
    }
    public bool Verify(ReadOnlyMemory<byte> spki, ReadOnlyMemory<byte> signedBytes, ReadOnlyMemory<byte> signature)
    {
        Calls++; return Allow && spki.Span.SequenceEqual(_spki) && signedBytes.Span.SequenceEqual(_tbs) && signature.Span.SequenceEqual(_signature);
    }
}
