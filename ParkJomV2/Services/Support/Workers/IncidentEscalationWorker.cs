using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Services.Support.Workers;

public class IncidentEscalationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IncidentEscalationWorker> _logger;

    public IncidentEscalationWorker(IServiceProvider serviceProvider, ILogger<IncidentEscalationWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IncidentEscalationWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var auditService = scope.ServiceProvider.GetRequiredService<SupportAuditService>();
                var realtimeNotifier = scope.ServiceProvider.GetRequiredService<ISupportRealtimeNotifier>();

                var now = DateTime.UtcNow;

                var overdueIncidents = await context.OperationalIncidents
                    .Where(i => (i.Status == IncidentStatus.Open || i.Status == IncidentStatus.Escalated)
                        && i.NextEscalationAt.HasValue
                        && i.NextEscalationAt.Value <= now)
                    .ToListAsync(stoppingToken);

                if (overdueIncidents.Count > 0)
                {
                    var policy = await context.SupportOnCallPolicies.FirstOrDefaultAsync(stoppingToken) ?? new SupportOnCallPolicy();
                    var schedule = await context.SupportOnCallSchedules.FirstOrDefaultAsync(s => s.IsActive, stoppingToken);

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

                    await context.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in IncidentEscalationWorker.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
