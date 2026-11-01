using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using PeripheralBatteryCompanion.Core.TransportSecurity;

namespace PeripheralBatteryCompanion.Windows;

/// <summary>Persisted CurrentUser Software-KSP foundation, held before ANY CNG call. No certificate/TLS adapter.</summary>
internal sealed partial class WindowsPersistentIdentityCustody : IDisposable
{
    private readonly CngKey _key;
    private WindowsPersistentIdentityCustody(CngKey key) => _key = key;
    internal static string ReviewedKeyName(Guid identityGeneration)
    {
        if (identityGeneration == Guid.Empty) throw new ArgumentException("Identity generation required.");
        return "PBC.Identity." + identityGeneration.ToString("D");
    }
    internal static WindowsPersistentIdentityCustody CreateByLocalOwner(Guid identityGeneration)
    {
        RequireNativeApproval();
        string name = ReviewedKeyName(identityGeneration);
        using var current = WindowsIdentity.GetCurrent();
        var owner = current.User ?? throw new InvalidOperationException("Current user unavailable.");
        var descriptor = new RawSecurityDescriptor($"O:{owner.Value}D:P(A;;0x001f019b;;;{owner.Value})");
        byte[] bytes = new byte[descriptor.BinaryLength]; descriptor.GetBinaryForm(bytes, 0);
        var parameters = new CngKeyCreationParameters
        {
            Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
            ExportPolicy = CngExportPolicies.None, KeyUsage = CngKeyUsages.Signing,
            KeyCreationOptions = CngKeyCreationOptions.None, ParentWindowHandle = IntPtr.Zero,
            UIPolicy = new CngUIPolicy(CngUIProtectionLevels.None)
        };
        // OWNER | DACL selectors; raw descriptor requests protected owner-only access.
        // Provider application/readback must be demonstrated before any native approval.
        parameters.Parameters.Add(new CngProperty("Security Descr", bytes, (CngPropertyOptions)5));
        CngKey? key = null;
        try
        {
            // No overwrite/machine flags, existence scan, fallback provider or silent retry.
            key = CngKey.Create(CngAlgorithm.ECDsaP256, name, parameters);
            Verify(key, name, owner);
            return new(key);
        }
        catch
        {
            key?.Dispose();
            // A partially created named resource is retained for exact-owner review.
            throw new InvalidOperationException("Identity creation/verification failed; named-resource persistence is unconfirmed.");
        }
    }
    internal static WindowsPersistentIdentityCustody OpenReviewed(EnrollmentIdentity expected)
    {
        RequireNativeApproval();
        ArgumentNullException.ThrowIfNull(expected);
        string name = ReviewedKeyName(expected.IdentityId);
        using var current = WindowsIdentity.GetCurrent();
        var owner = current.User ?? throw new InvalidOperationException("Current user unavailable.");
        var key = CngKey.Open(name, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.Silent);
        try
        {
            Verify(key, name, owner);
            using var signer = new ECDsaCng(key);
            byte[] spki = signer.ExportSubjectPublicKeyInfo();
            if (spki.Length is < 1 or > 512 || Convert.ToHexString(SHA256.HashData(spki)).ToLowerInvariant() != expected.SpkiSha256)
                throw new InvalidOperationException("Identity public binding rejected.");
            return new(key);
        }
        catch { key.Dispose(); throw new InvalidOperationException("Identity custody verification refused."); }
    }
    private static void Verify(CngKey key, string name, SecurityIdentifier owner)
    {
        if (key.KeyName != name || key.IsEphemeral || key.IsMachineKey || key.Provider != CngProvider.MicrosoftSoftwareKeyStorageProvider ||
            key.ExportPolicy != CngExportPolicies.None || key.KeyUsage != CngKeyUsages.Signing || key.Algorithm != CngAlgorithm.ECDsaP256)
            throw new InvalidOperationException("Identity metadata rejected.");
        using var signer = new ECDsaCng(key);
        if (signer.KeySize != 256) throw new InvalidOperationException("Identity curve rejected.");
        if (NCryptGetProperty(key.Handle, "Security Descr", null, 0, out int length, 5) != 0 || length is < 1 or > 8192)
            throw new InvalidOperationException("Identity security descriptor unavailable.");
        byte[] bytes = new byte[length];
        if (NCryptGetProperty(key.Handle, "Security Descr", bytes, bytes.Length, out int actual, 5) != 0 || actual != bytes.Length)
            throw new InvalidOperationException("Identity descriptor readback rejected.");
        var descriptor = new RawSecurityDescriptor(bytes, 0);
        if (!OwnerOnlyDescriptor(descriptor, owner)) throw new InvalidOperationException("Identity ACL rejected.");
    }
    internal static bool OwnerOnlyDescriptor(RawSecurityDescriptor descriptor, SecurityIdentifier owner) =>
        descriptor.Owner == owner && descriptor.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclPresent) &&
        descriptor.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected) &&
        descriptor.DiscretionaryAcl is { Count: 1 } dacl && dacl[0] is CommonAce ace && !ace.IsCallback &&
        ace.AceQualifier == AceQualifier.AccessAllowed && ace.SecurityIdentifier == owner &&
        ace.AccessMask == 0x001f019b && ace.AceFlags == AceFlags.None;

    internal byte[] ExportPublicSpki()
    {
        RequireNativeApproval();
        using var signer = new ECDsaCng(_key);
        byte[] result = signer.ExportSubjectPublicKeyInfo();
        if (result.Length is < 1 or > 512) throw new InvalidOperationException("Identity public buffer bound rejected.");
        return result;
    }
    // No private export, deletion, enumeration, issuance route or automatic certificate creation.
    public void Dispose() => _key.Dispose(); // Handle disposal is NOT named-key deletion proof.
    private static void RequireNativeApproval([System.Runtime.CompilerServices.CallerMemberName] string operation = "")
    {
#if GATE_E_ACCEPTANCE_HARNESS
        PeripheralBatteryCompanion.NativeAcceptance.NativeAdmission.Demand("custody", operation);
#else
        throw new InvalidOperationException("GATE E BLOCKED: persisted identity custody requires exact owner approval, provenance and native validation.");
#endif
    }
    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
    private static extern int NCryptGetProperty(SafeNCryptKeyHandle key, string name, byte[]? output,
        int outputLength, out int resultLength, int flags);
}
