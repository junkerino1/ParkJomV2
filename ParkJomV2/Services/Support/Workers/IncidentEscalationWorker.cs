using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Services.Support.Workers;

public class IncidentEscalationJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IncidentEscalationJob> _logger;

    public IncidentEscalationJob(IServiceScopeFactory scopeFactory, ILogger<IncidentEscalationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var auditService = scope.ServiceProvider.GetRequiredService<SupportAuditService>();
        var realtimeNotifier = scope.ServiceProvider.GetRequiredService<ISupportRealtimeNotifier>();
        var now = DateTime.UtcNow;
        var overdueIncidents = await context.OperationalIncidents
            .Where(i => (i.Status == IncidentStatus.Open || i.Status == IncidentStatus.Escalated)
                && i.NextEscalationAt.HasValue
                && i.NextEscalationAt.Value <= now)
            .ToListAsync(cancellationToken);

        if (overdueIncidents.Count > 0)
        {
            var policy = await context.SupportOnCallPolicies.FirstOrDefaultAsync(cancellationToken) ?? new SupportOnCallPolicy();
            var schedule = await context.SupportOnCallSchedules.FirstOrDefaultAsync(s => s.IsActive, cancellationToken);

            foreach (var incident in overdueIncidents)
            {
                        var oldLevel = incident.EscalationLevel;
                        var nextLevel = oldLevel + 1;
                        incident.EscalationLevel = nextLevel;
                        incident.Status = IncidentStatus.Escalated;

                        int delayMinutes = incident.Priority == IncidentPriority.P0
                            ? (nextLevel == 1 ? policy.P0BackupDelayMinutes : nextLevel == 2 ? policy.P0SupervisorDelayMinutes : policy.P0ManagerDelayMinutes)
                            : (nextLevel == 1 ? policy.P1BackupDelayMinutes : nextLevel == 2 ? policy.P1SupervisorDelayMinutes : policy.P1ManagerDelayMinutes);

                        incident.NextEscalationAt = nextLevel < 3 ? now.AddMinutes(delayMinutes) : null;
                        incident.UpdatedAt = now;

                        int? targetUserId = nextLevel switch
                        {
                            1 => schedule?.BackupResponderId,
                            2 => schedule?.SupervisorId,
                            _ => schedule?.OperationsManagerId
                        };

                        var targetRoleName = nextLevel switch
                        {
                            1 => "Backup Responder",
                            2 => "Supervisor",
                            _ => "Operations Manager"
                        };

                        // Create notification record
                        var attempt = new SupportNotificationAttempt
                        {
                            IncidentId = incident.IncidentId,
                            Channel = NotificationChannel.Push,
                            Recipient = targetRoleName,
                            RecipientUserId = targetUserId,
                            Subject = $"[ESCALATION L{nextLevel}] {incident.Priority} Incident {incident.IncidentReference}",
                            Message = $"Unacknowledged {incident.Priority} incident '{incident.Title}' escalated to {targetRoleName}.",
                            Status = "Sent",
                            AttemptCount = 1,
                            CreatedAt = now,
                            SentAt = now
                        };
                        context.SupportNotificationAttempts.Add(attempt);

                await auditService.LogAsync("Incident", incident.IncidentId, incident.IncidentReference, "Escalated", null, "System", $"L{oldLevel}", $"L{nextLevel}", $"Escalated to {targetRoleName} due to response timeout");
                await realtimeNotifier.BroadcastEventAsync("incident.updated", new { incidentId = incident.IncidentId, status = "Escalated", escalationLevel = nextLevel });

                _logger.LogWarning("Escalated Incident {Reference} (Priority {Priority}) to Level {Level} ({Role})",
                    incident.IncidentReference, incident.Priority, nextLevel, targetRoleName);
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        return overdueIncidents.Count;
    }
}
