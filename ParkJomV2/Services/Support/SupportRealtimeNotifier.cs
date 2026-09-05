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

    public async Task BroadcastEventAsync(string eventType, object payload, int? targetUserId = null, int? ticketId = null, int? conversationId = null)
    {
        try
        {
            // 1. Broadcast via SignalR
            // Always notify Admin group
            await _hubContext.Clients.Group("Admin-Group").SendAsync(eventType, payload);

            // Notify target user group if specified
            if (targetUserId.HasValue)
            {
                await _hubContext.Clients.Group($"User-{targetUserId.Value}").SendAsync(eventType, payload);
            }

            // Notify specific ticket room if applicable
            if (ticketId.HasValue)
            {
                await _hubContext.Clients.Group($"Ticket-{ticketId.Value}").SendAsync(eventType, payload);
            }

            // Notify specific conversation room if applicable
            if (conversationId.HasValue)
            {
                await _hubContext.Clients.Group($"Conversation-{conversationId.Value}").SendAsync(eventType, payload);
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
