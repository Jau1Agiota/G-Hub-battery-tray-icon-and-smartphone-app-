using System.Net.WebSockets;

namespace PeripheralBatteryCompanion.Core;

// Injectable byte-fragment boundary: tests never open a port or require G HUB.
public readonly record struct GatewayFragment(int Count, WebSocketMessageType Type, bool EndOfMessage);
public interface IGHubGatewayConnection : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken token);
    Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken token);
    Task<GatewayFragment> ReceiveAsync(Memory<byte> buffer, CancellationToken token);
}
public interface IGHubGatewayConnectionFactory
{
    IGHubGatewayConnection Create();
}

public sealed class GHubGatewayConnectionFactory : IGHubGatewayConnectionFactory
{
    public IGHubGatewayConnection Create() => new Connection();
    private sealed class Connection : IGHubGatewayConnection
    {
        private readonly ClientWebSocket _socket = new();
        public Connection()
        {
            _socket.Options.AddSubProtocol("json");
            _socket.Options.Proxy = null;
            _socket.Options.UseDefaultCredentials = false;
        }
        public async Task ConnectAsync(CancellationToken token)
        {
            // Fixed owner-verified loopback endpoint; no remote address, redirect, discovery or command console.
            await _socket.ConnectAsync(new Uri("ws://127.0.0.1:9010/"), token).ConfigureAwait(false);
            if (_socket.SubProtocol != "json") throw new InvalidDataException("Gateway subprotocol unavailable");
        }
        public async Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken token) =>
            await _socket.SendAsync(message, WebSocketMessageType.Text, true, token).ConfigureAwait(false);
        public async Task<GatewayFragment> ReceiveAsync(Memory<byte> buffer, CancellationToken token)
        {
            var result = await _socket.ReceiveAsync(buffer, token).ConfigureAwait(false);
            return new(result.Count, result.MessageType, result.EndOfMessage);
        }
        public ValueTask DisposeAsync()
        {
            // No close-handshake wait or background reader survives a poll, failure or cancellation.
            _socket.Abort();
            _socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
