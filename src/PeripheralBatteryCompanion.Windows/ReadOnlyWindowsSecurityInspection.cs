using System.Collections.Immutable;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PeripheralBatteryCompanion.Core.TransportSecurity;

namespace PeripheralBatteryCompanion.Windows;

internal enum NativeInspectionStatus { Inspected, InvalidSelection, NotFound, Ambiguous, Unavailable }
internal enum KeyProtectionAssessment { NotInspected }
internal sealed record InterfaceAddressInspection(NativeInspectionStatus Status, LocalInterfaceEvidence? Evidence, bool? IsWireless);
internal sealed record SelectedCertificateInspection(NativeInspectionStatus Status, PublicCertificateAssessment? PublicFields,
    bool? PrivateKeyAssociationReported, KeyProtectionAssessment KeyProtection = KeyProtectionAssessment.NotInspected);

// Explicit read-only entry points. Not IWindowsSecurityEvidenceSource: profile, key ACL and DPAPI evidence remain unverified.
internal static class ReadOnlyWindowsSecurityInspection
{
    public static InterfaceAddressInspection InspectInterfaceAddress(string interfaceId, string address)
    {
        if (string.IsNullOrWhiteSpace(interfaceId) || interfaceId.Length > 128 || interfaceId.Any(char.IsControl) ||
            !IPAddress.TryParse(address,out var selected) ||
            !string.Equals(selected.ToString(),address,StringComparison.OrdinalIgnoreCase))
            return new(NativeInspectionStatus.InvalidSelection,null,null);
        try
        {
            var matches = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.Id == interfaceId).ToArray();
            if (matches.Length != 1) return new(matches.Length == 0 ? NativeInspectionStatus.NotFound : NativeInspectionStatus.Ambiguous,null,null);
            var nic = matches[0];
            if (!nic.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(selected)))
                return new(NativeInspectionStatus.NotFound,null,null);
            // Assigned/private-looking IP is never proof of Windows NLM Private profile or authorization.
            return new(NativeInspectionStatus.Inspected,new(interfaceId,address,nic.OperationalStatus == OperationalStatus.Up,NetworkTrustProfile.Unknown),
                nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
        }
        catch (Exception error) when (error is NetworkInformationException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
        { return new(NativeInspectionStatus.Unavailable,null,null); }
    }

    public static SelectedCertificateInspection InspectCurrentUserCertificate(string thumbprint, string dnsName,
        ImmutableArray<byte> expectedSpkiSha256, DateTimeOffset now)
    {
        // Thumbprint is an exact local store selector only, not a security pin. Do not choose the first subject/name match.
        if (thumbprint is null || thumbprint.Length != 40 || !thumbprint.All(char.IsAsciiHexDigit) ||
            !PublicServerCertificateInspector.ValidExpectation(dnsName,expectedSpkiSha256))
            return new(NativeInspectionStatus.InvalidSelection,null,null);
        var owned = new HashSet<X509Certificate2>(ReferenceEqualityComparer.Instance);
        try
        {
            using var store = new X509Store(StoreName.My,StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            var certificates = store.Certificates;
            foreach (var certificate in certificates) owned.Add(certificate);
            // validOnly=false deliberately avoids implicit chain/revocation fetching; public checks below are offline only.
            var matches = certificates.Find(X509FindType.FindByThumbprint,thumbprint,validOnly:false);
            foreach (var certificate in matches) owned.Add(certificate);
            if (matches.Count != 1) return new(matches.Count == 0 ? NativeInspectionStatus.NotFound : NativeInspectionStatus.Ambiguous,null,null);
            var selected = matches[0];
            var result = PublicServerCertificateInspector.Inspect(selected,dnsName,expectedSpkiSha256,now);
            // HasPrivateKey is reported association metadata only. Never open/sign/export a key or infer nonexportability/ACLs.
            return new(NativeInspectionStatus.Inspected,result,selected.HasPrivateKey);
        }
        catch (Exception error) when (error is CryptographicException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
        { return new(NativeInspectionStatus.Unavailable,null,null); }
        finally { foreach (var certificate in owned) certificate.Dispose(); }
    }
}
