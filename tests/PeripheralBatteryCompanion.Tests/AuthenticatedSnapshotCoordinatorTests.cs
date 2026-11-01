using PeripheralBatteryCompanion.Core.SnapshotService;
using PeripheralBatteryCompanion.Core.TransportSecurity;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class AuthenticatedSnapshotCoordinatorTests
{
    [Fact]
    public async Task AuthenticatedCoreFeedsTheDurableReceiverWithoutReserializingPublication()
    {
        using var f = new SecurityProtocolFixture(); using var server = f.Server();
        var receiver = f.Receiver(); Assert.True(receiver.StartForegroundByLocalOwner());
        for (int i = 1; i <= 2; i++)
        {
            string nonce = SecurityProtocolFixture.Nonce(i); Assert.True(receiver.TryBegin(f.Peer, nonce, out long attempt));
            Assert.Equal(200, server.TryPrepare(f.Peer, "GET", SnapshotRequestCore.SnapshotPath, 0, SecurityProtocolFixture.RequestHeaders(nonce), out var prepared));
            using var response = prepared!; ReplayDisposition result = ReplayDisposition.Rejected;
            Assert.True(await response.SendAsync((body, _) => { result = receiver.TryAccept(attempt, f.Peer, body); return Task.CompletedTask; }));
            Assert.Equal(i == 1 ? ReplayDisposition.NewPublisher : ReplayDisposition.Duplicate, result);
            Assert.Equal(f.Publications.ReadLatest()!.Json.ToArray(), receiver.VisibleBody.ToArray());
        }
        Assert.Equal(2, f.Journal.ReadReplay(1)!.Version);
    }
    [Fact]
    public async Task ExactCredentialAndFreshNonceReturnImmutablePublicationOnlyOnce()
    {
        using var f = new SecurityProtocolFixture(); using var server = f.Server();
        var request = SecurityProtocolFixture.RequestHeaders(SecurityProtocolFixture.Nonce(1));
        Assert.Equal(200, server.TryPrepare(f.Peer, "GET", SnapshotRequestCore.SnapshotPath, 0, request, out var prepared));
        using var response = prepared!;
        Assert.Equal(f.Publications.ReadLatest()!.Json.ToArray(), response.Response.Body.ToArray());
        int writes = 0;
        Assert.True(await response.SendAsync((body, stop) => { Assert.False(stop.IsCancellationRequested); writes++; return Task.CompletedTask; }));
        Assert.False(await response.SendAsync((body, stop) => { writes++; return Task.CompletedTask; }));
        Assert.Equal(1, writes); Assert.Equal(0, server.ActiveResponses);
        Assert.Equal(400, server.TryPrepare(f.Peer, "GET", SnapshotRequestCore.SnapshotPath, 0, request, out _));
    }
    [Theory]
    [InlineData("missing", 401)]
    [InlineData("wrong", 401)]
    [InlineData("alias", 401)]
    [InlineData("duplicate", 400)]
    [InlineData("protocol", 400)]
    [InlineData("nonce", 400)]
    [InlineData("transfer", 400)]
    [InlineData("oversize", 400)]
    public void MalformedOrMismatchedAuthenticationNeverPreparesResponse(string field, int status)
    {
        using var f = new SecurityProtocolFixture(); using var server = f.Server();
        var headers = SecurityProtocolFixture.RequestHeaders(SecurityProtocolFixture.Nonce(1)).ToList();
        if (field == "missing") headers.RemoveAt(0);
        if (field == "wrong") headers[0] = new("Authorization", SecurityProtocolFixture.BootstrapHeader);
        if (field == "alias") headers[0] = new("Authorization", "Bearer " + new string('A', 42) + "B");
        if (field == "duplicate") headers.Add(new("authorization", SecurityProtocolFixture.Bearer));
        if (field == "protocol") headers[1] = new("PBC-Protocol", "2");
        if (field == "nonce") headers[2] = new("PBC-Request-Nonce", "short");
        if (field == "transfer") headers.Add(new("Transfer-Encoding", "chunked"));
        if (field == "oversize") headers.Add(new("X-Test", new string('x', 8193)));
        Assert.Equal(status, server.TryPrepare(f.Peer, "GET", SnapshotRequestCore.SnapshotPath, 0, headers, out var response));
        Assert.Null(response); Assert.Equal(0, server.ActiveResponses);
    }
    [Fact]
    public async Task PeerBoundaryMustRemainValidUntilTheWrite()
    {
        using var f = new SecurityProtocolFixture(); using (var denied = f.Server(true))
            Assert.Equal(403, denied.TryPrepare(f.Peer, "GET", SnapshotRequestCore.SnapshotPath, 0,
                SecurityProtocolFixture.RequestHeaders(SecurityProtocolFixture.Nonce(1)), out _));
        // The default-denied server was disposed; use a fresh owner-reviewed fixture.
        using var other = new SecurityProtocolFixture(); using var server = other.Server();
        Assert.Equal(200, server.TryPrepare(other.Peer, "GET", SnapshotRequestCore.SnapshotPath, 0,
            SecurityProtocolFixture.RequestHeaders(SecurityProtocolFixture.Nonce(2)), out var prepared));
        other.Proof.Allow = false; using var response = prepared!; bool called = false;
        Assert.False(await response.SendAsync((_, _) => { called = true; return Task.CompletedTask; }));
        Assert.False(called);
    }
    [Fact]
    public async Task FourSlotsAreBoundedAndQueuedResponsesDenyAfterRevocation()
    {
        using var f = new SecurityProtocolFixture(); using var server = f.Server();
        var responses = new List<AuthenticatedSnapshotCoordinator.PreparedResponse>();
        try
        {
            for (int i = 0; i < 4; i++)
            { Assert.Equal(200, server.TryPrepare(f.Peer, "GET", SnapshotRequestCore.SnapshotPath, 0, SecurityProtocolFixture.RequestHeaders(SecurityProtocolFixture.Nonce(i)), out var response)); responses.Add(response!); }
            Assert.Equal(429, server.TryPrepare(f.Peer, "GET", SnapshotRequestCore.SnapshotPath, 0, SecurityProtocolFixture.RequestHeaders(SecurityProtocolFixture.Nonce(4)), out _));
            Assert.True(f.Authorization.RevokeByLocalOwner());
            foreach (var response in responses)
            {
                Assert.True(response.Revocation.IsCancellationRequested); bool called = false;
                Assert.False(await response.SendAsync((_, _) => { called = true; return Task.CompletedTask; })); Assert.False(called);
            }
            Assert.Equal(0, server.ActiveResponses);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
    [Fact]
    public async Task IgnoredCancellationRetainsTheUnknownWriterSlotUntilActualCompletion()
    {
        using var f = new SecurityProtocolFixture(); using var server = f.Server();
        Assert.Equal(200, server.TryPrepare(f.Peer, "GET", SnapshotRequestCore.SnapshotPath, 0,
            SecurityProtocolFixture.RequestHeaders(SecurityProtocolFixture.Nonce(1)), out var prepared));
        using var response = prepared!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var send = response.SendAsync((_, token) => { entered.SetResult(); return exit.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(f.Authorization.RevokeByLocalOwner());
        Assert.False(await send.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, server.ActiveResponses); // No fabricated cancellation/cleanup success.
        Assert.Equal(503, server.TryPrepare(f.Peer, "GET", SnapshotRequestCore.SnapshotPath, 0,
            SecurityProtocolFixture.RequestHeaders(SecurityProtocolFixture.Nonce(2)), out _));
        exit.SetResult();
        Assert.True(SpinWait.SpinUntil(() => server.ActiveResponses == 0, TimeSpan.FromSeconds(5)));
    }
    [Fact]
    public async Task ExposureAndQueueDeadlinesInvalidateWithoutWriting()
    {
        using var f = new SecurityProtocolFixture(); using var server = f.Server();
        Assert.Equal(200, server.TryPrepare(f.Peer, "GET", SnapshotRequestCore.SnapshotPath, 0,
            SecurityProtocolFixture.RequestHeaders(SecurityProtocolFixture.Nonce(1)), out var prepared));
        f.Time.Advance(TimeSpan.FromSeconds(5)); using var response = prepared!; bool called = false;
        Assert.False(await response.SendAsync((_, _) => { called = true; return Task.CompletedTask; })); Assert.False(called);
        f.Time.Advance(TimeSpan.FromHours(8));
        Assert.False(f.Authorization.ApproveExposureByLocalOwner());
        Assert.True(response.Revocation.IsCancellationRequested);
    }
}
