using PeripheralBatteryCompanion.Windows;
using Xunit;

namespace PeripheralBatteryCompanion.Windows.Tests;

public sealed class WindowsProtocolSecurityHoldTests
{
    [Fact]
    public void CompositionAndChannelAssociationDenyBeforeAnyArgumentOrNativeAccess()
    {
        Assert.Throws<InvalidOperationException>(() => WindowsProtocolSecurityAdapters.OpenHeld(null!, null!, null!));
        Assert.Throws<InvalidOperationException>(() => WindowsServerChannel.CreateHeld(null!));
    }
    [Fact]
    public void SignatureAndEntropyHoldBeforeProvidersAndSecretStore()
    {
        Assert.Throws<InvalidOperationException>(() => new WindowsEnrolledCertificateSignature().Verify(default, default, default));
        var entropy = new WindowsProtocolEntropy(); var store = new Sink();
        Assert.Throws<InvalidOperationException>(() => entropy.NewRequestNonce());
        Assert.Throws<InvalidOperationException>(() => entropy.NewCredentialVerifier(store));
        Assert.Throws<InvalidOperationException>(() => entropy.NewBootstrapVerifier(store));
        Assert.Equal(0, store.Calls);
    }
    private sealed class Sink : IProtectedProtocolSecretStore, IEphemeralBootstrapDelivery
    {
        internal int Calls;
        public void Store(ReadOnlySpan<byte> secret) => Calls++;
        public void DeliverInMemory(ReadOnlySpan<byte> secret) => Calls++;
    }
}
