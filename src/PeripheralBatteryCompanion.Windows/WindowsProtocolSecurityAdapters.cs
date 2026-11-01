using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PeripheralBatteryCompanion.Core.SnapshotService;
using PeripheralBatteryCompanion.Core.TransportSecurity;

namespace PeripheralBatteryCompanion.Windows;

internal static class WindowsProtocolExecutionHold
{
    internal static void Demand() => throw new InvalidOperationException(
        "GATE E BLOCKED: certificate/TLS/entropy adapters require separately reviewed native execution approval.");
}

/// <summary>Held transport association; the stream is constructed over this exact socket, never a caller-asserted endpoint.</summary>
internal sealed class WindowsServerChannel : IDisposable
{
    internal readonly Socket Socket;
    internal readonly SslStream Stream;
    private WindowsServerChannel(Socket socket) { Socket = socket; Stream = new SslStream(new NetworkStream(socket, ownsSocket: false), false); }
    internal static WindowsServerChannel CreateHeld(Socket alreadyConnectedOwnedSocket)
    {
        WindowsProtocolExecutionHold.Demand();
        if (!alreadyConnectedOwnedSocket.Connected) throw new InvalidOperationException("Connected owned socket required.");
        return new(alreadyConnectedOwnedSocket);
    }
    internal SslStream ForSeparatelyApprovedHandshake() { WindowsProtocolExecutionHold.Demand(); return Stream; }
    public void Dispose() { try { Stream.Dispose(); } finally { Socket.Dispose(); } }
}

/// <summary>Non-listening composition only. Every entry point denies before journal, channel, RNG or key access.</summary>
internal interface IWindowsProtocolSecurity : IDisposable
{
    PeerBinding Binding { get; }
    PairingSession CreatePairing();
    AuthenticatedSnapshotCoordinator CreateSnapshotRequests(IReadOnlyPublishedSnapshot snapshots);
    bool ApproveExposureByLocalOwner();
    bool RevokeByLocalOwner();
}

