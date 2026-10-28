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
    private readonly AccessLogService _accessLogService;
    private readonly ISupportRealtimeNotifier _realtimeNotifier;
    private readonly CloudinaryService _cloudinaryService;
    private readonly ILogger<SupportTicketService> _logger;

    public SupportTicketService(
        ApplicationDbContext context,
        SupportAuditService auditService,
        AccessLogService accessLogService,
        ISupportRealtimeNotifier realtimeNotifier,
        CloudinaryService cloudinaryService,
        ILogger<SupportTicketService> logger)
    {
        _context = context;
        _auditService = auditService;
        _accessLogService = accessLogService;
        _realtimeNotifier = realtimeNotifier;
        _cloudinaryService = cloudinaryService;
        _logger = logger;
    }

    public async Task<SelfUnlockGateResultDto> SelfUnlockGateAsync(int userId, SelfUnlockGateRequestDto request)
    {
        var now = DateTime.UtcNow;
        var bookingsQuery = _context.Bookings
            .Include(b => b.ParkingSpot)
                .ThenInclude(p => p.IoTDevice)
            .Where(b => b.RenterId == userId);

        var booking = request.BookingId.HasValue
            ? await bookingsQuery.FirstOrDefaultAsync(b => b.BookingId == request.BookingId.Value)
            : await bookingsQuery
                .OrderByDescending(b => b.StartDate)
                .FirstOrDefaultAsync(b => b.BookingStatus == BookingStatus.Confirmed
                    || b.BookingStatus == BookingStatus.Active
                    || (b.StartDate <= now.AddHours(2) && b.EndDate >= now.AddHours(-2)));

        if (booking == null)
        {
            throw new ArgumentException("No eligible active booking found. Please select a booking or submit an urgent assistance request.");
        }

        var spotName = booking.ParkingSpot?.ParkingLabel ?? $"Spot #{booking.ParkingSpotId}";
        var iot = booking.ParkingSpot?.IoTDevice;
        var isEligibleStatus = booking.BookingStatus == BookingStatus.Confirmed || booking.BookingStatus == BookingStatus.Active;
        var isWithinWindow = booking.StartDate <= now.AddHours(1) && booking.EndDate >= now.AddHours(-2);

        if (!isEligibleStatus || !isWithinWindow)
        {
            return new SelfUnlockGateResultDto
            {
                Success = false,
                Message = $"Booking #{booking.BookingReference} is not currently within the valid access window. Please submit a support ticket for assistance.",
                BookingId = booking.BookingId,
                BookingReference = booking.BookingReference,
                ParkingSpotName = spotName,
                IoTDeviceId = iot?.IoTDeviceId,
                IoTDeviceStatus = iot?.DeviceStatus.ToString(),
                SuggestUrgentTicket = true,
                ExecutedAt = now
            };
        }

        if (iot == null || iot.DeviceStatus != DeviceStatus.Online)
        {
            return new SelfUnlockGateResultDto
            {
                Success = false,
                Message = "The parking barrier device is currently offline and remote self-unlock is unavailable. Please click below to contact 24/7 on-call support.",
                BookingId = booking.BookingId,
                BookingReference = booking.BookingReference,
                ParkingSpotName = spotName,
                IoTDeviceId = iot?.IoTDeviceId,
                IoTDeviceStatus = iot?.DeviceStatus.ToString() ?? "NoDevice",
                SuggestUrgentTicket = true,
                ExecutedAt = now
            };
        }

        // Rate limit: max 1 self-unlock per 2 minutes per booking
        var recentSelfUnlock = await _context.AccessLogs
            .AsNoTracking()
            .AnyAsync(a => a.UserId == userId
                && a.BookingId == booking.BookingId
                && a.AccessedAt >= now.AddMinutes(-2)
                && a.Actions.Contains("Self-Service Gate Unlock"));

        if (recentSelfUnlock)
        {
            return new SelfUnlockGateResultDto
            {
                Success = false,
                Message = "A gate unlock command was recently dispatched. If the barrier has not opened, please submit an urgent assistance request for immediate intervention.",
                BookingId = booking.BookingId,
                BookingReference = booking.BookingReference,
                ParkingSpotName = spotName,
                IoTDeviceId = iot.IoTDeviceId,
                IoTDeviceStatus = iot.DeviceStatus.ToString(),
                SuggestUrgentTicket = true,
                ExecutedAt = now
            };
        }

        var actionType = string.Equals(request.Action, "Exit", StringComparison.OrdinalIgnoreCase) ? "Exit" : "Enter";
        var commandId = $"CMD-SELF-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

        if (actionType == "Enter" && !booking.CheckedInAt.HasValue)
        {
            booking.CheckedInAt = now;
            booking.BookingStatus = BookingStatus.Active;
            booking.UpdatedAt = now;
        }
        else if (actionType == "Exit")
        {
            booking.ActualExitAt = now;
            booking.UpdatedAt = now;
        }

        await _context.SaveChangesAsync();

        await _accessLogService.LogAsync(
            userId,
            $"Self-Service Gate Unlock ({actionType} - {commandId})",
            true,
            $"Customer triggered emergency self-unlock for Booking #{booking.BookingReference} at {spotName}",
            booking.BookingId,
            iot.IoTDeviceId);

        await _auditService.LogAsync(
            "GateUnlock",
            booking.BookingId,
            booking.BookingReference,
            "SelfUnlockExecuted",
            userId,
            "Customer",
            null,
            actionType,
            $"Self-service gate unlock ({actionType}) dispatched to IoT device {iot.Esp32Serial}");

        return new SelfUnlockGateResultDto
        {
            Success = true,
            CommandId = commandId,
            Message = $"Remote gate unlock command ({commandId}) sent to spot {spotName}. Please proceed safely. If the barrier does not open, request urgent assistance.",
            BookingId = booking.BookingId,
            BookingReference = booking.BookingReference,
            ParkingSpotName = spotName,
            IoTDeviceId = iot.IoTDeviceId,
            IoTDeviceStatus = iot.DeviceStatus.ToString(),
            SuggestUrgentTicket = false,
            ExecutedAt = now
        };
    }

    public async Task<List<SupportTicketSummaryDto>> GetMyTicketsAsync(int userId, string? status = null, string? search = null)
    {
        var query = _context.SupportTickets
            .AsNoTracking()
            .Where(t => t.CustomerUserId == userId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var trimmedStatus = status.Trim();
            if (string.Equals(trimmedStatus, "Processing", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => t.Status == SupportTicketStatus.New
                    || t.Status == SupportTicketStatus.Assigned
                    || t.Status == SupportTicketStatus.InProgress
                    || t.Status == SupportTicketStatus.WaitingForInternalTeam
                    || t.Status == SupportTicketStatus.Reopened);
            }
            else if (string.Equals(trimmedStatus, "ActionRequired", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => t.Status == SupportTicketStatus.WaitingForCustomer);
            }
            else if (Enum.TryParse<SupportTicketStatus>(trimmedStatus, true, out var parsedStatus))
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
            .OrderByDescending(t => t.UpdatedAt)
            .Include(t => t.CustomerUser)
            .Include(t => t.AssignedAdminUser)
            .Include(t => t.Booking)
            .Include(t => t.ParkingSpot)
            .Include(t => t.DisputeInvestigation)
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
            .Include(t => t.Booking)
            .Include(t => t.ParkingSpot)
            .Include(t => t.DisputeInvestigation)
            .Include(t => t.Messages)
            .ToListAsync();

        return (tickets.Select(MapToSummaryDto).ToList(), total);
    }

    public async Task<SupportTicketDto?> GetTicketDetailAsync(int ticketId, int callerUserId, bool isAdmin)
    {
        var ticket = await _context.SupportTickets
            .Include(t => t.CustomerUser)
            .Include(t => t.AssignedAdminUser)
            .Include(t => t.Booking)
                .ThenInclude(b => b!.ParkingSpot)
                    .ThenInclude(p => p.IoTDevice)
            .Include(t => t.ParkingSpot)
                .ThenInclude(p => p!.IoTDevice)
            .Include(t => t.Vehicle)
            .Include(t => t.OperationalIncident)
            .Include(t => t.DisputeInvestigation)
            .Include(t => t.Messages)
                .ThenInclude(m => m.SenderUser)
            .Include(t => t.Messages)
                .ThenInclude(m => m.Attachments)
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.TicketId == ticketId);

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

        var (displayStatus, statusLabel) = MapUserDisplayStatus(ticket.Status);
        var now = DateTime.UtcNow;
        var canSelfUnlock = ticket.Booking != null && SupportContextService.CanBookingSelfUnlock(ticket.Booking, now);

        return new SupportTicketDto
        {
            TicketId = ticket.TicketId,
            TicketReference = ticket.TicketReference,
            IssueCode = ticket.IssueCode,
            TicketType = ticket.TicketType.ToString(),
            Source = ticket.Source.ToString(),
            Category = ticket.Category.ToString(),
            Priority = ticket.Priority.ToString(),
            Status = ticket.Status.ToString(),
            UserDisplayStatus = displayStatus,
            UserStatusLabel = statusLabel,
            Subject = ticket.Subject,
            Description = ticket.Description,
            CustomerUserId = ticket.CustomerUserId,
            CustomerName = $"{ticket.CustomerUser.FirstName} {ticket.CustomerUser.LastName}".Trim(),
            CustomerEmail = ticket.CustomerUser.Email,
            CustomerRole = ticket.CustomerUser.UserType.ToString(),
            AssignedAdminUserId = ticket.AssignedAdminUserId,
            AssignedAdminName = ticket.AssignedAdminUser != null ? $"{ticket.AssignedAdminUser.FirstName} {ticket.AssignedAdminUser.LastName}".Trim() : null,
            AssignedTeam = ticket.AssignedTeam,
            BookingId = ticket.BookingId,
            BookingReference = ticket.Booking?.BookingReference,
            ParkingSpotId = ticket.ParkingSpotId,
            ParkingSpotName = ticket.ParkingSpot?.ParkingLabel ?? ticket.Booking?.ParkingSpot?.ParkingLabel,
            VehicleId = ticket.VehicleId,
            VehiclePlate = ticket.Vehicle?.NumberPlate,
            CanSelfUnlockGate = canSelfUnlock,
            OperationalIncidentId = ticket.OperationalIncidentId,
            IncidentReference = ticket.OperationalIncident?.IncidentReference,
            DisputeInvestigationId = ticket.DisputeInvestigationId,
            DisputeReference = ticket.DisputeInvestigation?.DisputeReference,
            RefundSummary = MapRefundSummary(ticket.DisputeInvestigation),
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
        var issueCode = NormalizeIssueCode(request.IssueCode);

        // 1. Auto-resolve Booking context (explicit BookingId or auto-detect active booking for parking/booking/refund issues)
        Booking? booking = null;
        if (request.BookingId.HasValue)
        {
            booking = await _context.Bookings
                .Include(b => b.ParkingSpot)
                    .ThenInclude(p => p.IoTDevice)
                .Include(b => b.Vehicle)
                .FirstOrDefaultAsync(b => b.BookingId == request.BookingId.Value
                    && (b.RenterId == userId || b.ParkingSpot.OwnerId == userId));
        }
        else if (ShouldAutoBindActiveBooking(issueCode, request.Category))
        {
            booking = await _context.Bookings
                .Include(b => b.ParkingSpot)
                    .ThenInclude(p => p.IoTDevice)
                .Include(b => b.Vehicle)
                .Where(b => b.RenterId == userId)
                .OrderByDescending(b => b.StartDate)
                .FirstOrDefaultAsync(b => b.BookingStatus == BookingStatus.Confirmed
                    || b.BookingStatus == BookingStatus.Active
                    || (b.StartDate <= now.AddHours(2) && b.EndDate >= now.AddHours(-2)));
        }

        var resolvedSpotId = request.ParkingSpotId ?? booking?.ParkingSpotId;
        var resolvedVehicleId = request.VehicleId ?? booking?.VehicleId;

        ParkingSpot? spot = booking?.ParkingSpot;
        if (spot == null && resolvedSpotId.HasValue)
        {
            spot = await _context.ParkingSpots
                .Include(p => p.IoTDevice)
                .FirstOrDefaultAsync(p => p.ParkingSpotId == resolvedSpotId.Value);
        }

        // 2. Automatically infer Category, Priority, AssignedTeam, Subject, and opening message
        var (category, priority, assignedTeam, defaultTitle) = ResolveIssueMetadata(issueCode, request.Category, spot?.IoTDevice);
        var (firstResponseHours, resolutionHours) = CalculateSlaHours(priority);

        var spotLabel = spot?.ParkingLabel ?? (resolvedSpotId.HasValue ? $"Spot #{resolvedSpotId.Value}" : null);
        var subject = !string.IsNullOrWhiteSpace(request.Subject)
            ? request.Subject.Trim()
            : BuildAutomaticSubject(defaultTitle, spotLabel, booking?.BookingReference);

        var description = !string.IsNullOrWhiteSpace(request.Message)
            ? request.Message.Trim()
            : BuildAutomaticDescription(defaultTitle, spotLabel, booking);

        var ticket = new SupportTicket
        {
            TicketReference = $"TKT-{now:yyyy}-{Random.Shared.Next(10000, 99999)}",
            IssueCode = issueCode,
            TicketType = !string.IsNullOrWhiteSpace(issueCode) ? SupportTicketType.Preset : SupportTicketType.Custom,
            Source = SupportSource.QuickHelp,
            Category = category,
            Priority = priority,
            CustomerUserId = userId,
            CreatedByUserId = userId,
            AssignedTeam = assignedTeam,
            BookingId = booking?.BookingId ?? request.BookingId,
            ParkingSpotId = resolvedSpotId,
            VehicleId = resolvedVehicleId,
            Subject = subject,
            Description = description,
            Status = SupportTicketStatus.New,
            FirstResponseDueAt = now.AddHours(firstResponseHours),
            ResolutionDueAt = now.AddHours(resolutionHours),
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.SupportTickets.Add(ticket);
        await _context.SaveChangesAsync();

        var openingMessage = new SupportTicketMessage
        {
            TicketId = ticket.TicketId,
            SenderUserId = userId,
            SenderRole = "Customer",
            Body = description,
            CreatedAt = now
        };
        _context.SupportTicketMessages.Add(openingMessage);
        await _context.SaveChangesAsync();

        // 3. Silent Background Orchestration: Auto-create OperationalIncident for urgent gate/spot/IoT issues
        if (ShouldAutoCreateIncident(issueCode, category, priority, spot?.IoTDevice))
        {
            var incPriority = priority == SupportTicketPriority.P0 ? IncidentPriority.P0 : IncidentPriority.P1;
            var incident = new OperationalIncident
            {
                IncidentReference = $"INC-{now:yyyy}-{Random.Shared.Next(10000, 99999)}",
                IncidentType = issueCode switch
                {
                    "GATE_STUCK" => "GateFailure",
                    "SPOT_OCCUPIED" => "SpotOccupied",
                    "IOT_OFFLINE" => "DeviceOffline",
                    "RENTER_OVERSTAY" => "OverstayBlocking",
                    _ => "GateFailure"
                },
                Priority = incPriority,
                Status = IncidentStatus.Open,
                Title = subject,
                Description = $"Auto-escalated from Ticket #{ticket.TicketReference} ({user.Email}). {description}",
                PropertyId = spot?.PropertyId,
                ParkingSpotId = resolvedSpotId,
                IoTDeviceId = spot?.IoTDevice?.IoTDeviceId,
                Source = "TicketAutoTriage",
                AssignedTeam = "ParkingOperations",
                AffectedCustomerCount = 1,
                NextEscalationAt = incPriority == IncidentPriority.P0 ? now.AddMinutes(2) : now.AddMinutes(5),
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.OperationalIncidents.Add(incident);
            await _context.SaveChangesAsync();

            ticket.OperationalIncidentId = incident.IncidentId;
            _context.IncidentTickets.Add(new IncidentTicket
            {
                IncidentId = incident.IncidentId,
                TicketId = ticket.TicketId,
                LinkedAt = now
            });
            await _context.SaveChangesAsync();

            await _auditService.LogAsync("Incident", incident.IncidentId, incident.IncidentReference, "Created", userId, "System", null, "Open", $"Auto-created from Ticket #{ticket.TicketReference}");
            await _realtimeNotifier.BroadcastEventAsync("incident.created", new { incidentId = incident.IncidentId, reference = incident.IncidentReference, priority = incident.Priority.ToString() });
        }

        // 4. Silent Background Orchestration: Auto-create DisputeInvestigation for refund/payment/payout claims
        if (ShouldAutoCreateDispute(issueCode, category))
        {
            var disputeType = issueCode switch
            {
                "DUPLICATE_CHARGE" => DisputeType.DuplicateCharge,
                "UNRECOGNIZED_CHARGE" => DisputeType.UnrecognizedCharge,
                "OWNER_PAYOUT" => DisputeType.OwnerPayout,
                _ => DisputeType.Refund
            };

            var claimAmount = request.ClaimAmount ?? booking?.TotalAmount ?? 0m;
            var dispute = new DisputeInvestigation
            {
                DisputeReference = $"DSP-{now:yyyy}-{Random.Shared.Next(10000, 99999)}",
                DisputeType = disputeType,
                Status = DisputeStatus.Opened,
                CustomerUserId = userId,
                TicketId = ticket.TicketId,
                BookingId = ticket.BookingId,
                Amount = claimAmount,
                Currency = "MYR",
                Reason = description,
                AssignedTeam = "Payments",
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.DisputeInvestigations.Add(dispute);
            await _context.SaveChangesAsync();

            ticket.DisputeInvestigationId = dispute.DisputeId;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync("Dispute", dispute.DisputeId, dispute.DisputeReference, "Created", userId, "Customer", null, "Opened", $"Auto-created from Ticket #{ticket.TicketReference}");
            await _realtimeNotifier.BroadcastEventAsync("dispute.updated", new { disputeId = dispute.DisputeId, reference = dispute.DisputeReference });
        }

        // 5. Save attachments (and auto-sync to DisputeEvidence if linked to a Dispute)
        if (request.Attachments != null && request.Attachments.Count > 0)
        {
            await SaveAttachmentsAsync(request.Attachments, userId, "Customer", ticket.TicketId, openingMessage.MessageId, ticket.DisputeInvestigationId);
        }

        await _auditService.LogAsync("Ticket", ticket.TicketId, ticket.TicketReference, "Created", userId, "Customer", null, "New", $"Ticket created ({issueCode ?? "CUSTOM"})");
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
            IssueCode = NormalizeIssueCode(request.IssueCode),
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
                await SaveAttachmentsAsync(request.Attachments, adminUserId, "Admin", ticket.TicketId, msg.MessageId, ticket.DisputeInvestigationId);
            }
        }

        await _auditService.LogAsync("Ticket", ticket.TicketId, ticket.TicketReference, "Created", adminUserId, "Admin", null, ticket.Status.ToString(), "Created by Admin");
        await _realtimeNotifier.BroadcastEventAsync("ticket.created", new { ticketId = ticket.TicketId, reference = ticket.TicketReference }, targetCustomerId, ticket.TicketId);

        return (await GetTicketDetailAsync(ticket.TicketId, adminUserId, true))!;
    }

    public async Task<SupportTicketMessageDto> AddMessageAsync(int ticketId, int senderUserId, string role, SendTicketMessageRequestDto request)
    {
        var ticket = await _context.SupportTickets
            .Include(t => t.DisputeInvestigation)
            .FirstOrDefaultAsync(t => t.TicketId == ticketId);
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
            if (ticket.DisputeInvestigation != null && ticket.DisputeInvestigation.Status == DisputeStatus.MoreInfo)
            {
                ticket.DisputeInvestigation.Status = DisputeStatus.EvidenceReview;
                ticket.DisputeInvestigation.UpdatedAt = now;
            }
        }

        ticket.UpdatedAt = now;
        await _context.SaveChangesAsync();

        if (request.Attachments != null && request.Attachments.Count > 0)
        {
            await SaveAttachmentsAsync(request.Attachments, senderUserId, role, ticketId, message.MessageId, ticket.DisputeInvestigationId);
        }

        await _auditService.LogAsync("Ticket", ticketId, ticket.TicketReference, "MessageAdded", senderUserId, role, null, ticket.Status.ToString(), $"Message added by {role}");
        await _realtimeNotifier.BroadcastEventAsync("message.created", new { ticketId = ticketId, messageId = message.MessageId, isInternal = message.IsInternal }, ticket.CustomerUserId, ticketId);

        var sender = await _context.Users.FindAsync(senderUserId);
        var savedAttachments = await _context.SupportAttachments
            .AsNoTracking()
            .Where(a => a.TicketMessageId == message.MessageId)
            .Select(a => new SupportAttachmentDto
            {
                AttachmentId = a.AttachmentId,
                FileName = a.FileName,
                FileUrl = a.FileUrl,
                ContentType = a.ContentType,
                FileSize = a.FileSize,
                IsPrivate = a.IsPrivate,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return new SupportTicketMessageDto
        {
            MessageId = message.MessageId,
            TicketId = message.TicketId,
            SenderUserId = senderUserId,
            SenderName = sender != null ? $"{sender.FirstName} {sender.LastName}".Trim() : role,
            SenderRole = role,
            Body = message.Body,
            IsInternal = message.IsInternal,
            CreatedAt = message.CreatedAt,
            Attachments = savedAttachments
        };
    }

    public async Task<SupportTicketDto> CloseCustomerTicketAsync(int ticketId, int customerUserId, CloseTicketRequestDto? request)
    {
        var ticket = await _context.SupportTickets
            .Include(t => t.OperationalIncident)
            .FirstOrDefaultAsync(t => t.TicketId == ticketId);

        if (ticket == null || ticket.CustomerUserId != customerUserId)
        {
            throw new KeyNotFoundException("Ticket not found");
        }

        var oldState = ticket.Status.ToString();
        var now = DateTime.UtcNow;
        var reason = string.IsNullOrWhiteSpace(request?.Reason)
            ? "Issue resolved, closed by customer"
            : request.Reason.Trim();

        ticket.Status = SupportTicketStatus.Closed;
        ticket.ResolvedAt ??= now;
        ticket.ClosedAt = now;
        ticket.ResolutionCode = "ClosedByCustomer";
        ticket.UpdatedAt = now;

        _context.SupportTicketMessages.Add(new SupportTicketMessage
        {
            TicketId = ticketId,
            SenderUserId = customerUserId,
            SenderRole = "Customer",
            Body = $"[Ticket Closed by Customer] {reason}",
            CreatedAt = now
        });

        // Auto-resolve single-customer OperationalIncident if still open
        if (ticket.OperationalIncident != null
            && ticket.OperationalIncident.AffectedCustomerCount <= 1
            && (ticket.OperationalIncident.Status == IncidentStatus.Open || ticket.OperationalIncident.Status == IncidentStatus.Acknowledged || ticket.OperationalIncident.Status == IncidentStatus.Escalated))
        {
            ticket.OperationalIncident.Status = IncidentStatus.Resolved;
            ticket.OperationalIncident.ResolvedAt = now;
            ticket.OperationalIncident.NextEscalationAt = null;
            ticket.OperationalIncident.UpdatedAt = now;
        }

        await _context.SaveChangesAsync();
        await _auditService.LogAsync("Ticket", ticketId, ticket.TicketReference, "ClosedByCustomer", customerUserId, "Customer", oldState, "Closed", reason);
        await _realtimeNotifier.BroadcastEventAsync("ticket.updated", new { ticketId = ticketId, status = "Closed" }, ticket.CustomerUserId, ticketId);

        return (await GetTicketDetailAsync(ticketId, customerUserId, false))!;
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

    private async Task SaveAttachmentsAsync(List<IFormFile> files, int userId, string role, int ticketId, int? messageId = null, int? disputeId = null)
    {
        var now = DateTime.UtcNow;
        foreach (var file in files)
        {
            try
            {
                var uploadResult = await _cloudinaryService.UploadPrivateDocumentAsync(file, "support_attachments");
                var fileUrl = uploadResult.SecureUrl?.ToString() ?? uploadResult.Url?.ToString() ?? string.Empty;

                var attachment = new SupportAttachment
                {
                    TicketId = ticketId,
                    TicketMessageId = messageId,
                    FileName = file.FileName,
                    FileUrl = fileUrl,
                    ContentType = file.ContentType,
                    FileSize = file.Length,
                    UploadedByUserId = userId,
                    IsPrivate = true,
                    CreatedAt = now
                };
                _context.SupportAttachments.Add(attachment);

                // Automatically mirror attachment as DisputeEvidence when the ticket has an active DisputeInvestigation
                if (disputeId.HasValue)
                {
                    _context.DisputeEvidences.Add(new DisputeEvidence
                    {
                        DisputeId = disputeId.Value,
                        EvidenceType = "TicketAttachment",
                        FileName = file.FileName,
                        FileUrl = fileUrl,
                        UploadedByUserId = userId,
                        UploadedRole = role,
                        IsVerified = role == "Admin",
                        Description = "Uploaded via Support Ticket conversation",
                        CreatedAt = now
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to upload support attachment {FileName}", file.FileName);
            }
        }
        await _context.SaveChangesAsync();
    }

    public static SupportTicketSummaryDto MapToSummaryDto(SupportTicket t)
    {
        var (displayStatus, statusLabel) = MapUserDisplayStatus(t.Status);
        return new SupportTicketSummaryDto
        {
            TicketId = t.TicketId,
            TicketReference = t.TicketReference,
            IssueCode = t.IssueCode,
            TicketType = t.TicketType.ToString(),
            Source = t.Source.ToString(),
            Category = t.Category.ToString(),
            Priority = t.Priority.ToString(),
            Status = t.Status.ToString(),
            UserDisplayStatus = displayStatus,
            UserStatusLabel = statusLabel,
            Subject = t.Subject,
            CustomerUserId = t.CustomerUserId,
            CustomerName = t.CustomerUser != null ? $"{t.CustomerUser.FirstName} {t.CustomerUser.LastName}".Trim() : "Customer",
            CustomerEmail = t.CustomerUser?.Email ?? string.Empty,
            AssignedAdminName = t.AssignedAdminUser != null ? $"{t.AssignedAdminUser.FirstName} {t.AssignedAdminUser.LastName}".Trim() : null,
            AssignedTeam = t.AssignedTeam,
            BookingId = t.BookingId,
            BookingReference = t.Booking?.BookingReference,
            ParkingSpotName = t.ParkingSpot?.ParkingLabel,
            OperationalIncidentId = t.OperationalIncidentId,
            DisputeInvestigationId = t.DisputeInvestigationId,
            RefundSummary = MapRefundSummary(t.DisputeInvestigation),
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            FirstResponseDueAt = t.FirstResponseDueAt,
            ResolutionDueAt = t.ResolutionDueAt,
            MessageCount = t.Messages.Count
        };
    }

    public static (string DisplayStatus, string StatusLabel) MapUserDisplayStatus(SupportTicketStatus status) => status switch
    {
        SupportTicketStatus.WaitingForCustomer => ("ActionRequired", "Action Required"),
        SupportTicketStatus.Resolved => ("Resolved", "Resolved"),
        SupportTicketStatus.Closed or SupportTicketStatus.Cancelled or SupportTicketStatus.Duplicate => ("Closed", "Closed"),
        _ => ("Processing", "In Progress")
    };

    private static TicketRefundSummaryDto? MapRefundSummary(DisputeInvestigation? d)
    {
        if (d == null) return null;
        return new TicketRefundSummaryDto
        {
            DisputeId = d.DisputeId,
            DisputeReference = d.DisputeReference,
            DisputeType = d.DisputeType.ToString(),
            Status = d.Status.ToString(),
            ClaimAmount = d.Amount,
            Currency = d.Currency,
            Decision = d.Decision,
            DecisionReason = d.DecisionReason,
            DecidedAt = d.DecidedAt
        };
    }

    private static string? NormalizeIssueCode(string? issueCode)
    {
        return string.IsNullOrWhiteSpace(issueCode) ? null : issueCode.Trim().ToUpperInvariant();
    }

    private static bool ShouldAutoBindActiveBooking(string? issueCode, string? category)
    {
        if (issueCode is "GATE_STUCK" or "SPOT_OCCUPIED" or "REFUND_REQUEST" or "RENTER_OVERSTAY")
            return true;

        var parsedCat = ParseCategory(category);
        return parsedCat is SupportCategory.ParkingAccess or SupportCategory.Booking;
    }

    private static bool ShouldAutoCreateIncident(string? issueCode, SupportCategory category, SupportTicketPriority priority, IoTDevice? iot)
    {
        if (issueCode is "GATE_STUCK" or "SPOT_OCCUPIED" or "IOT_OFFLINE" or "RENTER_OVERSTAY")
            return true;

        if (category == SupportCategory.ParkingAccess && (priority == SupportTicketPriority.P0 || (iot != null && iot.DeviceStatus == DeviceStatus.Offline)))
            return true;

        return false;
    }

    private static bool ShouldAutoCreateDispute(string? issueCode, SupportCategory category)
    {
        if (issueCode is "REFUND_REQUEST" or "DUPLICATE_CHARGE" or "UNRECOGNIZED_CHARGE" or "OWNER_PAYOUT")
            return true;

        return category == SupportCategory.Payment;
    }

    private static (SupportCategory Category, SupportTicketPriority Priority, string AssignedTeam, string DefaultTitle) ResolveIssueMetadata(
        string? issueCode,
        string? rawCategory,
        IoTDevice? iot)
    {
        return issueCode switch
        {
            "GATE_STUCK" => (SupportCategory.ParkingAccess, SupportTicketPriority.P0, "ParkingOperations", "Barrier Gate Stuck / Entry Denied"),
            "SPOT_OCCUPIED" => (SupportCategory.ParkingAccess, SupportTicketPriority.P0, "ParkingOperations", "Reserved Spot Occupied"),
            "RENTER_OVERSTAY" => (SupportCategory.OwnerSupport, SupportTicketPriority.P1, "ParkingOperations", "Renter Overstay / Blocking Spot"),
            "IOT_OFFLINE" => (SupportCategory.ParkingAccess, SupportTicketPriority.P1, "ParkingOperations", "IoT Barrier / Hardware Fault"),
            "DUPLICATE_CHARGE" => (SupportCategory.Payment, SupportTicketPriority.P1, "Payments", "Duplicate Charge / Billing Issue"),
            "UNRECOGNIZED_CHARGE" => (SupportCategory.Payment, SupportTicketPriority.P1, "Payments", "Unrecognized Charge Dispute"),
            "REFUND_REQUEST" => (SupportCategory.Payment, SupportTicketPriority.P2, "Payments", "Refund Request"),
            "OWNER_PAYOUT" => (SupportCategory.OwnerSupport, SupportTicketPriority.P2, "Payments", "Host Payout & Revenue Inquiry"),
            _ => ResolveFromFallbackCategory(rawCategory, iot)
        };
    }

    private static (SupportCategory Category, SupportTicketPriority Priority, string AssignedTeam, string DefaultTitle) ResolveFromFallbackCategory(
        string? rawCategory,
        IoTDevice? iot)
    {
        var category = ParseCategory(rawCategory);
        var priority = category switch
        {
            SupportCategory.ParkingAccess => iot?.DeviceStatus == DeviceStatus.Offline ? SupportTicketPriority.P0 : SupportTicketPriority.P1,
            SupportCategory.Payment => SupportTicketPriority.P2,
            _ => SupportTicketPriority.P2
        };
        var team = DetermineDefaultTeam(category);
        return (category, priority, team, "Support Request");
    }

    private static string BuildAutomaticSubject(string defaultTitle, string? spotLabel, string? bookingReference)
    {
        var parts = new List<string> { defaultTitle };
        if (!string.IsNullOrWhiteSpace(spotLabel)) parts.Add(spotLabel);
        if (!string.IsNullOrWhiteSpace(bookingReference)) parts.Add($"({bookingReference})");
        return string.Join(" - ", parts);
    }

    private static string BuildAutomaticDescription(string defaultTitle, string? spotLabel, Booking? booking)
    {
        if (booking != null)
        {
            return $"{defaultTitle}. Linked booking: #{booking.BookingReference}, spot: {spotLabel ?? "Unknown"}, plate: {booking.Vehicle?.NumberPlate ?? "Unknown"}.";
        }
        return $"{defaultTitle}.";
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
