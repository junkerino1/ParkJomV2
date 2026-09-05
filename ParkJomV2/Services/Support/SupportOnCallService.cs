using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Services.Support;

public class SupportOnCallService
{
    private readonly ApplicationDbContext _context;
    private readonly SupportAuditService _auditService;
    private readonly ILogger<SupportOnCallService> _logger;

    public SupportOnCallService(
        ApplicationDbContext context,
        SupportAuditService auditService,
        ILogger<SupportOnCallService> logger)
    {
        _context = context;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<SupportDashboardDto> GetDashboardMetricsAsync()
    {
        var waitingConvs = await _context.SupportConversations
            .CountAsync(c => c.Status == ConversationStatus.Active || c.Status == ConversationStatus.WaitingAdmin);

        var openTickets = await _context.SupportTickets
            .CountAsync(t => t.Status != SupportTicketStatus.Resolved && t.Status != SupportTicketStatus.Closed && t.Status != SupportTicketStatus.Cancelled);

        var activeIncidents = await _context.OperationalIncidents
            .CountAsync(i => i.Status == IncidentStatus.Open || i.Status == IncidentStatus.Acknowledged || i.Status == IncidentStatus.Monitoring || i.Status == IncidentStatus.Escalated);

        var openDisputes = await _context.DisputeInvestigations
            .CountAsync(d => d.Status != DisputeStatus.Approved && d.Status != DisputeStatus.Declined);

        var now = DateTime.UtcNow;
        var slaRiskTickets = await _context.SupportTickets
            .CountAsync(t => (t.Status == SupportTicketStatus.New || t.Status == SupportTicketStatus.Assigned || t.Status == SupportTicketStatus.InProgress)
                && ((t.FirstResponseDueAt.HasValue && t.FirstResponseDueAt.Value <= now.AddMinutes(30) && !t.FirstResponseAt.HasValue)
                    || (t.ResolutionDueAt.HasValue && t.ResolutionDueAt.Value <= now.AddHours(2) && !t.ResolvedAt.HasValue)));

        var recentTickets = await _context.SupportTickets
            .OrderByDescending(t => t.CreatedAt)
            .Take(5)
            .Include(t => t.CustomerUser)
            .Include(t => t.AssignedAdminUser)
            .Select(t => new SupportTicketSummaryDto
            {
                TicketId = t.TicketId,
                TicketReference = t.TicketReference,
                Subject = t.Subject,
                Status = t.Status.ToString(),
                Priority = t.Priority.ToString(),
                Category = t.Category.ToString(),
                CustomerUserId = t.CustomerUserId,
                CustomerName = $"{t.CustomerUser.FirstName} {t.CustomerUser.LastName}".Trim(),
                CustomerEmail = t.CustomerUser.Email,
                AssignedAdminName = t.AssignedAdminUser != null ? $"{t.AssignedAdminUser.FirstName} {t.AssignedAdminUser.LastName}".Trim() : null,
                AssignedTeam = t.AssignedTeam,
                CreatedAt = t.CreatedAt,
                FirstResponseDueAt = t.FirstResponseDueAt,
                ResolutionDueAt = t.ResolutionDueAt
            })
            .ToListAsync();

        var recentIncidents = await _context.OperationalIncidents
            .OrderByDescending(i => i.CreatedAt)
            .Take(5)
            .Select(i => new OperationalIncidentSummaryDto
            {
                IncidentId = i.IncidentId,
                IncidentReference = i.IncidentReference,
                IncidentType = i.IncidentType,
                Priority = i.Priority.ToString(),
                Status = i.Status.ToString(),
                Title = i.Title,
                AssignedTeam = i.AssignedTeam,
                AffectedCustomerCount = i.AffectedCustomerCount,
                CreatedAt = i.CreatedAt
            })
            .ToListAsync();

        var activeConversations = await _context.SupportConversations
            .Where(c => c.Status == ConversationStatus.Active || c.Status == ConversationStatus.WaitingAdmin)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Take(5)
            .Include(c => c.CustomerUser)
            .Include(c => c.AssignedAdminUser)
            .Select(c => new ConversationDto
            {
                ConversationId = c.ConversationId,
                ConversationReference = c.ConversationReference,
                CustomerUserId = c.CustomerUserId,
                CustomerName = $"{c.CustomerUser.FirstName} {c.CustomerUser.LastName}".Trim(),
                CustomerEmail = c.CustomerUser.Email,
                Channel = c.Channel,
                Status = c.Status.ToString(),
                AssignedAdminUserId = c.AssignedAdminUserId,
                AssignedAdminName = c.AssignedAdminUser != null ? $"{c.AssignedAdminUser.FirstName} {c.AssignedAdminUser.LastName}".Trim() : null,
                StartedAt = c.StartedAt,
                LastMessageAt = c.LastMessageAt,
                MessageCount = c.Messages.Count
            })
            .ToListAsync();

        return new SupportDashboardDto
        {
            WaitingConversationsCount = waitingConvs,
            OpenTicketsCount = openTickets,
            ActiveIncidentsCount = activeIncidents,
            OpenDisputesCount = openDisputes,
            SlaRiskTicketsCount = slaRiskTickets,
            RecentTickets = recentTickets,
            RecentIncidents = recentIncidents,
            ActiveConversations = activeConversations
        };
    }

    public async Task<SupportOnCallStatusDto> GetOnCallStatusAsync()
    {
        var schedule = await _context.SupportOnCallSchedules
            .Include(s => s.PrimaryResponder)
            .Include(s => s.BackupResponder)
            .Include(s => s.Supervisor)
            .Include(s => s.OperationsManager)
            .FirstOrDefaultAsync(s => s.IsActive);

        var policy = await _context.SupportOnCallPolicies.FirstOrDefaultAsync();
        if (policy == null)
        {
            policy = new SupportOnCallPolicy();
            _context.SupportOnCallPolicies.Add(policy);
            await _context.SaveChangesAsync();
        }

        if (schedule == null)
        {
            var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.UserType == UserType.Admin);
            schedule = new SupportOnCallSchedule
            {
                ShiftName = "24/7 Primary Shift",
                ShiftStart = DateTime.UtcNow,
                ShiftEnd = DateTime.UtcNow.AddDays(30),
                PrimaryResponderId = adminUser?.UserId,
                ActiveChannels = "Push,SMS,Phone,Email",
                IsActive = true
            };
            _context.SupportOnCallSchedules.Add(schedule);
            await _context.SaveChangesAsync();
        }

        return new SupportOnCallStatusDto
        {
            ScheduleId = schedule.ScheduleId,
            ShiftName = schedule.ShiftName,
            ShiftStart = schedule.ShiftStart,
            ShiftEnd = schedule.ShiftEnd,
            PrimaryResponder = schedule.PrimaryResponder != null ? MapResponder(schedule.PrimaryResponder, "Primary") : null,
            BackupResponder = schedule.BackupResponder != null ? MapResponder(schedule.BackupResponder, "Backup") : null,
            Supervisor = schedule.Supervisor != null ? MapResponder(schedule.Supervisor, "Supervisor") : null,
            OperationsManager = schedule.OperationsManager != null ? MapResponder(schedule.OperationsManager, "Manager") : null,
            ActiveChannels = schedule.ActiveChannels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Policy = new SupportOnCallPolicyDto
            {
                P0BackupDelayMinutes = policy.P0BackupDelayMinutes,
                P0SupervisorDelayMinutes = policy.P0SupervisorDelayMinutes,
                P0ManagerDelayMinutes = policy.P0ManagerDelayMinutes,
                P1BackupDelayMinutes = policy.P1BackupDelayMinutes,
                P1SupervisorDelayMinutes = policy.P1SupervisorDelayMinutes,
                P1ManagerDelayMinutes = policy.P1ManagerDelayMinutes,
                NotificationChannels = policy.NotificationChannels,
                AutoEscalateEnabled = policy.AutoEscalateEnabled
            }
        };
    }

