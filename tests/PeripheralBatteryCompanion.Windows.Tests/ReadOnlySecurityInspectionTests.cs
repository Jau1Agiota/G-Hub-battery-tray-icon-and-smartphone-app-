using System.Collections.Immutable;
using System.Net.NetworkInformation;
using PeripheralBatteryCompanion.Core.TransportSecurity;
using PeripheralBatteryCompanion.Windows;
using Xunit;

namespace PeripheralBatteryCompanion.Windows.Tests;

// Actual read-only Windows APIs only. No certificate/key provisioning, store writes, profile changes or listener.
public sealed class ReadOnlySecurityInspectionTests
{
    private static readonly ImmutableArray<byte> SyntheticPin=ImmutableArray.CreateRange(Enumerable.Repeat((byte)1,32));
    [Fact]
    public void InvalidSelectionDoesNotInspectOrCreateIdentity()
    {
        var result=ReadOnlyWindowsSecurityInspection.InspectCurrentUserCertificate("", "pbc-readonly.test",SyntheticPin,DateTimeOffset.UtcNow);
        Assert.Equal(NativeInspectionStatus.InvalidSelection,result.Status);Assert.Null(result.PublicFields);
        Assert.Null(result.PrivateKeyAssociationReported);Assert.Equal(KeyProtectionAssessment.NotInspected,result.KeyProtection);
    }
    [Fact]
    public void MissingCurrentUserCertificateNeverCreatesReplacementOrClaimsProtection()
    {
        var result=ReadOnlyWindowsSecurityInspection.InspectCurrentUserCertificate(new string('F',40),"pbc-readonly.test",SyntheticPin,DateTimeOffset.UtcNow);
        Assert.Contains(result.Status,new[]{NativeInspectionStatus.NotFound,NativeInspectionStatus.Unavailable});
        Assert.Null(result.PublicFields);Assert.Null(result.PrivateKeyAssociationReported);
        Assert.Equal(KeyProtectionAssessment.NotInspected,result.KeyProtection);
    }
    [Fact]
    public void MissingInterfaceIsNotAProfileOrBindingApproval()
    {
        var result=ReadOnlyWindowsSecurityInspection.InspectInterfaceAddress("synthetic-interface-not-present","192.0.2.1");
        Assert.Contains(result.Status,new[]{NativeInspectionStatus.NotFound,NativeInspectionStatus.Unavailable});Assert.Null(result.Evidence);
    }
    [Fact]
    public void ActualAssignedAddressesNeverSelfAssertPrivateProfile()
    {
        var interfaces=NetworkInterface.GetAllNetworkInterfaces();
        foreach(var nic in interfaces)
            foreach(var address in nic.GetIPProperties().UnicastAddresses) {
                var result=ReadOnlyWindowsSecurityInspection.InspectInterfaceAddress(nic.Id,address.Address.ToString());
                if(result.Status==NativeInspectionStatus.Inspected) {
                    Assert.NotNull(result.Evidence);Assert.Equal(NetworkTrustProfile.Unknown,result.Evidence.Profile);
                    Assert.Equal(nic.Id,result.Evidence.InterfaceId);Assert.Equal(address.Address.ToString(),result.Evidence.Address);
                } else Assert.Contains(result.Status,new[]{NativeInspectionStatus.NotFound,NativeInspectionStatus.Unavailable});
            }
        // An empty/offline machine is not silently promoted to Private.
        var missing=ReadOnlyWindowsSecurityInspection.InspectInterfaceAddress("synthetic-interface-not-present","192.0.2.1");
        Assert.Null(missing.Evidence);
    }
}
