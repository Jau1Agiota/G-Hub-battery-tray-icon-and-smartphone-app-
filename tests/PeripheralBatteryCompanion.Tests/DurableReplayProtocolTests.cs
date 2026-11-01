using System.Buffers.Binary;
using System.Security.Cryptography;
using PeripheralBatteryCompanion.Core.TransportSecurity;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class DurableReplayProtocolTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlreadyAcceptedDisplayIsHiddenAfterRevocationOrCredentialExpiry(bool expire)
    {
        using var f = new SecurityProtocolFixture(); var receiver = f.Receiver(); Assert.True(receiver.StartForegroundByLocalOwner());
        Assert.True(receiver.TryBegin(f.Peer, SecurityProtocolFixture.Nonce(1), out long attempt));
        Assert.Equal(ReplayDisposition.NewPublisher, receiver.TryAccept(attempt, f.Peer, f.Response(SecurityProtocolFixture.Publisher, 1, SecurityProtocolFixture.Nonce(1))));
        Assert.NotEmpty(receiver.VisibleBody);
        if (expire) f.Time.Advance(TimeSpan.FromDays(90)); else Assert.True(f.Authorization.RevokeByLocalOwner());
        Assert.Empty(receiver.VisibleBody); Assert.True(receiver.RecoveryRequired);
    }
    [Fact]
    public void FreshDuplicateOnlyAuthenticatesTheSamePublicationAndOldCallbacksCannotBeReplayed()
    {
        using var f = new SecurityProtocolFixture(); var receiver = f.Receiver(); Assert.True(receiver.StartForegroundByLocalOwner());
        Assert.True(receiver.TryBegin(f.Peer, SecurityProtocolFixture.Nonce(1), out long first));
        var body = f.Response(SecurityProtocolFixture.Publisher, 7, SecurityProtocolFixture.Nonce(1));
        Assert.Equal(ReplayDisposition.NewPublisher, receiver.TryAccept(first, f.Peer, body));
        Assert.Equal(ReplayDisposition.Rejected, receiver.TryAccept(first, f.Peer, body));
        Assert.True(receiver.TryBegin(f.Peer, SecurityProtocolFixture.Nonce(2), out long second));
        Assert.Equal(ReplayDisposition.Rejected, receiver.TryAccept(first, f.Peer, body));
        var duplicate = f.Response(SecurityProtocolFixture.Publisher, 7, SecurityProtocolFixture.Nonce(2));
        Assert.Equal(ReplayDisposition.Duplicate, receiver.TryAccept(second, f.Peer, duplicate));
        Assert.Equal(body.Body.ToArray(), receiver.VisibleBody.ToArray());
        Assert.Equal(7, f.Journal.ReadReplay(1)!.Instances[0].Revision);
        Assert.Equal(2, f.Journal.ReadReplay(1)!.Version);
    }
    [Fact]
    public void RealJournalRestartsRetainHighWaterMarksAndRejectRegression()
    {
        using var f = new SecurityProtocolFixture(); var receiver = f.Receiver(); Assert.True(receiver.StartForegroundByLocalOwner());
        Assert.True(receiver.TryBegin(f.Peer, SecurityProtocolFixture.Nonce(1), out long attempt));
        Assert.Equal(ReplayDisposition.NewPublisher, receiver.TryAccept(attempt, f.Peer, f.Response(SecurityProtocolFixture.Publisher, 9, SecurityProtocolFixture.Nonce(1))));
        f.Restart(); var restored = f.Receiver(); Assert.True(restored.StartForegroundByLocalOwner());
        Assert.True(restored.TryBegin(f.Peer, SecurityProtocolFixture.Nonce(2), out long next));
        Assert.Equal(ReplayDisposition.Rejected, restored.TryAccept(next, f.Peer, f.Response(SecurityProtocolFixture.Publisher, 8, SecurityProtocolFixture.Nonce(2))));
        Assert.True(restored.RecoveryRequired); Assert.Empty(restored.VisibleBody);
    }
    [Fact]
    public void SixtyFifthInstanceLatchesRecoveryWithoutEvictionOrDisplay()
    {
        using var f = new SecurityProtocolFixture(); var receiver = f.Receiver(); Assert.True(receiver.StartForegroundByLocalOwner());
        for (int i = 1; i <= 64; i++)
        {
            Assert.True(receiver.TryBegin(f.Peer, SecurityProtocolFixture.Nonce(i), out long attempt));
            Guid instance = Guid.Parse($"00000000-0000-4000-8000-{i:D12}");
            Assert.Equal(ReplayDisposition.NewPublisher, receiver.TryAccept(attempt, f.Peer, f.Response(instance, 1, SecurityProtocolFixture.Nonce(i))));
        }
        Assert.Equal(64, f.Journal.ReadReplay(1)!.Instances.Length);
        byte[] committed = File.ReadAllBytes(Path.Join(f.Root, "state.bin"));
        Assert.True(receiver.TryBegin(f.Peer, SecurityProtocolFixture.Nonce(65), out long over));
        Assert.Equal(ReplayDisposition.Rejected, receiver.TryAccept(over, f.Peer, f.Response(Guid.Parse("00000000-0000-4000-8000-000000000065"), 1, SecurityProtocolFixture.Nonce(65))));
        Assert.Equal(committed, File.ReadAllBytes(Path.Join(f.Root, "state.bin")));
        Assert.True(receiver.RecoveryRequired); Assert.Empty(receiver.VisibleBody);
        Assert.False(receiver.StartForegroundByLocalOwner());
    }
    [Fact]
    public void RetiredInstancesAndReusedNonceAreDenied()
    {
        using var f = new SecurityProtocolFixture(); var receiver = f.Receiver(); Assert.True(receiver.StartForegroundByLocalOwner());
        for (int i = 1; i <= 2; i++)
        {
            Assert.True(receiver.TryBegin(f.Peer, SecurityProtocolFixture.Nonce(i), out long attempt));
            Assert.Equal(ReplayDisposition.NewPublisher, receiver.TryAccept(attempt, f.Peer,
                f.Response(Guid.Parse($"00000000-0000-4000-8000-{i:D12}"), 1, SecurityProtocolFixture.Nonce(i))));
        }
        Assert.True(receiver.TryBegin(f.Peer, SecurityProtocolFixture.Nonce(3), out long old));
        Assert.Equal(ReplayDisposition.Rejected, receiver.TryAccept(old, f.Peer,
            f.Response(Guid.Parse("00000000-0000-4000-8000-000000000001"), 9, SecurityProtocolFixture.Nonce(3))));
        using var other = new SecurityProtocolFixture(); var fresh = other.Receiver(); Assert.True(fresh.StartForegroundByLocalOwner());
        Assert.True(fresh.TryBegin(other.Peer, SecurityProtocolFixture.Nonce(1), out _)); fresh.Pause(); Assert.True(fresh.StartForegroundByLocalOwner());
        Assert.False(fresh.TryBegin(other.Peer, SecurityProtocolFixture.Nonce(1), out _)); Assert.True(fresh.RecoveryRequired);
    }
    [Theory]
    [InlineData("nonce")]
    [InlineData("generation")]
    [InlineData("digest")]
    [InlineData("publisher")]
    [InlineData("framing")]
    [InlineData("duplicate")]
    [InlineData("compression")]
    public void WrongResponseContextCannotCommitOrPublish(string field)
    {
        using var f = new SecurityProtocolFixture(); var receiver = f.Receiver(); Assert.True(receiver.StartForegroundByLocalOwner());
        string nonce = SecurityProtocolFixture.Nonce(1); Assert.True(receiver.TryBegin(f.Peer, nonce, out long attempt));
        var response = f.Response(SecurityProtocolFixture.Publisher, 1, nonce); var headers = response.Headers.ToDictionary();
        if (field == "nonce") headers["PBC-Request-Nonce"] = SecurityProtocolFixture.Nonce(2);
        if (field == "generation") headers["PBC-Authorization-Generation"] = "2";
        if (field == "digest") headers["PBC-Snapshot-SHA256"] = new string('0', 64);
        if (field == "publisher") headers["PBC-Publisher-Instance"] = "00000000-0000-4000-8000-000000000001";
        if (field == "framing") headers["Content-Length"] = "1";
        if (field == "compression") headers["Content-Encoding"] = "gzip";
        var wire = headers.ToList(); if (field == "duplicate") wire.Add(new("pbc-protocol", "1"));
        Assert.Equal(ReplayDisposition.Rejected, receiver.TryAcceptWire(attempt, f.Peer, response.Body.AsMemory(), wire));
        Assert.True(receiver.RecoveryRequired); Assert.Empty(receiver.VisibleBody);
    }
    [Fact]
    public void ReplayCommitFailureAndRevocationNeverPublishPreparedBytes()
    {
        using var f = new SecurityProtocolFixture(); var receiver = f.Receiver(); Assert.True(receiver.StartForegroundByLocalOwner());
        Assert.True(receiver.TryBegin(f.Peer, SecurityProtocolFixture.Nonce(1), out long attempt));
        f.FailNextStateWrite = true;
        Assert.Equal(ReplayDisposition.Rejected, receiver.TryAccept(attempt, f.Peer,
            f.Response(SecurityProtocolFixture.Publisher, 1, SecurityProtocolFixture.Nonce(1))));
        Assert.True(receiver.RecoveryRequired); Assert.Empty(receiver.VisibleBody);
        Assert.False(f.Authorization.TryAuthenticateBearer(SecurityProtocolFixture.Bearer, out _));
        using var other = new SecurityProtocolFixture(); var next = other.Receiver(); Assert.True(next.StartForegroundByLocalOwner());
        Assert.True(next.TryBegin(other.Peer, SecurityProtocolFixture.Nonce(1), out long pending));
        Assert.True(other.Authorization.RevokeByLocalOwner());
        Assert.Equal(ReplayDisposition.Rejected, next.TryAccept(pending, other.Peer,
            other.Response(SecurityProtocolFixture.Publisher, 1, SecurityProtocolFixture.Nonce(1))));
    }
    [Fact]
    public void MissingReplayHistoryForExistingTrustIsNotReinitialized()
    {
        using var f = new SecurityProtocolFixture(); f.Authorization.Dispose();
        string statePath = Path.Join(f.Root, "state.bin"), checkpointPath = Path.Join(f.Root, "checkpoint.bin");
        byte[] state = File.ReadAllBytes(statePath); // TEST wrapper prefix + envelope, not a real DPAPI blob.
        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(state.AsSpan(49, 4));
        Array.Resize(ref state, 53 + payloadLength + 4);
        BinaryPrimitives.WriteInt32LittleEndian(state.AsSpan(53 + payloadLength, 4), 0);
        byte[] checkpoint = File.ReadAllBytes(checkpointPath); SHA256.HashData(state).CopyTo(checkpoint, 16);
        File.WriteAllBytes(statePath, state); File.WriteAllBytes(checkpointPath, checkpoint);
        f.Restart(); Assert.True(f.Authorization.RecoveryRequired);
        Assert.Equal(AuthorizationLoadOutcome.Uncertain, f.Journal.Read().Outcome);
    }
}
