using Microsoft.AspNetCore.SignalR;
using ParkJomV2.Hubs;
using ParkJomV2.Middleware;

namespace ParkJomV2.Services.Support;

public class SupportRealtimeNotifier : ISupportRealtimeNotifier
{
    private readonly IHubContext<SupportHub> _hubContext;
    private readonly SupportWebSocketConnectionManager _webSocketManager;
    private readonly ILogger<SupportRealtimeNotifier> _logger;

    public SupportRealtimeNotifier(
        IHubContext<SupportHub> hubContext,
        SupportWebSocketConnectionManager webSocketManager,
        ILogger<SupportRealtimeNotifier> logger)
    {
        _hubContext = hubContext;
        _webSocketManager = webSocketManager;
        _logger = logger;
    }

    public async Task BroadcastEventAsync(string eventType, object payload, int? targetUserId = null, int? ticketId = null)
    {
        try
        {
            // 1. Broadcast via SignalR
            await _hubContext.Clients.Group("Admin-Group").SendAsync(eventType, payload);

            if (targetUserId.HasValue)
            {
                await _hubContext.Clients.Group($"User-{targetUserId.Value}").SendAsync(eventType, payload);
            }

            if (ticketId.HasValue)
            {
                await _hubContext.Clients.Group($"Ticket-{ticketId.Value}").SendAsync(eventType, payload);
            }

            // 2. Broadcast via Native WebSocket manager
            await _webSocketManager.BroadcastAsync(eventType, payload, targetUserId, ticketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast realtime support event {EventType}", eventType);
        }
    }
}
