using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace ParkJomV2.Hubs;

[Authorize]
public class SupportHub : Hub
{
    private readonly ILogger<SupportHub> _logger;

    public SupportHub(ILogger<SupportHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var userType = Context.User?.FindFirstValue("UserType");

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"User-{userId}");
        }

        if (string.Equals(userType, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Admin-Group");
        }

        _logger.LogInformation("SignalR client connected: {ConnectionId}, User: {UserId}, Role: {UserType}",
            Context.ConnectionId, userId, userType);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("SignalR client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinTicket(int ticketId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"Ticket-{ticketId}");
    }

    public async Task LeaveTicket(int ticketId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Ticket-{ticketId}");
    }

    public async Task JoinConversation(int conversationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"Conversation-{conversationId}");
    }

    public async Task LeaveConversation(int conversationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Conversation-{conversationId}");
    }
}
