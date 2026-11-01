using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using PeripheralBatteryCompanion.Core.TransportSecurity;
using PeripheralBatteryCompanion.NativeAcceptance;

namespace PeripheralBatteryCompanion.Windows;

// Compiled ONLY in the separate harness. No friend access to the production Windows assembly.
internal sealed partial class WindowsPersistentIdentityCustody
{
    internal static WindowsPersistentIdentityCustody CreateExactFixture()
    {
        var s = NativeAdmission.Current;
        if (s.Plan.Id != "identity") throw new InvalidOperationException("Fixture creation scope.");
        NativeAdmission.Demand("custody", "CreateExactFixture");
        if (CngKey.Exists(s.Manifest.KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.Silent))
            throw new InvalidOperationException("Existing exact fixture key; no overwrite.");
        return CreateByLocalOwner(Guid.Parse(s.Manifest.IdentityId));
    }
    internal byte[] IssueFixtureCertificate()
    {
        var s = NativeAdmission.Current;
        if (s.Plan.Id != "identity" || _key.KeyName != s.Manifest.KeyName) throw new InvalidOperationException("Certificate scope.");
        s.Check();
        s.Budget.Charge("sign");
        using var signer = new ECDsaCng(_key);
        var request = new CertificateRequest("CN=PBC isolated acceptance fixture", signer, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Parse("192.168.100.10")); request.CertificateExtensions.Add(san.Build());
        using var cert = request.CreateSelfSigned(ExecutionManifest.Utc(s.Manifest.CertificateNotBeforeUtc), ExecutionManifest.Utc(s.Manifest.CertificateNotAfterUtc));
        byte[] der = cert.Export(X509ContentType.Cert);
        if (der.Length > 4096) throw new InvalidOperationException("Certificate bound."); return der;
    }
    internal byte[] SignFixtureChallenge(byte[] challenge)
    {
        var s = NativeAdmission.Current;
        if (s.Plan.Id != "identity" || _key.KeyName != s.Manifest.KeyName || challenge.Length > 128) throw new InvalidOperationException("Challenge scope.");
        s.Check();
        s.Budget.Charge("sign"); using var signer = new ECDsaCng(_key);
        return signer.SignData(challenge, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }
    internal void DeleteConfirmedFixture(EnrollmentIdentity expected)
    {
        var s = NativeAdmission.Current;
        if (s.Plan.Id != "cleanup" || _key.KeyName != s.Manifest.KeyName || expected.IdentityId.ToString("D") != s.Manifest.IdentityId)
            throw new InvalidOperationException("Delete scope.");
        s.Check();
        _key.Delete();
        if (CngKey.Exists(s.Manifest.KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.Silent))
            throw new InvalidOperationException("Fixture key deletion unconfirmed.");
    }
}

internal sealed partial class WindowsAuthorizationDirectory
{
    internal static WindowsAuthorizationDirectory PinPrivateFixture(string path)
    {
        NativeAdmission.Demand("directory");
        // Read metadata + read/write sharing, never delete sharing. Allows the independently
        // guarded journal's writable directory handle while keeping this ancestry stable.
        return new(path, requirePrivate: true, sharedReadOnlyPin: true);
    }
    internal static void CreateFixtureHardLink(string link, string target)
    {
        NativeAdmission.Demand("directory"); var m = NativeAdmission.Current.Manifest;
        if (link != m.Owned("filesystem/alias.bin") || target != m.Owned("filesystem/target.bin") ||
            !CreateHardLinkW(link, target, IntPtr.Zero)) throw new IOException("Exact fixture hard link failed.");
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string link, string target, IntPtr reserved);
    internal static void VerifyFixtureAccount()
    {
        NativeAdmission.Demand("directory");
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User?.Value != NativeAdmission.Current.Manifest.AccountSid ||
            new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) ||
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) != NativeAdmission.Current.Manifest.LocalAppData)
            throw new InvalidOperationException("Disposable nonadministrator account mismatch.");
    }
}
