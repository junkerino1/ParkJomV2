using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;

namespace ParkJomV2.Services.Support;

public class SupportTicketService
{
    private readonly ApplicationDbContext _context;
    private readonly SupportAuditService _auditService;
    private readonly ISupportRealtimeNotifier _realtimeNotifier;
    private readonly CloudinaryService _cloudinaryService;
    private readonly ILogger<SupportTicketService> _logger;

    public SupportTicketService(
        ApplicationDbContext context,
        SupportAuditService auditService,
        ISupportRealtimeNotifier realtimeNotifier,
        CloudinaryService cloudinaryService,
        ILogger<SupportTicketService> logger)
    {
        _context = context;
        _auditService = auditService;
        _realtimeNotifier = realtimeNotifier;
        _cloudinaryService = cloudinaryService;
        _logger = logger;
    }

    public async Task<List<SupportTicketSummaryDto>> GetMyTicketsAsync(int userId, string? status = null, string? search = null)
    {
        var query = _context.SupportTickets
            .AsNoTracking()
            .Where(t => t.CustomerUserId == userId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<SupportTicketStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(t => t.Status == parsedStatus);
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(t => t.Subject.ToLower().Contains(term)
                || t.TicketReference.ToLower().Contains(term)
                || t.Description.ToLower().Contains(term));
        }

        var tickets = await query
            .OrderByDescending(t => t.CreatedAt)
            .Include(t => t.CustomerUser)
            .Include(t => t.AssignedAdminUser)
            .Include(t => t.Messages)
            .ToListAsync();

        return tickets.Select(MapToSummaryDto).ToList();
    }