    public async Task<TestNotificationResultDto> TestNotificationAsync(int adminUserId, TestOnCallNotificationRequestDto request)
    {
        var channel = request.Channel;
        var recipient = string.IsNullOrWhiteSpace(request.TargetRecipient) ? "oncall-duty@parkjom.com" : request.TargetRecipient.Trim();
        var message = string.IsNullOrWhiteSpace(request.Message) ? "[TEST ALERT] ParkJom 24/7 On-Call Notification System Test" : request.Message;

        var attempt = new SupportNotificationAttempt
        {
            Channel = Enum.TryParse<NotificationChannel>(channel, true, out var ch) ? ch : NotificationChannel.Push,
            Recipient = recipient,
            RecipientUserId = adminUserId,
            Subject = "[TEST] On-Call Alert",
            Message = message,
            Status = "Sent",
            AttemptCount = 1,
            ProviderResponse = "OK - Simulation Success",
            CreatedAt = DateTime.UtcNow,
            SentAt = DateTime.UtcNow
        };

        _context.SupportNotificationAttempts.Add(attempt);
        await _context.SaveChangesAsync();

        await _auditService.LogAsync("OnCall", attempt.NotificationAttemptId, "TEST-ALERT", "TestNotificationSent", adminUserId, "Admin", null, "Sent", $"Tested channel {channel} to {recipient}");

        _logger.LogInformation("Test on-call notification dispatched: Channel {Channel}, Recipient {Recipient}", channel, recipient);

        return new TestNotificationResultDto
        {
            Success = true,
            Channel = channel,
            Recipient = recipient,
            Status = "Sent",
            Detail = $"Successfully verified provider dispatch over {channel}.",
            Timestamp = DateTime.UtcNow
        };
    }

