using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using PeripheralBatteryCompanion.Core.Snapshots;
using PeripheralBatteryCompanion.Core.SnapshotService;

namespace PeripheralBatteryCompanion.Windows;

// Deliberately separate from authenticated transport. Reads already published bytes only.
internal sealed class LanPreviewServer : IAsyncDisposable
{
    internal const string Label = "LAN preview · unencrypted and unauthenticated.";
    internal const string Path = "/v1/battery-snapshot";
    private readonly IReadOnlyPublishedSnapshot _snapshots;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private long _window = Environment.TickCount64;
    private int _requests;
    internal IPEndPoint Endpoint { get; }
    internal static bool IsPrivateIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168;
    }
    internal static IPAddress[] LocalAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
        .Where(IsPrivateIpv4).Distinct().ToArray();
    internal LanPreviewServer(IReadOnlyPublishedSnapshot snapshots, IPAddress address, int port)
    {
        if (!LocalAddresses().Contains(address) || port is < 0 or > 65535) throw new ArgumentException("Select an assigned private IPv4 interface and valid port.");
        _snapshots = snapshots;
        _listener = new(address, port); // No HTTP.sys URL ACL, elevation, wildcard or dual-stack bind.
        _listener.Server.ExclusiveAddressUse = true;
        _listener.Start(8);
        Endpoint = (IPEndPoint)_listener.LocalEndpoint;
        _loop = ServeAsync();
    }
    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                await HandleAsync(client.GetStream(), deadline.Token);
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        }
    }
    private async Task HandleAsync(NetworkStream stream, CancellationToken stop)
    {
        var buffer = new byte[8192]; int length = 0;
        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length, 1), stop);
            if (read == 0) return;
            length++;
            if (length >= 4 && buffer.AsSpan(length - 4, 4).SequenceEqual("\r\n\r\n"u8)) break;
        }
        if (length == buffer.Length) { await Reply(stream, 431, "{\"error\":\"headers_too_large\"}"u8.ToArray(), stop); return; }
        if (buffer.AsSpan(0, length).ContainsAnyExceptInRange((byte)0, (byte)127)) { await Reply(stream, 400, [], stop); return; }
        var lines = Encoding.ASCII.GetString(buffer, 0, length).Split("\r\n");
        var request = lines[0].Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines.Skip(1).Where(l => l.Length > 0))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0 || !headers.TryAdd(line[..colon], line[(colon + 1)..].Trim())) { await Reply(stream, 400, [], stop); return; }
        }
        if (request.Length != 3 || request[2] is not ("HTTP/1.1" or "HTTP/1.0") ||
            headers.ContainsKey("Transfer-Encoding") || headers.TryGetValue("Content-Length", out var body) && body != "0" ||
            !headers.TryGetValue("Host", out var host) || host != Endpoint.ToString())
        { await Reply(stream, 400, [], stop); return; }
        if (request[0] != "GET") { await Reply(stream, 405, [], stop); return; }
        if (request[1] != Path) { await Reply(stream, 404, [], stop); return; }
        if (Environment.TickCount64 - _window >= 60000) { _window = Environment.TickCount64; _requests = 0; }
        if (++_requests > 120) { await Reply(stream, 429, [], stop); return; }
        PublishedSnapshot? snapshot;
        try { snapshot = _snapshots.ReadLatest(); }
        catch { await Reply(stream, 503, [], stop); return; }
        if (snapshot is null || snapshot.Snapshot.Synthetic || snapshot.Json.Length > BatterySnapshotJson.MaxPayloadBytes)
        { await Reply(stream, 503, [], stop); return; }
        await Reply(stream, 200, snapshot.Json.ToArray(), stop);
    }
    private static async Task Reply(NetworkStream stream, int status, byte[] body, CancellationToken stop)
    {
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Response\r\nContent-Type: application/json\r\nCache-Control: no-store\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\nContent-Length: {body.Length}\r\n\r\n");
        await stream.WriteAsync(header, stop);
        await stream.WriteAsync(body, stop);
    }
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync(); _listener.Stop(); await _loop; _stop.Dispose();
    }
}