internal sealed class WindowsProtocolSecurityAdapters : IWindowsProtocolSecurity, IPeerCertificateVerifier
{
    private readonly EnrollmentIdentity _identity;
    private readonly EnrollmentAuthorization _authorization;
    private readonly WindowsServerChannel _channel;
    private readonly TimeProvider _clock;
    private readonly PeerBinding _binding;
    private bool _disposed;
    private WindowsProtocolSecurityAdapters(EnrollmentIdentity identity, EnrollmentAuthorization authorization,
        WindowsServerChannel channel, TimeProvider clock)
    {
        _identity = identity; _authorization = authorization; _channel = channel;
        _clock = clock;
        _binding = new(identity.Endpoint, identity.IdentityId, identity.LeafDer.AsSpan());
    }
    internal static IWindowsProtocolSecurity OpenHeld(string journalPath, EnrollmentIdentity identity,
        WindowsServerChannel existingServerChannel, TimeProvider? clock = null)
    {
        WindowsProtocolExecutionHold.Demand();
        ArgumentNullException.ThrowIfNull(existingServerChannel);
        // Does not connect, authenticate, allocate a listener or assume channel ownership.
        var journal = WindowsAuthorizationJournal.OpenExisting(journalPath, identity);
        try
        {
            var authorization = new EnrollmentAuthorization(identity, journal, clock);
            return new WindowsProtocolSecurityAdapters(identity, authorization, existingServerChannel, clock ?? TimeProvider.System);
        }
        catch { journal.Dispose(); throw; }
    }
    public PeerBinding Binding { get { WindowsProtocolExecutionHold.Demand(); return _binding; } }
    public PairingSession CreatePairing()
    {
        WindowsProtocolExecutionHold.Demand(); ObjectDisposedException.ThrowIf(_disposed, this);
        return new(_identity, _authorization, _clock, this);
    }
    public AuthenticatedSnapshotCoordinator CreateSnapshotRequests(IReadOnlyPublishedSnapshot snapshots)
    {
        WindowsProtocolExecutionHold.Demand(); ObjectDisposedException.ThrowIf(_disposed, this);
        return new(_identity, _authorization, snapshots, _clock, this);
    }
    public bool ApproveExposureByLocalOwner() { WindowsProtocolExecutionHold.Demand(); return !_disposed && _authorization.ApproveExposureByLocalOwner(); }
    public bool RevokeByLocalOwner() { WindowsProtocolExecutionHold.Demand(); return !_disposed && _authorization.RevokeByLocalOwner(); }
    public bool IsAuthenticatedPeer(EnrollmentIdentity expected, PeerBinding observed, DateTimeOffset now)
    {
        WindowsProtocolExecutionHold.Demand();
        try
        {
            var stream = _channel.Stream;
            if (_disposed || !ReferenceEquals(observed, _binding) || !_identity.Matches(expected) || !_channel.Socket.Connected || !stream.IsServer ||
                stream.RemoteCertificate is not null || stream.IsMutuallyAuthenticated || _channel.Socket.LocalEndPoint is not IPEndPoint endpoint) return false;
            // Server role: verify its LOCAL server certificate and socket endpoint; no invented phone mTLS.
            var local = stream.LocalCertificate;
            if (local is null) return false;
            string origin = $"https://{endpoint.Address}:{endpoint.Port}";
            var observation = new Tls13ChannelObservation(stream.IsAuthenticated, stream.IsEncrypted, stream.IsSigned, true,
                stream.SslProtocol, stream.NegotiatedApplicationProtocol.ToString(), origin, local.GetRawCertData());
            return Tls13ChannelPolicy.Validate(expected, observed, observation, now, new WindowsEnrolledCertificateSignature());
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { return false; }
    }
    public void Dispose()
    {
        // No channel close/cleanup claim: the future transport owns its socket/channel lifetime.
        if (_disposed) return; _disposed = true; _authorization.Dispose();
    }
}

internal sealed class WindowsEnrolledCertificateSignature : IEnrolledCertificateSignature
{
    public bool Verify(ReadOnlyMemory<byte> spki, ReadOnlyMemory<byte> signedBytes, ReadOnlyMemory<byte> signature)
    {
        WindowsProtocolExecutionHold.Demand();
        try
        {
            if (spki.Length is < 1 or > 256 || signedBytes.Length is < 1 or > 4096 || signature.Length is < 8 or > 72) return false;
            using var verifier = ECDsa.Create(); verifier.ImportSubjectPublicKeyInfo(spki.Span, out int read);
            return read == spki.Length && verifier.KeySize == 256 &&
                verifier.VerifyData(signedBytes.Span, signature.Span, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { return false; }
    }
}

/// <summary>Trusted storage must protect before returning. A thrown store may have partially persisted; no implicit retry/delete.</summary>
internal interface IProtectedProtocolSecretStore
{
    void Store(ReadOnlySpan<byte> secret);
}
internal interface IEphemeralBootstrapDelivery
{
    void DeliverInMemory(ReadOnlySpan<byte> secret);
}

internal interface IWindowsProtocolEntropy
{
    string NewRequestNonce();
    byte[] NewBootstrapVerifier(IEphemeralBootstrapDelivery oneSessionDelivery);
    byte[] NewCredentialVerifier(IProtectedProtocolSecretStore protectedStorage);
}

/// <summary>Held CSPRNG source, not a Windows-to-phone credential issuance route or Android storage replacement.</summary>
internal sealed class WindowsProtocolEntropy : IWindowsProtocolEntropy
{
    public string NewRequestNonce()
    {
        WindowsProtocolExecutionHold.Demand();
        Span<byte> bytes = stackalloc byte[32];
        try { RandomNumberGenerator.Fill(bytes); return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public byte[] NewBootstrapVerifier(IEphemeralBootstrapDelivery oneSessionDelivery)
    {
        WindowsProtocolExecutionHold.Demand(); ArgumentNullException.ThrowIfNull(oneSessionDelivery);
        return Generate(oneSessionDelivery.DeliverInMemory);
    }
    public byte[] NewCredentialVerifier(IProtectedProtocolSecretStore protectedStorage)
    {
        WindowsProtocolExecutionHold.Demand(); ArgumentNullException.ThrowIfNull(protectedStorage);
        return Generate(protectedStorage.Store);
    }
    private delegate void SecretConsumer(ReadOnlySpan<byte> secret);
    private static byte[] Generate(SecretConsumer consume)
    {
        Span<byte> bytes = stackalloc byte[32];
        try { RandomNumberGenerator.Fill(bytes); consume(bytes); return SHA256.HashData(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
