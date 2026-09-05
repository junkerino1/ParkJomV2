namespace ParkJomV2.Services.Support;

public interface ISupportRealtimeNotifier
{
    Task BroadcastEventAsync(string eventType, object payload, int? targetUserId = null, int? ticketId = null, int? conversationId = null);
}
