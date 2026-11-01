using System.Security.Authentication;

namespace PeripheralBatteryCompanion.Core.TransportSecurity;

/// <summary>Observation data, NOT proof. Only the held platform inspector may supply production observations.</summary>
public sealed record Tls13ChannelObservation(bool Authenticated, bool Encrypted, bool Signed, bool ServerRole,
    SslProtocols Protocol, string ApplicationProtocol, string ServerEndpoint, ReadOnlyMemory<byte> ServerLeaf);

public static class Tls13ChannelPolicy
{
    public static bool Validate(EnrollmentIdentity expected, PeerBinding peer, Tls13ChannelObservation channel,
        DateTimeOffset now, IEnrolledCertificateSignature signature)
    {
        try
        {
            return channel.Authenticated && channel.Encrypted && channel.Signed && channel.Protocol == SslProtocols.Tls13 &&
                channel.ApplicationProtocol == "http/1.1" && channel.ServerEndpoint == expected.Endpoint &&
                peer.Endpoint == expected.Endpoint && peer.IdentityId == expected.IdentityId &&
                peer.LeafDer.AsSpan().SequenceEqual(expected.LeafDer.AsSpan()) &&
                EnrolledCertificatePolicy.Validate(expected, channel.ServerLeaf, now, signature);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { return false; }
    }
}