    public async Task<(List<SupportTicketSummaryDto> Items, int TotalCount)> GetAdminTicketsAsync(
        string? status = null,
        string? priority = null,
        string? team = null,
        int? assigneeId = null,
        string? search = null,
        int page = 1,
        int pageSize = 25)
    {
        var query = _context.SupportTickets
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<SupportTicketStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(t => t.Status == parsedStatus);
            }
        }

        if (!string.IsNullOrWhiteSpace(priority))
        {
            if (Enum.TryParse<SupportTicketPriority>(priority, true, out var parsedPriority))
            {
                query = query.Where(t => t.Priority == parsedPriority);
            }
        }

        if (!string.IsNullOrWhiteSpace(team))
        {
            query = query.Where(t => t.AssignedTeam == team);
        }

        if (assigneeId.HasValue)
        {
            query = query.Where(t => t.AssignedAdminUserId == assigneeId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(t => t.Subject.ToLower().Contains(term)
                || t.TicketReference.ToLower().Contains(term)
                || (t.CustomerUser != null && (t.CustomerUser.Email.ToLower().Contains(term) || t.CustomerUser.FirstName!.ToLower().Contains(term))));
        }

        var total = await query.CountAsync();

        var tickets = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(t => t.CustomerUser)
            .Include(t => t.AssignedAdminUser)
            .Include(t => t.Messages)
            .ToListAsync();

        return (tickets.Select(MapToSummaryDto).ToList(), total);
    }

    public async Task<SupportTicketDto?> GetTicketDetailAsync(int ticketId, int callerUserId, bool isAdmin)
    {
        var query = _context.SupportTickets
            .Include(t => t.CustomerUser)
            .Include(t => t.AssignedAdminUser)
            .Include(t => t.Conversation)
            .Include(t => t.WorkflowRun)
            .Include(t => t.Booking)
            .Include(t => t.OperationalIncident)
            .Include(t => t.DisputeInvestigation)
            .Include(t => t.Messages)
                .ThenInclude(m => m.SenderUser)
            .Include(t => t.Messages)
                .ThenInclude(m => m.Attachments)
            .Include(t => t.Attachments)
            .AsQueryable();

        var ticket = await query.FirstOrDefaultAsync(t => t.TicketId == ticketId);
        if (ticket == null) return null;
        if (!isAdmin && ticket.CustomerUserId != callerUserId) return null;

        var auditTimeline = new List<SupportAuditEventDto>();
        if (isAdmin)
        {
            auditTimeline = await _context.SupportAuditEvents
                .AsNoTracking()
                .Where(a => a.ObjectType == "Ticket" && a.ObjectId == ticketId)
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
        }

        // Filter out internal messages for customers
        var visibleMessages = ticket.Messages
            .Where(m => isAdmin || !m.IsInternal)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new SupportTicketMessageDto
            {
                MessageId = m.MessageId,
                TicketId = m.TicketId,
                SenderUserId = m.SenderUserId,
                SenderName = m.SenderUser != null ? $"{m.SenderUser.FirstName} {m.SenderUser.LastName}".Trim() : m.SenderRole,
                SenderRole = m.SenderRole,
                Body = m.Body,
                IsInternal = m.IsInternal,
                CreatedAt = m.CreatedAt,
                Attachments = m.Attachments.Select(a => new SupportAttachmentDto
                {
                    AttachmentId = a.AttachmentId,
                    FileName = a.FileName,
                    FileUrl = a.FileUrl,
                    ContentType = a.ContentType,
                    FileSize = a.FileSize,
                    IsPrivate = a.IsPrivate,
                    CreatedAt = a.CreatedAt
                }).ToList()
            })
            .ToList();

        return new SupportTicketDto
        {
            TicketId = ticket.TicketId,
            TicketReference = ticket.TicketReference,
            TicketType = ticket.TicketType.ToString(),
            Source = ticket.Source.ToString(),
            Category = ticket.Category.ToString(),
            Priority = ticket.Priority.ToString(),
            Status = ticket.Status.ToString(),
            Subject = ticket.Subject,
            Description = ticket.Description,
            CustomerUserId = ticket.CustomerUserId,
            CustomerName = $"{ticket.CustomerUser.FirstName} {ticket.CustomerUser.LastName}".Trim(),
            CustomerEmail = ticket.CustomerUser.Email,
            CustomerRole = ticket.CustomerUser.UserType.ToString(),
            AssignedAdminUserId = ticket.AssignedAdminUserId,
            AssignedAdminName = ticket.AssignedAdminUser != null ? $"{ticket.AssignedAdminUser.FirstName} {ticket.AssignedAdminUser.LastName}".Trim() : null,
            AssignedTeam = ticket.AssignedTeam,
            ConversationId = ticket.ConversationId,
            ConversationReference = ticket.Conversation?.ConversationReference,
            WorkflowRunId = ticket.WorkflowRunId,
            WorkflowRunReference = ticket.WorkflowRun?.RunReference,
            BookingId = ticket.BookingId,
            BookingReference = ticket.Booking?.BookingReference,
            ParkingSpotId = ticket.ParkingSpotId,
            VehicleId = ticket.VehicleId,
            OperationalIncidentId = ticket.OperationalIncidentId,
            IncidentReference = ticket.OperationalIncident?.IncidentReference,
            DisputeInvestigationId = ticket.DisputeInvestigationId,
            DisputeReference = ticket.DisputeInvestigation?.DisputeReference,
            AcceptedAt = ticket.AcceptedAt,
            FirstResponseAt = ticket.FirstResponseAt,
            FirstResponseDueAt = ticket.FirstResponseDueAt,
            ResolvedAt = ticket.ResolvedAt,
            ResolutionDueAt = ticket.ResolutionDueAt,
            ClosedAt = ticket.ClosedAt,
            ResolutionCode = ticket.ResolutionCode,
            InternalSummary = isAdmin ? ticket.InternalSummary : null,
            CreatedAt = ticket.CreatedAt,
            UpdatedAt = ticket.UpdatedAt,
            Messages = visibleMessages,
            Attachments = ticket.Attachments.Select(a => new SupportAttachmentDto
            {
                AttachmentId = a.AttachmentId,
                FileName = a.FileName,
                FileUrl = a.FileUrl,
                ContentType = a.ContentType,
                FileSize = a.FileSize,
                IsPrivate = a.IsPrivate,
                CreatedAt = a.CreatedAt
            }).ToList(),
            AuditTimeline = auditTimeline
        };
    }

    public async Task<SupportTicketDto?> GetTicketDetailByIdentifierAsync(string ticketIdentifier, int callerUserId, bool isAdmin)
    {
        var trimmed = ticketIdentifier.Trim();
        int? resolvedTicketId = int.TryParse(trimmed, out var tId)
            ? tId
            : await _context.SupportTickets.Where(t => t.TicketReference == trimmed).Select(t => (int?)t.TicketId).FirstOrDefaultAsync();

        if (!resolvedTicketId.HasValue) return null;
        return await GetTicketDetailAsync(resolvedTicketId.Value, callerUserId, isAdmin);
    }

    public async Task<SupportTicketDto> CreateCustomerTicketAsync(int userId, CreateSupportTicketRequestDto request)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) throw new KeyNotFoundException("User not found");

        var now = DateTime.UtcNow;
        var priority = ParsePriority(request.Priority);
        var category = ParseCategory(request.Category);
        var (firstResponseHours, resolutionHours) = CalculateSlaHours(priority);

        var ticket = new SupportTicket
        {
            TicketReference = $"TKT-{now:yyyy}-{Random.Shared.Next(10000, 99999)}",
            TicketType = SupportTicketType.Custom,
            Source = request.ConversationId.HasValue ? SupportSource.LiveChat : SupportSource.QuickHelp,
            Category = category,
            Priority = priority,
            CustomerUserId = userId,
            CreatedByUserId = userId,
            AssignedTeam = DetermineDefaultTeam(category),
            BookingId = request.BookingId,
            ParkingSpotId = request.ParkingSpotId,
            VehicleId = request.VehicleId,
            ConversationId = request.ConversationId,
            Subject = string.IsNullOrWhiteSpace(request.Subject) ? "Support Request" : request.Subject.Trim(),
            Description = request.Message.Trim(),
            Status = SupportTicketStatus.New,
            FirstResponseDueAt = now.AddHours(firstResponseHours),
            ResolutionDueAt = now.AddHours(resolutionHours),
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.SupportTickets.Add(ticket);
        await _context.SaveChangesAsync();

        var message = new SupportTicketMessage
        {
            TicketId = ticket.TicketId,
            SenderUserId = userId,
            SenderRole = "Customer",
            Body = request.Message,
            CreatedAt = now
        };
        _context.SupportTicketMessages.Add(message);
        await _context.SaveChangesAsync();

        if (request.Attachments != null && request.Attachments.Count > 0)
        {
            await SaveAttachmentsAsync(request.Attachments, userId, ticket.TicketId, message.MessageId);
        }

        await _auditService.LogAsync("Ticket", ticket.TicketId, ticket.TicketReference, "Created", userId, "Customer", null, "New", "Ticket created by customer");
        await _realtimeNotifier.BroadcastEventAsync("ticket.created", new { ticketId = ticket.TicketId, reference = ticket.TicketReference }, userId, ticket.TicketId);

        return (await GetTicketDetailAsync(ticket.TicketId, userId, false))!;
    }

    public async Task<SupportTicketDto> AdminCreateTicketAsync(int adminUserId, AdminCreateSupportTicketRequestDto request)
    {
        var now = DateTime.UtcNow;
        int targetCustomerId = adminUserId;

        if (request.CustomerUserId.HasValue)
        {
            targetCustomerId = request.CustomerUserId.Value;
        }
        else if (!string.IsNullOrWhiteSpace(request.CustomerEmail))
        {
            var matchedUser = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == request.CustomerEmail.Trim().ToLower());
            if (matchedUser != null)
            {
                targetCustomerId = matchedUser.UserId;
            }
        }

        var priority = ParsePriority(request.Priority);
        var category = ParseCategory(request.Category);
        var (firstResponseHours, resolutionHours) = CalculateSlaHours(priority);

        var ticket = new SupportTicket
        {
            TicketReference = $"TKT-{now:yyyy}-{Random.Shared.Next(10000, 99999)}",
            TicketType = SupportTicketType.Custom,
            Source = SupportSource.Admin,
            Category = category,
            Priority = priority,
            CustomerUserId = targetCustomerId,
            CreatedByUserId = adminUserId,
            AssignedAdminUserId = request.AssignedAdminUserId ?? adminUserId,
            AssignedTeam = string.IsNullOrWhiteSpace(request.AssignedTeam) ? DetermineDefaultTeam(category) : request.AssignedTeam,
            BookingId = request.BookingId,
            ParkingSpotId = request.ParkingSpotId,
            VehicleId = request.VehicleId,
            ConversationId = request.ConversationId,
            Subject = string.IsNullOrWhiteSpace(request.Subject) ? "Admin Created Ticket" : request.Subject.Trim(),
            Description = request.Message.Trim(),
            InternalSummary = request.InternalSummary,
            Status = request.AssignedAdminUserId.HasValue || adminUserId > 0 ? SupportTicketStatus.Assigned : SupportTicketStatus.New,
            AcceptedAt = now,
            FirstResponseDueAt = now.AddHours(firstResponseHours),
            ResolutionDueAt = now.AddHours(resolutionHours),
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.SupportTickets.Add(ticket);
        await _context.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(request.Message))
        {
            var msg = new SupportTicketMessage
            {
                TicketId = ticket.TicketId,
                SenderUserId = adminUserId,
                SenderRole = "Admin",
                Body = request.Message,
                CreatedAt = now
            };
            _context.SupportTicketMessages.Add(msg);
            await _context.SaveChangesAsync();

            if (request.Attachments != null && request.Attachments.Count > 0)
            {
                await SaveAttachmentsAsync(request.Attachments, adminUserId, ticket.TicketId, msg.MessageId);
            }
        }

        await _auditService.LogAsync("Ticket", ticket.TicketId, ticket.TicketReference, "Created", adminUserId, "Admin", null, ticket.Status.ToString(), "Created by Admin");
        await _realtimeNotifier.BroadcastEventAsync("ticket.created", new { ticketId = ticket.TicketId, reference = ticket.TicketReference }, targetCustomerId, ticket.TicketId);

        return (await GetTicketDetailAsync(ticket.TicketId, adminUserId, true))!;
    }

    public async Task<SupportTicketMessageDto> AddMessageAsync(int ticketId, int senderUserId, string role, SendTicketMessageRequestDto request)
    {
        var ticket = await _context.SupportTickets.FindAsync(ticketId);
        if (ticket == null) throw new KeyNotFoundException("Ticket not found");

        var now = DateTime.UtcNow;
        var message = new SupportTicketMessage
        {
            TicketId = ticketId,
            SenderUserId = senderUserId,
            SenderRole = role,
            Body = request.Message,
            IsInternal = request.IsInternal,
            CreatedAt = now
        };

        _context.SupportTicketMessages.Add(message);

        // Update ticket response timestamps and status flow
        if (role == "Admin")
        {
            if (!ticket.FirstResponseAt.HasValue && !request.IsInternal)
            {
                ticket.FirstResponseAt = now;
            }
            if (ticket.Status == SupportTicketStatus.Assigned || ticket.Status == SupportTicketStatus.New)
            {
                ticket.Status = SupportTicketStatus.InProgress;
            }
        }
        else
        {
            if (ticket.Status == SupportTicketStatus.WaitingForCustomer)
            {
                ticket.Status = SupportTicketStatus.InProgress;
            }
        }

        ticket.UpdatedAt = now;
        await _context.SaveChangesAsync();

        if (request.Attachments != null && request.Attachments.Count > 0)
        {
            await SaveAttachmentsAsync(request.Attachments, senderUserId, ticketId, message.MessageId);
        }

        await _auditService.LogAsync("Ticket", ticketId, ticket.TicketReference, "MessageAdded", senderUserId, role, null, ticket.Status.ToString(), $"Message added by {role}");
        await _realtimeNotifier.BroadcastEventAsync("message.created", new { ticketId = ticketId, messageId = message.MessageId, isInternal = message.IsInternal }, ticket.CustomerUserId, ticketId);

        var sender = await _context.Users.FindAsync(senderUserId);
        return new SupportTicketMessageDto
        {
            MessageId = message.MessageId,
            TicketId = message.TicketId,
            SenderUserId = senderUserId,
            SenderName = sender != null ? $"{sender.FirstName} {sender.LastName}".Trim() : role,
            SenderRole = role,
            Body = message.Body,
            IsInternal = message.IsInternal,
            CreatedAt = message.CreatedAt
        };
    }

    public async Task<SupportTicketDto> AcceptTicketAsync(int ticketId, int adminUserId)
    {
        var ticket = await _context.SupportTickets.FindAsync(ticketId);
        if (ticket == null) throw new KeyNotFoundException("Ticket not found");

        var oldState = ticket.Status.ToString();
        var now = DateTime.UtcNow;

        ticket.AssignedAdminUserId = adminUserId;
        ticket.AcceptedAt = now;
        ticket.Status = SupportTicketStatus.Assigned;
        ticket.UpdatedAt = now;

        await _context.SaveChangesAsync();
        await _auditService.LogAsync("Ticket", ticketId, ticket.TicketReference, "Assigned", adminUserId, "Admin", oldState, "Assigned", $"Accepted by Admin user {adminUserId}");
        await _realtimeNotifier.BroadcastEventAsync("ticket.updated", new { ticketId = ticketId, status = "Assigned", assignedAdminUserId = adminUserId }, ticket.CustomerUserId, ticketId);

        return (await GetTicketDetailAsync(ticketId, adminUserId, true))!;
    }

    public async Task<SupportTicketDto> AssignTicketAsync(int ticketId, int actorAdminUserId, AssignTicketRequestDto request)
    {
        var ticket = await _context.SupportTickets.FindAsync(ticketId);
        if (ticket == null) throw new KeyNotFoundException("Ticket not found");

        var oldState = ticket.Status.ToString();
        var now = DateTime.UtcNow;

        if (request.AssignedAdminUserId.HasValue)
        {
            ticket.AssignedAdminUserId = request.AssignedAdminUserId.Value;
            ticket.AcceptedAt = now;
            ticket.Status = SupportTicketStatus.Assigned;
        }

        if (!string.IsNullOrWhiteSpace(request.AssignedTeam))
        {
            ticket.AssignedTeam = request.AssignedTeam.Trim();
        }

        ticket.UpdatedAt = now;
        await _context.SaveChangesAsync();

        await _auditService.LogAsync("Ticket", ticketId, ticket.TicketReference, "Assigned", actorAdminUserId, "Admin", oldState, ticket.Status.ToString(), $"Assigned to {ticket.AssignedAdminUserId} / {ticket.AssignedTeam}");
        await _realtimeNotifier.BroadcastEventAsync("ticket.updated", new { ticketId = ticketId, status = ticket.Status.ToString(), assignedAdminUserId = ticket.AssignedAdminUserId }, ticket.CustomerUserId, ticketId);

        return (await GetTicketDetailAsync(ticketId, actorAdminUserId, true))!;
    }

    public async Task<SupportTicketDto> TransitionStatusAsync(int ticketId, int actorUserId, string role, TicketTransitionRequestDto request)
    {
        var ticket = await _context.SupportTickets.FindAsync(ticketId);
        if (ticket == null) throw new KeyNotFoundException("Ticket not found");

        if (!Enum.TryParse<SupportTicketStatus>(request.ToStatus, true, out var newStatus))
        {
            throw new ArgumentException($"Invalid status: {request.ToStatus}");
        }

        var oldState = ticket.Status.ToString();
        var now = DateTime.UtcNow;

        ticket.Status = newStatus;
        if (newStatus == SupportTicketStatus.Resolved)
        {
            ticket.ResolvedAt = now;
            ticket.ResolutionCode = request.ResolutionCode ?? "ResolvedByAdmin";
        }
        else if (newStatus == SupportTicketStatus.Closed)
        {
            ticket.ClosedAt = now;
        }

        ticket.UpdatedAt = now;
        await _context.SaveChangesAsync();

        await _auditService.LogAsync("Ticket", ticketId, ticket.TicketReference, "StatusChanged", actorUserId, role, oldState, newStatus.ToString(), request.Reason);
        await _realtimeNotifier.BroadcastEventAsync("ticket.updated", new { ticketId = ticketId, status = newStatus.ToString() }, ticket.CustomerUserId, ticketId);

        return (await GetTicketDetailAsync(ticketId, actorUserId, role == "Admin"))!;
    }

    public async Task<SupportTicketDto> ReopenTicketAsync(int ticketId, int actorUserId, string role, ReopenTicketRequestDto request)
    {
        var ticket = await _context.SupportTickets.FindAsync(ticketId);
        if (ticket == null) throw new KeyNotFoundException("Ticket not found");

        var oldState = ticket.Status.ToString();
        var now = DateTime.UtcNow;

        ticket.Status = SupportTicketStatus.Reopened;
        ticket.ResolvedAt = null;
        ticket.ClosedAt = null;
        ticket.UpdatedAt = now;

        await _context.SaveChangesAsync();

        var msg = new SupportTicketMessage
        {
            TicketId = ticketId,
            SenderUserId = actorUserId,
            SenderRole = role,
            Body = $"[Ticket Reopened] Reason: {request.Reason}",
            CreatedAt = now
        };
        _context.SupportTicketMessages.Add(msg);
        await _context.SaveChangesAsync();

        await _auditService.LogAsync("Ticket", ticketId, ticket.TicketReference, "Reopened", actorUserId, role, oldState, "Reopened", request.Reason);
        await _realtimeNotifier.BroadcastEventAsync("ticket.updated", new { ticketId = ticketId, status = "Reopened" }, ticket.CustomerUserId, ticketId);

        return (await GetTicketDetailAsync(ticketId, actorUserId, role == "Admin"))!;
    }

    public async Task<SupportTicketDto> LinkIncidentAsync(string ticketIdentifier, LinkIncidentRequestDto request, int actorAdminUserId)
    {
        if (string.IsNullOrWhiteSpace(ticketIdentifier))
            throw new ArgumentException("Ticket identifier is required.");

        var trimmedTicket = ticketIdentifier.Trim();
        var ticket = int.TryParse(trimmedTicket, out var tId)
            ? await _context.SupportTickets.FindAsync(tId)
            : await _context.SupportTickets.FirstOrDefaultAsync(t => t.TicketReference == trimmedTicket);

        if (ticket == null)
            throw new KeyNotFoundException($"Support ticket '{ticketIdentifier}' was not found.");

        OperationalIncident? incident = null;
        if (request.IncidentId.HasValue && request.IncidentId.Value > 0)
        {
            incident = await _context.OperationalIncidents.FindAsync(request.IncidentId.Value);
        }

        if (incident == null && !string.IsNullOrWhiteSpace(request.IncidentReference))
        {
            var refTrim = request.IncidentReference.Trim();
            incident = await _context.OperationalIncidents.FirstOrDefaultAsync(i => i.IncidentReference == refTrim);
            if (incident == null && int.TryParse(refTrim, out var parsedIncId))
            {
                incident = await _context.OperationalIncidents.FindAsync(parsedIncId);
            }
        }

        if (incident == null)
        {
            var requestedRef = !string.IsNullOrWhiteSpace(request.IncidentReference)
                ? request.IncidentReference.Trim()
                : (request.IncidentId.HasValue ? request.IncidentId.Value.ToString() : null);

            if (string.IsNullOrWhiteSpace(requestedRef))
            {
                throw new ArgumentException("Please provide a valid Incident ID or Reference (e.g. INC-2026-XXXXX).");
            }
            throw new KeyNotFoundException($"Operational incident '{requestedRef}' was not found.");
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

        await _auditService.LogAsync("Ticket", ticket.TicketId, ticket.TicketReference, "LinkedIncident", actorAdminUserId, "Admin", null, null, $"Linked with Incident #{incident.IncidentReference}");
        await _auditService.LogAsync("Incident", incident.IncidentId, incident.IncidentReference, "LinkedTicket", actorAdminUserId, "Admin", null, null, $"Linked with Ticket #{ticket.TicketReference}");
        await _realtimeNotifier.BroadcastEventAsync("ticket.updated", new { ticketId = ticket.TicketId, incidentId = incident.IncidentId, incidentReference = incident.IncidentReference }, ticket.CustomerUserId, ticket.TicketId);
        await _realtimeNotifier.BroadcastEventAsync("incident.updated", new { incidentId = incident.IncidentId, ticketId = ticket.TicketId, ticketReference = ticket.TicketReference });

        return (await GetTicketDetailAsync(ticket.TicketId, actorAdminUserId, true))!;
    }

    public Task<SupportTicketDto> LinkIncidentAsync(int ticketId, int incidentId, int actorAdminUserId)
    {
        return LinkIncidentAsync(ticketId.ToString(), new LinkIncidentRequestDto { IncidentId = incidentId }, actorAdminUserId);
    }

    public async Task<SupportTicketDto> LinkDisputeAsync(string ticketIdentifier, LinkDisputeRequestDto request, int actorAdminUserId)
    {
        if (string.IsNullOrWhiteSpace(ticketIdentifier))
            throw new ArgumentException("Ticket identifier is required.");

        var trimmedTicket = ticketIdentifier.Trim();
        var ticket = int.TryParse(trimmedTicket, out var tId)
            ? await _context.SupportTickets.FindAsync(tId)
            : await _context.SupportTickets.FirstOrDefaultAsync(t => t.TicketReference == trimmedTicket);

        if (ticket == null)
            throw new KeyNotFoundException($"Support ticket '{ticketIdentifier}' was not found.");

        DisputeInvestigation? dispute = null;
        if (request.DisputeId.HasValue && request.DisputeId.Value > 0)
        {
            dispute = await _context.DisputeInvestigations.FindAsync(request.DisputeId.Value);
        }

        if (dispute == null && !string.IsNullOrWhiteSpace(request.DisputeReference))
        {
            var refTrim = request.DisputeReference.Trim();
            dispute = await _context.DisputeInvestigations.FirstOrDefaultAsync(d => d.DisputeReference == refTrim);
            if (dispute == null && int.TryParse(refTrim, out var parsedDId))
            {
                dispute = await _context.DisputeInvestigations.FindAsync(parsedDId);
            }
        }

        if (dispute == null)
        {
            var requestedRef = !string.IsNullOrWhiteSpace(request.DisputeReference)
                ? request.DisputeReference.Trim()
                : (request.DisputeId.HasValue ? request.DisputeId.Value.ToString() : null);

            if (string.IsNullOrWhiteSpace(requestedRef))
            {
                throw new ArgumentException("Please provide a valid Dispute ID or Reference (e.g. DSP-2026-XXXXX).");
            }
            throw new KeyNotFoundException($"Dispute investigation '{requestedRef}' was not found.");
        }

        ticket.DisputeInvestigationId = dispute.DisputeId;
        dispute.TicketId = ticket.TicketId;
        ticket.UpdatedAt = DateTime.UtcNow;
        dispute.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await _auditService.LogAsync("Ticket", ticket.TicketId, ticket.TicketReference, "LinkedDispute", actorAdminUserId, "Admin", null, null, $"Linked with Dispute #{dispute.DisputeReference}");

        return (await GetTicketDetailAsync(ticket.TicketId, actorAdminUserId, true))!;
    }

    public Task<SupportTicketDto> LinkDisputeAsync(int ticketId, int disputeId, int actorAdminUserId)
    {
        return LinkDisputeAsync(ticketId.ToString(), new LinkDisputeRequestDto { DisputeId = disputeId }, actorAdminUserId);
    }

    private async Task SaveAttachmentsAsync(List<IFormFile> files, int userId, int ticketId, int? messageId = null)
    {
        foreach (var file in files)
        {
            try
            {
                var uploadResult = await _cloudinaryService.UploadPrivateDocumentAsync(file, "support_attachments");
                var attachment = new SupportAttachment
                {
                    TicketId = ticketId,
                    TicketMessageId = messageId,
                    FileName = file.FileName,
                    FileUrl = uploadResult.SecureUrl?.ToString() ?? uploadResult.Url?.ToString() ?? string.Empty,
                    ContentType = file.ContentType,
                    FileSize = file.Length,
                    UploadedByUserId = userId,
                    IsPrivate = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.SupportAttachments.Add(attachment);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to upload support attachment {FileName}", file.FileName);
            }
        }
        await _context.SaveChangesAsync();
    }

    private static SupportTicketSummaryDto MapToSummaryDto(SupportTicket t)
    {
        return new SupportTicketSummaryDto
        {
            TicketId = t.TicketId,
            TicketReference = t.TicketReference,
            TicketType = t.TicketType.ToString(),
            Source = t.Source.ToString(),
            Category = t.Category.ToString(),
            Priority = t.Priority.ToString(),
            Status = t.Status.ToString(),
            Subject = t.Subject,
            CustomerUserId = t.CustomerUserId,
            CustomerName = t.CustomerUser != null ? $"{t.CustomerUser.FirstName} {t.CustomerUser.LastName}".Trim() : "Customer",
            CustomerEmail = t.CustomerUser?.Email ?? string.Empty,
            AssignedAdminName = t.AssignedAdminUser != null ? $"{t.AssignedAdminUser.FirstName} {t.AssignedAdminUser.LastName}".Trim() : null,
            AssignedTeam = t.AssignedTeam,
            BookingId = t.BookingId,
            OperationalIncidentId = t.OperationalIncidentId,
            DisputeInvestigationId = t.DisputeInvestigationId,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            FirstResponseDueAt = t.FirstResponseDueAt,
            ResolutionDueAt = t.ResolutionDueAt,
            MessageCount = t.Messages.Count
        };
    }

    private static SupportTicketPriority ParsePriority(string? p) => p?.ToLowerInvariant() switch
    {
        "p0" or "urgent" => SupportTicketPriority.P0,
        "p1" or "high" => SupportTicketPriority.P1,
        "p3" or "low" => SupportTicketPriority.P3,
        _ => SupportTicketPriority.P2
    };

    private static SupportCategory ParseCategory(string? c) => c?.ToLowerInvariant() switch
    {
        "parkingaccess" or "parking_access" or "access" => SupportCategory.ParkingAccess,
        "booking" => SupportCategory.Booking,
        "payment" or "refund" => SupportCategory.Payment,
        "account" => SupportCategory.Account,
        "ownersupport" or "owner" => SupportCategory.OwnerSupport,
        _ => SupportCategory.General
    };

    private static string DetermineDefaultTeam(SupportCategory category) => category switch
    {
        SupportCategory.ParkingAccess => "ParkingOperations",
        SupportCategory.Booking => "CustomerSupport",
        SupportCategory.Payment => "Payments",
        SupportCategory.Account => "CustomerSupport",
        SupportCategory.OwnerSupport => "OwnerSupport",
        _ => "CustomerSupport"
    };

    private static (double FirstResponseHours, double ResolutionHours) CalculateSlaHours(SupportTicketPriority priority) => priority switch
    {
        SupportTicketPriority.P0 => (0.25, 1.0),
        SupportTicketPriority.P1 => (0.5, 4.0),
        SupportTicketPriority.P2 => (2.0, 24.0),
        _ => (4.0, 48.0)
    };
}
