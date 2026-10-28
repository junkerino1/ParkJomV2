using ParkJomV2.Data;
using ParkJomV2.Models;

namespace ParkJomV2.Services.Support;

public class SupportAuditService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SupportAuditService> _logger;

    public SupportAuditService(ApplicationDbContext context, ILogger<SupportAuditService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task LogAsync(
        string objectType,
        int objectId,
        string objectReference,
        string action,
        int? actorUserId,
        string actorRole,
        string? previousState = null,
        string? newState = null,
        string? detail = null)
    {
        try
        {
            var auditEvent = new SupportAuditEvent
            {
                ObjectType = objectType,
                ObjectId = objectId,
                ObjectReference = objectReference,
                Action = action,
                ActorUserId = actorUserId,
                ActorRole = actorRole,
                PreviousState = previousState,
                NewState = newState,
                Detail = detail,
                Timestamp = DateTime.UtcNow
            };

            _context.SupportAuditEvents.Add(auditEvent);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write support audit event for {ObjectType} {ObjectId}", objectType, objectId);
        }
    }
}