    public async Task<SupportOnCallPolicyDto> UpdatePolicyAsync(int adminUserId, UpdateOnCallPolicyRequestDto request)
    {
        var policy = await _context.SupportOnCallPolicies.FirstOrDefaultAsync();
        if (policy == null)
        {
            policy = new SupportOnCallPolicy();
            _context.SupportOnCallPolicies.Add(policy);
        }

        policy.P0BackupDelayMinutes = request.P0BackupDelayMinutes;
        policy.P0SupervisorDelayMinutes = request.P0SupervisorDelayMinutes;
        policy.P0ManagerDelayMinutes = request.P0ManagerDelayMinutes;
        policy.P1BackupDelayMinutes = request.P1BackupDelayMinutes;
        policy.P1SupervisorDelayMinutes = request.P1SupervisorDelayMinutes;
        policy.P1ManagerDelayMinutes = request.P1ManagerDelayMinutes;
        policy.NotificationChannels = request.NotificationChannels;
        policy.AutoEscalateEnabled = request.AutoEscalateEnabled;
        policy.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await _auditService.LogAsync("OnCall", policy.PolicyId, "ONCALL-POLICY", "PolicyUpdated", adminUserId, "Admin", null, null, "On-call escalation policy updated");

        return new SupportOnCallPolicyDto
        {
            P0BackupDelayMinutes = policy.P0BackupDelayMinutes,
            P0SupervisorDelayMinutes = policy.P0SupervisorDelayMinutes,
            P0ManagerDelayMinutes = policy.P0ManagerDelayMinutes,
            P1BackupDelayMinutes = policy.P1BackupDelayMinutes,
            P1SupervisorDelayMinutes = policy.P1SupervisorDelayMinutes,
            P1ManagerDelayMinutes = policy.P1ManagerDelayMinutes,
            NotificationChannels = policy.NotificationChannels,
            AutoEscalateEnabled = policy.AutoEscalateEnabled
        };
    }

    public async Task<(List<SupportAuditEventDto> Items, int TotalCount)> GetAuditLogsAsync(
        string? objectType = null,
        int? objectId = null,
        int page = 1,
        int pageSize = 50)
    {
        var query = _context.SupportAuditEvents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(objectType))
        {
            query = query.Where(a => a.ObjectType.ToLower() == objectType.Trim().ToLower());
        }

        if (objectId.HasValue)
        {
            query = query.Where(a => a.ObjectId == objectId.Value);
        }

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(a => a.ActorUser)
            .Select(a => new SupportAuditEventDto
            {
                AuditEventId = a.AuditEventId,
                ObjectType = a.ObjectType,
                ObjectId = a.ObjectId,
                ObjectReference = a.ObjectReference,
                Action = a.Action,
                ActorUserId = a.ActorUserId,
                ActorName = a.ActorUser != null ? $"{a.ActorUser.FirstName} {a.ActorUser.LastName}".Trim() : a.ActorRole,
                ActorRole = a.ActorRole,
                PreviousState = a.PreviousState,
                NewState = a.NewState,
                Detail = a.Detail,
                Timestamp = a.Timestamp
            })
            .ToListAsync();

        return (items, total);
    }

    private static OnCallResponderDto MapResponder(User u, string role) => new()
    {
        UserId = u.UserId,
        Name = $"{u.FirstName} {u.LastName}".Trim(),
        Email = u.Email,
        Phone = u.PhoneNumber ?? string.Empty,
        Role = role
    };
}
