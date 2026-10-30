using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PeripheralBatteryCompanion.Core.TransportSecurity;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

// Real public-certificate crypto on in-memory synthetic certificates; no OS certificate store, persisted key or TLS connection.
public sealed class PublicServerCertificateInspectorTests
{
    private const string Authority="pbc-readonly.test";
    private static readonly DateTimeOffset Now=new(2026,10,1,12,0,0,TimeSpan.Zero);
    private static ImmutableArray<byte> Pin(X509Certificate2 certificate)=>ImmutableArray.CreateRange(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));
    private static X509Certificate2 Certificate(string san=Authority,string cn="unrelated.test",bool includeSan=true,bool includeBasic=true,
        bool ca=false,bool serverAuth=true,bool signing=true)
    {
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request=new CertificateRequest("CN="+cn,key,HashAlgorithmName.SHA256);
        if(includeSan){var builder=new SubjectAlternativeNameBuilder();builder.AddDnsName(san);request.CertificateExtensions.Add(builder.Build());}
        if(includeBasic)request.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca,false,0,true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new(serverAuth?"1.3.6.1.5.5.7.3.1":"1.3.6.1.5.5.7.3.2")},true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(signing?X509KeyUsageFlags.DigitalSignature:X509KeyUsageFlags.KeyEncipherment,true));
        using var signed=request.CreateSelfSigned(Now.AddDays(-1),Now.AddDays(30));
        return X509CertificateLoader.LoadCertificate(signed.RawData); // public DER only, never a PFX export or trusted root installation.
    }
    [Fact]
    public void MatchesExactSanSpkiAndPublicServerFieldsButDoesNotClaimPrivateKeyOrTrust()
    {
        using var certificate=Certificate();var result=PublicServerCertificateInspector.Inspect(certificate,Authority,Pin(certificate),Now);
        Assert.Equal(PublicCertificateStatus.PublicFieldsMatch,result.Status);Assert.False(certificate.HasPrivateKey);
        Assert.Equal(32,result.SpkiSha256.Length);Assert.Equal(new DateTimeOffset(certificate.NotBefore.ToUniversalTime()),result.ValidFrom);
    }
    [Fact]
    public void SpkiPinIsNotCertificateFingerprint()
    {
        using var certificate=Certificate();
        var wrong=ImmutableArray.CreateRange(SHA256.HashData(certificate.RawData));
        Assert.Equal(PublicCertificateStatus.PinMismatch,PublicServerCertificateInspector.Inspect(certificate,Authority,wrong,Now).Status);
    }
    [Fact]
    public void SameHostnameWithRotatedKeyIsNotAutomaticallyTrusted()
    {
        using var old=Certificate();using var replacement=Certificate();
        Assert.Equal(PublicCertificateStatus.PinMismatch,PublicServerCertificateInspector.Inspect(replacement,Authority,Pin(old),Now).Status);
    }
    [Fact]
    public void CommonNameNeverOverridesWrongSan()
    {
        using var certificate=Certificate(san:"wrong.test",cn:Authority);
        Assert.Equal(PublicCertificateStatus.SanMismatch,PublicServerCertificateInspector.Inspect(certificate,Authority,Pin(certificate),Now).Status);
    }
    [Fact]
    public void WildcardSanIsNotAcceptedAsExactAuthority()
    {
        using var certificate=Certificate(san:"*.readonly.test");
        Assert.Equal(PublicCertificateStatus.SanMismatch,PublicServerCertificateInspector.Inspect(certificate,"pbc.readonly.test",Pin(certificate),Now).Status);
    }
    [Theory]
    [InlineData(-2)] [InlineData(30)] [InlineData(31)]
    public void NotYetValidAndExpiredAreRejected(int days)
    {
        using var certificate=Certificate();
        Assert.Equal(PublicCertificateStatus.OutsideValidity,PublicServerCertificateInspector.Inspect(certificate,Authority,Pin(certificate),Now.AddDays(days)).Status);
    }
    [Fact]
    public void RequiredExtensionsAreNotInventedFromCommonNameOrDefaults()
    {
        using var missingSan=Certificate(cn:Authority,includeSan:false);using var missingBasic=Certificate(includeBasic:false);
        Assert.Equal(PublicCertificateStatus.MalformedCertificate,PublicServerCertificateInspector.Inspect(missingSan,Authority,Pin(missingSan),Now).Status);
        Assert.Equal(PublicCertificateStatus.MalformedCertificate,PublicServerCertificateInspector.Inspect(missingBasic,Authority,Pin(missingBasic),Now).Status);
    }
    [Fact]
    public void CaClientOnlyAndNonSigningCertificatesAreRejected()
    {
        using var ca=Certificate(ca:true);using var client=Certificate(serverAuth:false);using var nonsigning=Certificate(signing:false);
        Assert.Equal(PublicCertificateStatus.NotServerLeaf,PublicServerCertificateInspector.Inspect(ca,Authority,Pin(ca),Now).Status);
        Assert.Equal(PublicCertificateStatus.MissingServerAuth,PublicServerCertificateInspector.Inspect(client,Authority,Pin(client),Now).Status);
        Assert.Equal(PublicCertificateStatus.MissingSigningUsage,PublicServerCertificateInspector.Inspect(nonsigning,Authority,Pin(nonsigning),Now).Status);
    }
    [Fact]
    public void InvalidExpectationAndDisposedCertificateFailSafely()
    {
        using var certificate=Certificate();var pin=Pin(certificate);
        Assert.Equal(PublicCertificateStatus.InvalidExpectation,PublicServerCertificateInspector.Inspect(certificate,"*",pin,Now).Status);
        Assert.Equal(PublicCertificateStatus.InvalidExpectation,PublicServerCertificateInspector.Inspect(certificate,Authority,default,Now).Status);
        Assert.Equal(PublicCertificateStatus.InvalidExpectation,PublicServerCertificateInspector.Inspect(certificate,Authority,ImmutableArray.CreateRange(new byte[32]),Now).Status);
        certificate.Dispose();
        Assert.Equal(PublicCertificateStatus.MalformedCertificate,PublicServerCertificateInspector.Inspect(certificate,Authority,pin,Now).Status);
    }
}
