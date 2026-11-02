using System.Net;
using System.Net.Sockets;
using System.Text;
using PeripheralBatteryCompanion.Core;
using PeripheralBatteryCompanion.Core.Snapshots;
using PeripheralBatteryCompanion.Core.SnapshotService;
using PeripheralBatteryCompanion.Windows;
using Xunit;

namespace PeripheralBatteryCompanion.Windows.Tests;

public sealed class LanPreviewServerTests
{
    [Theory]
    [InlineData("10.0.0.1", true)][InlineData("172.16.0.1", true)][InlineData("172.31.255.254", true)]
    [InlineData("192.168.2.111", true)][InlineData("0.0.0.0", false)][InlineData("127.0.0.1", false)]
    [InlineData("8.8.8.8", false)][InlineData("172.15.0.1", false)][InlineData("172.32.0.1", false)]
    [InlineData("169.254.1.1", false)][InlineData("::1", false)][InlineData("::ffff:192.168.1.1", false)]
    public void OnlyRfc1918Ipv4IsSelectable(string value, bool expected) =>
        Assert.Equal(expected, LanPreviewServer.IsPrivateIpv4(IPAddress.Parse(value)));

    private sealed class Cache : IReadOnlyPublishedSnapshot
    {
        public PublishedSnapshot? Value;
        public int Reads;
        public PublishedSnapshot? ReadLatest() { Reads++; return Value; }
    }
    [Fact]
    public async Task SelectedInterfaceServesImmutableCacheAndRejectsOtherOperationsThenShutsDown()
    {
        var address = LanPreviewServer.LocalAddresses().FirstOrDefault(a => a.GetAddressBytes()[0] == 192)
            ?? LanPreviewServer.LocalAddresses().FirstOrDefault();
        // A machine without an assigned private interface must explicitly report this environmental prerequisite.
        Assert.NotNull(address);
        using var store = new PublishedSnapshotStore(Guid.Parse("11111111-1111-4111-8111-111111111111"));
        var cache = new Cache();
        var server = new LanPreviewServer(cache, address, 0);
        var endpoint = server.Endpoint;
        Assert.Equal(address, endpoint.Address);
        Assert.Throws<ArgumentException>(() => new LanPreviewServer(cache, IPAddress.Any, 8765));
        Assert.Throws<ArgumentException>(() => new LanPreviewServer(cache, IPAddress.Loopback, 8765));
        try
        {
            Assert.StartsWith("HTTP/1.1 503", await Request(endpoint, "GET", LanPreviewServer.Path));
            var publication = store.Publish(BatterySnapshotProjection.Build(null, null, new(store.InstanceId, 0, null)));
            cache.Value = publication;
            string response = await Request(endpoint, "GET", LanPreviewServer.Path);
            Assert.StartsWith("HTTP/1.1 200", response);
            Assert.Contains("Cache-Control: no-store", response);
            Assert.Equal(Encoding.UTF8.GetString(publication.Json.AsSpan()), response.Split("\r\n\r\n")[1]);
            var repeat = await Request(endpoint, "GET", LanPreviewServer.Path);
            Assert.Equal(response, repeat);
            Assert.Same(publication, store.ReadLatest());
            Assert.Equal(3, cache.Reads); // HTTP did not publish or touch a battery provider.
            Assert.StartsWith("HTTP/1.1 405", await Request(endpoint, "POST", LanPreviewServer.Path));
            Assert.StartsWith("HTTP/1.1 404", await Request(endpoint, "GET", "/refresh"));
            Assert.StartsWith("HTTP/1.1 400", await Request(endpoint, "GET", LanPreviewServer.Path, "Host: attacker.invalid\r\n"));
            Assert.StartsWith("HTTP/1.1 400", await Request(endpoint, "GET", LanPreviewServer.Path, $"Host: {endpoint}\r\nContent-Length: 1\r\n"));
            Assert.StartsWith("HTTP/1.1 400", await Request(endpoint, "GET", LanPreviewServer.Path, $"Host: {endpoint}\r\nHost: {endpoint}\r\n"));
            Assert.StartsWith("HTTP/1.1 431", await Request(endpoint, "GET", LanPreviewServer.Path, "X: " + new string('a', 8192) + "\r\n"));
            using var loopback = new TcpClient();
            await Assert.ThrowsAsync<SocketException>(() => loopback.ConnectAsync(IPAddress.Loopback, endpoint.Port));
        }
        finally { await server.DisposeAsync(); }
        using var stopped = new TcpClient();
        await Assert.ThrowsAsync<SocketException>(() => stopped.ConnectAsync(endpoint.Address, endpoint.Port));
    }
    private static async Task<string> Request(IPEndPoint endpoint, string method, string path, string? headers = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint.Address, endpoint.Port, timeout.Token);
        using var stream = client.GetStream();
        var bytes = Encoding.ASCII.GetBytes($"{method} {path} HTTP/1.1\r\n{headers ?? $"Host: {endpoint}\r\n"}\r\n");
        await stream.WriteAsync(bytes, timeout.Token);
        using var output = new MemoryStream();
        // Oversized input may be reset after a complete rejection response: only that test closes without body.
        var buffer = new byte[4096];
        try { int count; while ((count = await stream.ReadAsync(buffer, timeout.Token)) != 0) output.Write(buffer, 0, count); }
        catch (IOException) when (output.Length > 0) { }
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
