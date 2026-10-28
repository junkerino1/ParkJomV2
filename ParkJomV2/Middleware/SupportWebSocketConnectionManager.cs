using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ParkJomV2.Middleware;

public class SupportWebSocketConnectionManager
{
    private readonly ConcurrentDictionary<string, (WebSocket Socket, int? UserId, string? Role)> _sockets = new();

    public string AddSocket(WebSocket socket, int? userId, string? role)
    {
        var id = Guid.NewGuid().ToString("N");
        _sockets.TryAdd(id, (socket, userId, role));
        return id;
    }

    public async Task RemoveSocketAsync(string id)
    {
        if (_sockets.TryRemove(id, out var socketInfo))
        {
            if (socketInfo.Socket.State == WebSocketState.Open || socketInfo.Socket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await socketInfo.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by server", CancellationToken.None);
                }
                catch { }
            }
        }
    }

    public async Task BroadcastAsync(string eventType, object data, int? targetUserId = null, int? ticketId = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            type = eventType,
            ticketId = ticketId,
            occurredAt = DateTime.UtcNow.ToString("o"),
            data = data
        });

        var bytes = Encoding.UTF8.GetBytes(payload);
        var buffer = new ArraySegment<byte>(bytes);

        foreach (var pair in _sockets)
        {
            var (socket, userId, role) = pair.Value;
            if (socket.State == WebSocketState.Open)
            {
                // If targeted to a user, send only to that user or admins
                if (targetUserId.HasValue && userId.HasValue && userId.Value != targetUserId.Value && !string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    await socket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch { }
            }
        }
    }
}
