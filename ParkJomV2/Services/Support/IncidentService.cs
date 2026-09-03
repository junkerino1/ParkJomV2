using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;

namespace ParkJomV2.Services.Support;

public class IncidentService
{
    private readonly ApplicationDbContext _context;
    private readonly SupportAuditService _auditService;
    private readonly AccessLogService _accessLogService;
    private readonly ISupportRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<IncidentService> _logger;

    public IncidentService(
        ApplicationDbContext context,
        SupportAuditService auditService,
        AccessLogService accessLogService,
        ISupportRealtimeNotifier realtimeNotifier,
        ILogger<IncidentService> logger)
    {
        _context = context;
        _auditService = auditService;
        _accessLogService = accessLogService;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    public async Task<(List<OperationalIncidentSummaryDto> Items, int TotalCount)> GetIncidentsAsync(
        string? status = null,
        string? priority = null,
        string? team = null,
        int page = 1,
        int pageSize = 25)
    {
        var query = _context.OperationalIncidents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<IncidentStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(i => i.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(priority) && Enum.TryParse<IncidentPriority>(priority, true, out var parsedPriority))
        {
            query = query.Where(i => i.Priority == parsedPriority);
        }

        if (!string.IsNullOrWhiteSpace(team))
        {
            query = query.Where(i => i.AssignedTeam == team);
        }

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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

        return (items, total);
    }

    public async Task<OperationalIncidentDto?> GetIncidentDetailAsync(int incidentId)
    {
        var incident = await _context.OperationalIncidents
            .Include(i => i.Property)
            .Include(i => i.ParkingSpot)
            .Include(i => i.IoTDevice)
            .Include(i => i.AssignedUser)
            .Include(i => i.IncidentTickets)
                .ThenInclude(it => it.Ticket)
                    .ThenInclude(t => t.CustomerUser)
            .Include(i => i.NotificationAttempts)
            .FirstOrDefaultAsync(i => i.IncidentId == incidentId);

        if (incident == null) return null;

        var auditTimeline = await _context.SupportAuditEvents
            .AsNoTracking()
            .Where(a => a.ObjectType == "Incident" && a.ObjectId == incidentId)
            .OrderBy(a => a.Timestamp)
            .Include(a => a.ActorUser)
            .Select(a => new SupportAuditEventDto
            {
                AuditEventId = a.AuditEventId,
                ObjectType = a.ObjectType,
                ObjectId = a.ObjectId,
                ObjectReference = a.ObjectReference,
                Action = a.Action,
                ActorUserId = a.ActorUserId,
                ActorName = a.ActorUser != null ? $"{a.ActorUser.FirstName} {a.ActorUser.LastName}".Trim() : "System",
                ActorRole = a.ActorRole,
                PreviousState = a.PreviousState,
                NewState = a.NewState,
                Detail = a.Detail,
                Timestamp = a.Timestamp
            })
            .ToListAsync();

        return new OperationalIncidentDto
        {
            IncidentId = incident.IncidentId,
            IncidentReference = incident.IncidentReference,
            IncidentType = incident.IncidentType,
            Priority = incident.Priority.ToString(),
            Status = incident.Status.ToString(),
            Title = incident.Title,
            Description = incident.Description,
            PropertyId = incident.PropertyId,
            PropertyName = incident.Property?.PropertyName,
            ParkingSpotId = incident.ParkingSpotId,
            SpotNumber = incident.ParkingSpot?.ParkingLabel ?? (incident.ParkingSpot != null ? $"Spot #{incident.ParkingSpot.ParkingSpotId}" : null),
            IoTDeviceId = incident.IoTDeviceId,
            Esp32Serial = incident.IoTDevice?.Esp32Serial,
            Source = incident.Source,
            AssignedTeam = incident.AssignedTeam,
            AssignedUserId = incident.AssignedUserId,
            AssignedUserName = incident.AssignedUser != null ? $"{incident.AssignedUser.FirstName} {incident.AssignedUser.LastName}".Trim() : null,
            AffectedCustomerCount = incident.AffectedCustomerCount,
            AcknowledgedAt = incident.AcknowledgedAt,
            ResolvedAt = incident.ResolvedAt,
            ClosedAt = incident.ClosedAt,
            EscalationLevel = incident.EscalationLevel,
            NextEscalationAt = incident.NextEscalationAt,
            CreatedAt = incident.CreatedAt,
            UpdatedAt = incident.UpdatedAt,
            LinkedTickets = incident.IncidentTickets.Select(it => new SupportTicketSummaryDto
            {
                TicketId = it.Ticket.TicketId,
                TicketReference = it.Ticket.TicketReference,
                Subject = it.Ticket.Subject,
                Status = it.Ticket.Status.ToString(),
                Priority = it.Ticket.Priority.ToString(),
                Category = it.Ticket.Category.ToString(),
                CustomerUserId = it.Ticket.CustomerUserId,
                CustomerName = $"{it.Ticket.CustomerUser?.FirstName} {it.Ticket.CustomerUser?.LastName}".Trim(),
                CustomerEmail = it.Ticket.CustomerUser?.Email ?? string.Empty,
                CreatedAt = it.Ticket.CreatedAt
            }).ToList(),
            NotificationAttempts = incident.NotificationAttempts.Select(na => new SupportNotificationAttemptDto
            {
                NotificationAttemptId = na.NotificationAttemptId,
                Channel = na.Channel.ToString(),
                Recipient = na.Recipient,
                Subject = na.Subject,
                Message = na.Message,
                Status = na.Status,
                AttemptCount = na.AttemptCount,
                CreatedAt = na.CreatedAt,
                SentAt = na.SentAt
            }).ToList(),
            AuditTimeline = auditTimeline
        };
    }

    public async Task<OperationalIncidentDto?> GetIncidentDetailByIdentifierAsync(string incidentIdentifier)
    {
        var trimmed = incidentIdentifier.Trim();
        var incident = int.TryParse(trimmed, out var incId)
            ? await _context.OperationalIncidents.FirstOrDefaultAsync(i => i.IncidentId == incId)
            : await _context.OperationalIncidents.FirstOrDefaultAsync(i => i.IncidentReference == trimmed);

        if (incident == null) return null;
        return await GetIncidentDetailAsync(incident.IncidentId);
    }

    public async Task<OperationalIncidentDto> CreateIncidentAsync(int actorUserId, CreateIncidentRequestDto request)
    {
        var now = DateTime.UtcNow;
        var priority = Enum.TryParse<IncidentPriority>(request.Priority, true, out var parsedPri) ? parsedPri : IncidentPriority.P1;

        var incident = new OperationalIncident
        {
            IncidentReference = $"INC-{now:yyyy}-{Random.Shared.Next(10000, 99999)}",
            IncidentType = string.IsNullOrWhiteSpace(request.IncidentType) ? "GateFailure" : request.IncidentType.Trim(),
            Priority = priority,
            Status = IncidentStatus.Open,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            PropertyId = request.PropertyId,
            ParkingSpotId = request.ParkingSpotId,
            IoTDeviceId = request.IoTDeviceId,
            Source = string.IsNullOrWhiteSpace(request.Source) ? "Admin" : request.Source.Trim(),
            AssignedTeam = string.IsNullOrWhiteSpace(request.AssignedTeam) ? "ParkingOperations" : request.AssignedTeam.Trim(),
            AssignedUserId = request.AssignedUserId,
            AffectedCustomerCount = 1,
            NextEscalationAt = priority == IncidentPriority.P0 ? now.AddMinutes(2) : now.AddMinutes(5),
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.OperationalIncidents.Add(incident);
        await _context.SaveChangesAsync();

        int? linkedTicketId = request.InitialTicketId;
        if (!linkedTicketId.HasValue && !string.IsNullOrWhiteSpace(request.InitialTicketReference))
        {
            var tRef = request.InitialTicketReference.Trim();
            var matchedTicket = int.TryParse(tRef, out var tId)
                ? await _context.SupportTickets.FindAsync(tId)
                : await _context.SupportTickets.FirstOrDefaultAsync(st => st.TicketReference == tRef);
            if (matchedTicket != null)
            {
                linkedTicketId = matchedTicket.TicketId;
            }
        }

        if (linkedTicketId.HasValue)
        {
            _context.IncidentTickets.Add(new IncidentTicket
            {
                IncidentId = incident.IncidentId,
                TicketId = linkedTicketId.Value,
                LinkedAt = now
            });
            await _context.SaveChangesAsync();
        }

        await _auditService.LogAsync("Incident", incident.IncidentId, incident.IncidentReference, "Created", actorUserId, "Admin", null, "Open", request.Title);
        await _realtimeNotifier.BroadcastEventAsync("incident.created", new { incidentId = incident.IncidentId, reference = incident.IncidentReference, priority = priority.ToString() });

        return (await GetIncidentDetailAsync(incident.IncidentId))!;
    }

    public async Task<OperationalIncidentDto> AcknowledgeIncidentAsync(int incidentId, int actorUserId)
    {
        var incident = await _context.OperationalIncidents.FindAsync(incidentId);
        if (incident == null) throw new KeyNotFoundException("Incident not found");

        var oldState = incident.Status.ToString();
        var now = DateTime.UtcNow;

        incident.Status = IncidentStatus.Acknowledged;
        incident.AcknowledgedAt = now;
        incident.AssignedUserId = actorUserId;
        incident.NextEscalationAt = null; // Stops escalation timer
        incident.UpdatedAt = now;

        await _context.SaveChangesAsync();
        await _auditService.LogAsync("Incident", incidentId, incident.IncidentReference, "Acknowledged", actorUserId, "Admin", oldState, "Acknowledged", $"Acknowledged by user {actorUserId}");
        await _realtimeNotifier.BroadcastEventAsync("incident.updated", new { incidentId = incidentId, status = "Acknowledged" });

        return (await GetIncidentDetailAsync(incidentId))!;
    }

    public async Task<OperationalIncidentDto> AssignIncidentAsync(int incidentId, int actorUserId, AssignIncidentRequestDto request)
    {
        var incident = await _context.OperationalIncidents.FindAsync(incidentId);
        if (incident == null) throw new KeyNotFoundException("Incident not found");

        var oldState = incident.Status.ToString();
        var now = DateTime.UtcNow;

        if (request.AssignedUserId.HasValue)
        {
            incident.AssignedUserId = request.AssignedUserId.Value;
        }
        if (!string.IsNullOrWhiteSpace(request.AssignedTeam))
        {
            incident.AssignedTeam = request.AssignedTeam.Trim();
        }

        incident.UpdatedAt = now;
        await _context.SaveChangesAsync();

        await _auditService.LogAsync("Incident", incidentId, incident.IncidentReference, "Assigned", actorUserId, "Admin", oldState, incident.Status.ToString(), $"Assigned to {incident.AssignedUserId} / {incident.AssignedTeam}");
        await _realtimeNotifier.BroadcastEventAsync("incident.updated", new { incidentId = incidentId, status = incident.Status.ToString() });

        return (await GetIncidentDetailAsync(incidentId))!;
    }

    public async Task<OperationalIncidentDto> TransitionStatusAsync(int incidentId, int actorUserId, IncidentTransitionRequestDto request)
    {
        var incident = await _context.OperationalIncidents.FindAsync(incidentId);
        if (incident == null) throw new KeyNotFoundException("Incident not found");

        if (!Enum.TryParse<IncidentStatus>(request.ToStatus, true, out var newStatus))
        {
            throw new ArgumentException($"Invalid incident status: {request.ToStatus}");
        }

        var oldState = incident.Status.ToString();
        var now = DateTime.UtcNow;

        incident.Status = newStatus;
        if (newStatus == IncidentStatus.Resolved)
        {
            incident.ResolvedAt = now;
            incident.NextEscalationAt = null;
        }
        else if (newStatus == IncidentStatus.Closed)
        {
            incident.ClosedAt = now;
            incident.NextEscalationAt = null;
        }

        incident.UpdatedAt = now;
        await _context.SaveChangesAsync();

        await _auditService.LogAsync("Incident", incidentId, incident.IncidentReference, "StatusChanged", actorUserId, "Admin", oldState, newStatus.ToString(), request.Reason);
        await _realtimeNotifier.BroadcastEventAsync("incident.updated", new { incidentId = incidentId, status = newStatus.ToString() });

        return (await GetIncidentDetailAsync(incidentId))!;
    }

    public async Task<AccessOverrideResultDto> ExecuteAccessOverrideAsync(int incidentId, int adminUserId, AccessOverrideRequestDto request)
    {
        var incident = await _context.OperationalIncidents
            .Include(i => i.ParkingSpot)
                .ThenInclude(p => p!.IoTDevice)
            .FirstOrDefaultAsync(i => i.IncidentId == incidentId);

        if (incident == null) throw new KeyNotFoundException("Incident not found");

        var booking = await _context.Bookings
            .Include(b => b.ParkingSpot)
                .ThenInclude(p => p!.IoTDevice)
            .FirstOrDefaultAsync(b => b.BookingId == request.BookingId);

        if (booking == null) throw new KeyNotFoundException("Booking not found");

        var now = DateTime.UtcNow;
        var commandId = string.IsNullOrWhiteSpace(request.CommandId) ? $"CMD-OVERRIDE-{Guid.NewGuid():N}" : request.CommandId;
        var iot = incident.ParkingSpot?.IoTDevice ?? booking.ParkingSpot?.IoTDevice;

        // 1. Audit in AccessLogs
        await _accessLogService.LogAsync(adminUserId, $"Gate Access Override ({commandId})", true, $"Executed for Booking #{booking.BookingReference}. Reason: {request.Reason}", booking.BookingId, iot?.IoTDeviceId);

        // 2. Audit in SupportAuditEvents
        await _auditService.LogAsync("Incident", incidentId, incident.IncidentReference, "OverrideExecuted", adminUserId, "Admin", null, null, $"Gate barrier override executed for booking #{booking.BookingReference}. CommandId: {commandId}. Reason: {request.Reason}");

        _logger.LogInformation("Admin {AdminId} executed barrier access override on Booking {BookingId}, Spot {SpotId}, CommandId {CommandId}",
            adminUserId, booking.BookingId, booking.ParkingSpotId, commandId);

        return new AccessOverrideResultDto
        {
            Success = true,
            CommandId = commandId,
            Message = $"Barrier access override successfully dispatched and audited for Booking {booking.BookingReference}.",
            BookingId = booking.BookingId,
            IoTDeviceId = iot?.IoTDeviceId,
            ExecutedAt = now
        };
    }

    public async Task<OperationalIncidentDto> LinkTicketAsync(string incidentIdentifier, LinkTicketRequestDto request, int actorAdminUserId)
    {
        var trimmedInc = incidentIdentifier.Trim();
        var incident = int.TryParse(trimmedInc, out var incId)
            ? await _context.OperationalIncidents.FindAsync(incId)
            : await _context.OperationalIncidents.FirstOrDefaultAsync(i => i.IncidentReference == trimmedInc);

        SupportTicket? ticket = null;
        if (request.TicketId.HasValue && request.TicketId.Value > 0)
        {
            ticket = await _context.SupportTickets.FindAsync(request.TicketId.Value);
        }

        if (ticket == null && !string.IsNullOrWhiteSpace(request.TicketReference))
        {
            var refTrim = request.TicketReference.Trim();
            ticket = await _context.SupportTickets.FirstOrDefaultAsync(t => t.TicketReference == refTrim);
            if (ticket == null && int.TryParse(refTrim, out var parsedTId))
            {
                ticket = await _context.SupportTickets.FindAsync(parsedTId);
            }
        }

        if (incident == null)
            throw new KeyNotFoundException($"Operational incident '{incidentIdentifier}' was not found.");

        if (ticket == null)
        {
            var requestedRef = !string.IsNullOrWhiteSpace(request.TicketReference)
                ? request.TicketReference.Trim()
                : (request.TicketId.HasValue ? request.TicketId.Value.ToString() : null);

            if (string.IsNullOrWhiteSpace(requestedRef))
            {
                throw new ArgumentException("Please provide a valid Ticket ID or Reference (e.g. TKT-2026-XXXXX).");
            }
            throw new KeyNotFoundException($"Support ticket '{requestedRef}' was not found.");
        }

        ticket.OperationalIncidentId = incident.IncidentId;
        var existingJoin = await _context.IncidentTickets.FirstOrDefaultAsync(it => it.IncidentId == incident.IncidentId && it.TicketId == ticket.TicketId);
        if (existingJoin == null)
        {
            _context.IncidentTickets.Add(new IncidentTicket { IncidentId = incident.IncidentId, TicketId = ticket.TicketId, LinkedAt = DateTime.UtcNow });
        }

        ticket.UpdatedAt = DateTime.UtcNow;
        incident.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _auditService.LogAsync("Incident", incident.IncidentId, incident.IncidentReference, "LinkedTicket", actorAdminUserId, "Admin", null, null, $"Linked with Ticket #{ticket.TicketReference}");
        await _auditService.LogAsync("Ticket", ticket.TicketId, ticket.TicketReference, "LinkedIncident", actorAdminUserId, "Admin", null, null, $"Linked with Incident #{incident.IncidentReference}");
        await _realtimeNotifier.BroadcastEventAsync("incident.updated", new { incidentId = incident.IncidentId, ticketId = ticket.TicketId, ticketReference = ticket.TicketReference });
        await _realtimeNotifier.BroadcastEventAsync("ticket.updated", new { ticketId = ticket.TicketId, incidentId = incident.IncidentId, incidentReference = incident.IncidentReference }, ticket.CustomerUserId, ticket.TicketId);

        return (await GetIncidentDetailAsync(incident.IncidentId))!;
    }
}
