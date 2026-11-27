using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Community360.Api.Services;

public sealed class WebSocketHubService
{
    private readonly ConcurrentDictionary<string, WebSocket> _sockets = new();

    public async Task HandleConnectionAsync(WebSocket socket, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        _sockets[id] = socket;

        var buffer = new byte[1024 * 4];
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Cerrado", ct);
                    break;
                }
            }
        }
        catch
        {
            // Cliente desconectado
        }
        finally
        {
            _sockets.TryRemove(id, out _);
        }
    }

    public async Task BroadcastEventoAsync(object evento)
    {
        var json = JsonSerializer.Serialize(evento);
        var bytes = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(bytes);

        foreach (var (id, ws) in _sockets)
        {
            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch
                {
                    _sockets.TryRemove(id, out _);
                }
            }
            else
            {
                _sockets.TryRemove(id, out _);
            }
        }
    }
}
