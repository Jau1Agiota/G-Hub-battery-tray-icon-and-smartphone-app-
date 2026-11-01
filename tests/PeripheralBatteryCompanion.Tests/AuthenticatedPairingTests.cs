using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PeripheralBatteryCompanion.Core.TransportSecurity;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class AuthenticatedPairingTests
{
    [Fact]
    public void DefaultPeerBoundaryCannotAuthenticateEvenAnExactPublicLeafAndCorrectBootstrap()
    {
        using var f = new SecurityProtocolFixture(false); using var pairing = f.Pairing(true);
        Assert.True(pairing.OpenByLocalOwner(SecurityProtocolFixture.EnrollmentId, SHA256.HashData(SecurityProtocolFixture.Bootstrap)));
        Assert.False(pairing.TryClaim(f.Peer, SecurityProtocolFixture.BootstrapHeader, f.Claim));
        Assert.False(pairing.ConfirmByLocalOwner(f.Claim.TranscriptSha256(f.Identity)));
        Assert.Equal(AuthorizationLoadOutcome.VerifiedEmpty, f.Journal.Read().Outcome);
    }
    [Theory]
    [InlineData("bootstrap")]
    [InlineData("endpoint")]
    [InlineData("leaf")]
    [InlineData("identity")]
    [InlineData("proof")]
    [InlineData("enrollment")]
    public void WrongBootstrapOrAnyBindingCannotClaim(string field)
    {
        using var f = new SecurityProtocolFixture(false); using var pairing = f.Pairing();
        Assert.True(pairing.OpenByLocalOwner(SecurityProtocolFixture.EnrollmentId, SHA256.HashData(SecurityProtocolFixture.Bootstrap)));
        var peer = field switch
        {
            "endpoint" => new PeerBinding("https://192.168.1.11:8443", f.Identity.IdentityId, f.Identity.LeafDer.AsSpan()),
            "leaf" => new PeerBinding(f.Identity.Endpoint, f.Identity.IdentityId, [1, 2, 4]),
            "identity" => new PeerBinding(f.Identity.Endpoint, SecurityProtocolFixture.PhoneId, f.Identity.LeafDer.AsSpan()),
            _ => f.Peer
        };
        if (field == "proof") f.Proof.Allow = false;
        var claim = field == "enrollment" ? new EnrollmentClaim(SecurityProtocolFixture.PhoneId, f.Claim.PhoneId, f.Claim.PhoneNonce, f.Claim.TokenSha256) : f.Claim;
        Assert.False(pairing.TryClaim(peer, field == "bootstrap" ? SecurityProtocolFixture.Bearer : SecurityProtocolFixture.BootstrapHeader, claim));
        Assert.Null(f.Journal.Read().Record);
    }
    [Fact]
    public void OneUseClaimExactOwnerConfirmationAndBoundedStatusDoNotExposeVerifier()
    {
        using var f = new SecurityProtocolFixture(false); using var pairing = f.Pairing();
        Assert.True(pairing.OpenByLocalOwner(SecurityProtocolFixture.EnrollmentId, SHA256.HashData(SecurityProtocolFixture.Bootstrap)));
        Assert.True(pairing.TryClaim(f.Peer, SecurityProtocolFixture.BootstrapHeader, f.Claim));
        Assert.False(pairing.TryClaim(f.Peer, SecurityProtocolFixture.BootstrapHeader, f.Claim));
        Assert.Equal(PairingOutcome.Pending, pairing.ReadStatus(f.Peer, f.Claim.EnrollmentId, SecurityProtocolFixture.BootstrapHeader).Outcome);
        Assert.True(pairing.ConfirmByLocalOwner(f.Claim.TranscriptSha256(f.Identity)));
        Assert.Equal(PairingOutcome.Rejected, pairing.ReadStatus(f.Peer, f.Claim.EnrollmentId, SecurityProtocolFixture.BootstrapHeader).Outcome);
        f.Time.Advance(TimeSpan.FromSeconds(5));
        var status = pairing.ReadStatus(f.Peer, f.Claim.EnrollmentId, SecurityProtocolFixture.BootstrapHeader);
        Assert.Equal(PairingOutcome.Approved, status.Outcome); Assert.Equal(1, status.AuthorizationGeneration);
        Assert.DoesNotContain(f.Claim.TokenSha256, JsonSerializer.Serialize(status));
        Assert.DoesNotContain(SecurityProtocolFixture.BootstrapHeader, status.ToString());
        Assert.True(f.Authorization.RevokeByLocalOwner()); f.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(PairingOutcome.Rejected, pairing.ReadStatus(f.Peer, f.Claim.EnrollmentId, SecurityProtocolFixture.BootstrapHeader).Outcome);
    }
    [Fact]
    public void DeadlineAndWrongTranscriptCannotIssueOrReactivate()
    {
        using var f = new SecurityProtocolFixture(false); using var pairing = f.Pairing();
        Assert.True(pairing.OpenByLocalOwner(SecurityProtocolFixture.EnrollmentId, SHA256.HashData(SecurityProtocolFixture.Bootstrap)));
        Assert.True(pairing.TryClaim(f.Peer, SecurityProtocolFixture.BootstrapHeader, f.Claim));
        Assert.False(pairing.ConfirmByLocalOwner(new string('0', 64)));
        Assert.False(pairing.ConfirmByLocalOwner(f.Claim.TranscriptSha256(f.Identity)));
        f.Time.Advance(TimeSpan.FromSeconds(120));
        Assert.Equal(PairingOutcome.Rejected, pairing.ReadStatus(f.Peer, f.Claim.EnrollmentId, SecurityProtocolFixture.BootstrapHeader).Outcome);
        Assert.Null(f.Journal.Read().Record);
    }
    [Fact]
    public void ClaimJsonRejectsDuplicateUnknownOversizeAndInvalidEncodings()
    {
        using var f = new SecurityProtocolFixture(false);
        string valid = JsonSerializer.Serialize(new { enrollmentId = f.Claim.EnrollmentId.ToString("D"), phoneId = f.Claim.PhoneId.ToString("D"), phoneNonce = f.Claim.PhoneNonce, tokenSha256 = f.Claim.TokenSha256 });
        Assert.True(EnrollmentClaimCodec.TryDecode(Encoding.UTF8.GetBytes(valid), out _));
        foreach (string bad in new[] { valid.Insert(1, "\"phoneId\":\"x\","), valid.Insert(1, "\"extra\":true,"), valid.Replace("phoneNonce", "other", StringComparison.Ordinal), valid + "{}", "[]" })
            Assert.False(EnrollmentClaimCodec.TryDecode(Encoding.UTF8.GetBytes(bad), out _));
        Assert.False(EnrollmentClaimCodec.TryDecode(new byte[1025], out _));
        Assert.False(EnrollmentClaimCodec.TryDecode(new byte[] { 0xFF }, out _));
    }
}
