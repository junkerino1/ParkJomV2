using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;

namespace ParkJomV2.Services.Support;

public class ConversationService
{
    private readonly ApplicationDbContext _context;
    private readonly SupportContextService _contextService;
    private readonly SupportTicketService _ticketService;
    private readonly SupportAuditService _auditService;
    private readonly ISupportRealtimeNotifier _realtimeNotifier;
    private readonly CloudinaryService _cloudinaryService;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(
        ApplicationDbContext context,
        SupportContextService contextService,
        SupportTicketService ticketService,
        SupportAuditService auditService,
        ISupportRealtimeNotifier realtimeNotifier,
        CloudinaryService cloudinaryService,
        ILogger<ConversationService> logger)
    {
        _context = context;
        _contextService = contextService;
        _ticketService = ticketService;
        _auditService = auditService;
        _realtimeNotifier = realtimeNotifier;
        _cloudinaryService = cloudinaryService;
        _logger = logger;
    }

    public async Task<ConversationDto> CreateConversationAsync(int userId, CreateConversationRequestDto request)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) throw new KeyNotFoundException("User not found");

        var now = DateTime.UtcNow;
        var snapshot = await _contextService.GetUserContextAsync(userId, request.BookingId);

        var conversation = new SupportConversation
        {
            ConversationReference = $"CON-{now:yyyy}-{Random.Shared.Next(10000, 99999)}",
            CustomerUserId = userId,
            Channel = string.IsNullOrWhiteSpace(request.Channel) ? "LiveChat" : request.Channel.Trim(),
            Status = ConversationStatus.Active,
            CurrentBookingId = request.BookingId ?? snapshot.ActiveBooking?.BookingId,
            CurrentParkingSpotId = request.ParkingSpotId,
            ContextSnapshotJson = JsonSerializer.Serialize(snapshot),
            StartedAt = now,
            LastMessageAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.SupportConversations.Add(conversation);
        await _context.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(request.InitialMessage))
        {
            var msg = new SupportConversationMessage
            {
                ConversationId = conversation.ConversationId,
                SenderUserId = userId,
                SenderRole = "Customer",
                MessageType = "Customer",
                Body = request.InitialMessage.Trim(),
                CreatedAt = now
            };
            _context.SupportConversationMessages.Add(msg);
            await _context.SaveChangesAsync();
        }

        await _auditService.LogAsync("Conversation", conversation.ConversationId, conversation.ConversationReference, "Created", userId, "Customer", null, "Active", "Conversation started");
        await _realtimeNotifier.BroadcastEventAsync("conversation.created", new { conversationId = conversation.ConversationId, reference = conversation.ConversationReference }, userId, conversationId: conversation.ConversationId);

        return (await GetConversationDetailAsync(conversation.ConversationId, userId, false))!;
    }

    public async Task<List<ConversationDto>> GetMyConversationsAsync(int userId, string? status = null)
    {
        var query = _context.SupportConversations
            .AsNoTracking()
            .Where(c => c.CustomerUserId == userId);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ConversationStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(c => c.Status == parsedStatus);
        }

        var conversations = await query
            .OrderByDescending(c => c.UpdatedAt)
            .Include(c => c.CustomerUser)
            .Include(c => c.AssignedAdminUser)
            .Include(c => c.Messages)
            .ToListAsync();

        return conversations.Select(MapToDto).ToList();
    }

    public async Task<(List<ConversationDto> Items, int TotalCount)> GetAdminConversationsAsync(
        string? status = null,
        string? search = null,
        int page = 1,
        int pageSize = 25)
    {
        var query = _context.SupportConversations
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ConversationStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(c => c.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(c => c.ConversationReference.ToLower().Contains(term)
                || c.CustomerUser.Email.ToLower().Contains(term)
                || c.CustomerUser.FirstName!.ToLower().Contains(term));
        }

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(c => c.CustomerUser)
            .Include(c => c.AssignedAdminUser)
            .Include(c => c.Messages)
            .ToListAsync();

        return (items.Select(MapToDto).ToList(), total);
    }

    public async Task<ConversationDetailDto?> GetConversationDetailAsync(int conversationId, int callerUserId, bool isAdmin)
    {
        var conversation = await _context.SupportConversations
            .Include(c => c.CustomerUser)
            .Include(c => c.AssignedAdminUser)
            .Include(c => c.ConvertedTickets)
            .Include(c => c.Messages)
                .ThenInclude(m => m.SenderUser)
            .Include(c => c.Messages)
                .ThenInclude(m => m.Attachments)
            .FirstOrDefaultAsync(c => c.ConversationId == conversationId);

        if (conversation == null) return null;
        if (!isAdmin && conversation.CustomerUserId != callerUserId) return null;

        var visibleMessages = conversation.Messages
            .Where(m => isAdmin || !m.IsInternal)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ConversationMessageDto
            {
                MessageId = m.MessageId,
                ConversationId = m.ConversationId,
                SenderUserId = m.SenderUserId,
                SenderName = m.SenderUser != null ? $"{m.SenderUser.FirstName} {m.SenderUser.LastName}".Trim() : m.SenderRole,
                SenderRole = m.SenderRole,
                MessageType = m.MessageType,
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

        var dto = new ConversationDetailDto
        {
            ConversationId = conversation.ConversationId,
            ConversationReference = conversation.ConversationReference,
            CustomerUserId = conversation.CustomerUserId,
            CustomerName = $"{conversation.CustomerUser.FirstName} {conversation.CustomerUser.LastName}".Trim(),
            CustomerEmail = conversation.CustomerUser.Email,
            Channel = conversation.Channel,
            Status = conversation.Status.ToString(),
            AssignedAdminUserId = conversation.AssignedAdminUserId,
            AssignedAdminName = conversation.AssignedAdminUser != null ? $"{conversation.AssignedAdminUser.FirstName} {conversation.AssignedAdminUser.LastName}".Trim() : null,
            CurrentBookingId = conversation.CurrentBookingId,
            StartedAt = conversation.StartedAt,
            LastMessageAt = conversation.LastMessageAt,
            ClosedAt = conversation.ClosedAt,
            ClosingReason = conversation.ClosingReason,
            ContextSnapshotJson = isAdmin ? conversation.ContextSnapshotJson : null,
            MessageCount = conversation.Messages.Count,
            Messages = visibleMessages,
            ConvertedTickets = conversation.ConvertedTickets.Select(t => new SupportTicketSummaryDto
            {
                TicketId = t.TicketId,
                TicketReference = t.TicketReference,
                Subject = t.Subject,
                Status = t.Status.ToString(),
                Priority = t.Priority.ToString(),
                Category = t.Category.ToString(),
                CreatedAt = t.CreatedAt
            }).ToList()
        };

        return dto;
    }

    public async Task<ConversationMessageDto> AddMessageAsync(int conversationId, int senderUserId, string role, SendConversationMessageRequestDto request)
    {
        var conversation = await _context.SupportConversations.FindAsync(conversationId);
        if (conversation == null) throw new KeyNotFoundException("Conversation not found");

        var now = DateTime.UtcNow;
        var message = new SupportConversationMessage
        {
            ConversationId = conversationId,
            SenderUserId = senderUserId,
            SenderRole = role,
            MessageType = role,
            Body = request.Message,
            IsInternal = request.IsInternal,
            CreatedAt = now
        };

        _context.SupportConversationMessages.Add(message);

        conversation.LastMessageAt = now;
        conversation.UpdatedAt = now;

        if (role == "Admin" && conversation.AssignedAdminUserId == null)
        {
            conversation.AssignedAdminUserId = senderUserId;
        }

        if (role == "Admin" && !request.IsInternal)
        {
            conversation.Status = ConversationStatus.WaitingCustomer;
        }
        else if (role != "Admin")
        {
            conversation.Status = ConversationStatus.WaitingAdmin;
        }

        await _context.SaveChangesAsync();

        if (request.Attachments != null && request.Attachments.Count > 0)
        {
            foreach (var file in request.Attachments)
            {
                try
                {
                    var uploadResult = await _cloudinaryService.UploadPrivateDocumentAsync(file, "conversation_attachments");
                    var attachment = new SupportAttachment
                    {
                        ConversationId = conversationId,
                        ConversationMessageId = message.MessageId,
                        FileName = file.FileName,
                        FileUrl = uploadResult.SecureUrl?.ToString() ?? uploadResult.Url?.ToString() ?? string.Empty,
                        ContentType = file.ContentType,
                        FileSize = file.Length,
                        UploadedByUserId = senderUserId,
                        IsPrivate = true,
                        CreatedAt = now
                    };
                    _context.SupportAttachments.Add(attachment);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to upload conversation attachment");
                }
            }
            await _context.SaveChangesAsync();
        }

        await _auditService.LogAsync("Conversation", conversationId, conversation.ConversationReference, "MessageAdded", senderUserId, role, null, conversation.Status.ToString(), $"Message by {role}");
        await _realtimeNotifier.BroadcastEventAsync("message.created", new { conversationId = conversationId, messageId = message.MessageId, isInternal = message.IsInternal }, conversation.CustomerUserId, conversationId: conversationId);

        var sender = await _context.Users.FindAsync(senderUserId);
        return new ConversationMessageDto
        {
            MessageId = message.MessageId,
            ConversationId = message.ConversationId,
            SenderUserId = senderUserId,
            SenderName = sender != null ? $"{sender.FirstName} {sender.LastName}".Trim() : role,
            SenderRole = role,
            MessageType = role,
            Body = message.Body,
            IsInternal = message.IsInternal,
            CreatedAt = message.CreatedAt
        };
    }

    public async Task<ConversationDto> CloseConversationAsync(int conversationId, int actorUserId, string role, CloseConversationRequestDto request)
    {
        var conversation = await _context.SupportConversations.FindAsync(conversationId);
        if (conversation == null) throw new KeyNotFoundException("Conversation not found");

        var oldState = conversation.Status.ToString();
        var now = DateTime.UtcNow;

        conversation.Status = ConversationStatus.Closed;
        conversation.ClosedAt = now;
        conversation.ClosingReason = request.Reason ?? $"Closed by {role}";
        conversation.UpdatedAt = now;

        await _context.SaveChangesAsync();
        await _auditService.LogAsync("Conversation", conversationId, conversation.ConversationReference, "Closed", actorUserId, role, oldState, "Closed", conversation.ClosingReason);
        await _realtimeNotifier.BroadcastEventAsync("conversation.updated", new { conversationId = conversationId, status = "Closed" }, conversation.CustomerUserId, conversationId: conversationId);

        return (await GetConversationDetailAsync(conversationId, actorUserId, role == "Admin"))!;
    }

    public async Task<SupportTicketDto> ConvertToTicketAsync(int conversationId, int actorUserId, string actorRole, ConvertConversationToTicketRequestDto request)
    {
        var conversation = await _context.SupportConversations
            .Include(c => c.CustomerUser)
            .Include(c => c.Messages)
                .ThenInclude(m => m.SenderUser)
            .FirstOrDefaultAsync(c => c.ConversationId == conversationId);

        if (conversation == null) throw new KeyNotFoundException("Conversation not found");

        var now = DateTime.UtcNow;
        var priority = string.Equals(request.Priority, "P0", StringComparison.OrdinalIgnoreCase) ? SupportTicketPriority.P0
            : string.Equals(request.Priority, "P1", StringComparison.OrdinalIgnoreCase) ? SupportTicketPriority.P1
            : SupportTicketPriority.P2;

        var category = Enum.TryParse<SupportCategory>(request.Category, true, out var parsedCat) ? parsedCat : SupportCategory.General;

        var subject = string.IsNullOrWhiteSpace(request.Subject)
            ? $"Custom Case from Conversation #{conversation.ConversationReference}"
            : request.Subject.Trim();

        var ticket = new SupportTicket
        {
            TicketReference = $"TKT-{now:yyyy}-{Random.Shared.Next(10000, 99999)}",
            TicketType = SupportTicketType.Custom,
            Source = SupportSource.LiveChat,
            Category = category,
            Priority = priority,
            CustomerUserId = conversation.CustomerUserId,
            CreatedByUserId = actorUserId,
            AssignedAdminUserId = actorRole == "Admin" ? actorUserId : conversation.AssignedAdminUserId,
            AssignedTeam = string.IsNullOrWhiteSpace(request.AssignedTeam) ? "CustomerSupport" : request.AssignedTeam,
            ConversationId = conversationId,
            BookingId = conversation.CurrentBookingId,
            ParkingSpotId = conversation.CurrentParkingSpotId,
            Subject = subject,
            Description = subject,
            InternalSummary = request.InternalSummary,
            Status = actorRole == "Admin" ? SupportTicketStatus.Assigned : SupportTicketStatus.New,
            AcceptedAt = actorRole == "Admin" ? now : null,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.SupportTickets.Add(ticket);
        await _context.SaveChangesAsync();

        // Migrate message history to ticket
        foreach (var cm in conversation.Messages.OrderBy(m => m.CreatedAt))
        {
            var tm = new SupportTicketMessage
            {
                TicketId = ticket.TicketId,
                SenderUserId = cm.SenderUserId,
                SenderRole = cm.SenderRole,
                Body = cm.Body,
                IsInternal = cm.IsInternal,
                CreatedAt = cm.CreatedAt
            };
            _context.SupportTicketMessages.Add(tm);
        }

        conversation.Status = ConversationStatus.ConvertedToTicket;
        conversation.UpdatedAt = now;
        await _context.SaveChangesAsync();

        // System message inside conversation alerting user
        var noticeMsg = new SupportConversationMessage
        {
            ConversationId = conversationId,
            SenderRole = "System",
            MessageType = "System",
            Body = $"This conversation continues under Ticket {ticket.TicketReference}. You can check its status in My Support Cases.",
            CreatedAt = now
        };
        _context.SupportConversationMessages.Add(noticeMsg);
        await _context.SaveChangesAsync();

        await _auditService.LogAsync("Conversation", conversationId, conversation.ConversationReference, "ConvertedToTicket", actorUserId, actorRole, null, "ConvertedToTicket", $"Converted to Ticket #{ticket.TicketReference}");
        await _auditService.LogAsync("Ticket", ticket.TicketId, ticket.TicketReference, "Created", actorUserId, actorRole, null, ticket.Status.ToString(), $"Created from LiveChat #{conversation.ConversationReference}");

        await _realtimeNotifier.BroadcastEventAsync("conversation.updated", new { conversationId = conversationId, status = "ConvertedToTicket", ticketReference = ticket.TicketReference }, conversation.CustomerUserId, conversationId: conversationId);
        await _realtimeNotifier.BroadcastEventAsync("ticket.created", new { ticketId = ticket.TicketId, reference = ticket.TicketReference }, conversation.CustomerUserId, ticket.TicketId);

        return (await _ticketService.GetTicketDetailAsync(ticket.TicketId, actorUserId, actorRole == "Admin"))!;
    }

    private static ConversationDto MapToDto(SupportConversation c)
    {
        var lastMsg = c.Messages.OrderByDescending(m => m.CreatedAt).FirstOrDefault();
        return new ConversationDto
        {
            ConversationId = c.ConversationId,
            ConversationReference = c.ConversationReference,
            CustomerUserId = c.CustomerUserId,
            CustomerName = c.CustomerUser != null ? $"{c.CustomerUser.FirstName} {c.CustomerUser.LastName}".Trim() : "Customer",
            CustomerEmail = c.CustomerUser?.Email ?? string.Empty,
            Channel = c.Channel,
            Status = c.Status.ToString(),
            AssignedAdminUserId = c.AssignedAdminUserId,
            AssignedAdminName = c.AssignedAdminUser != null ? $"{c.AssignedAdminUser.FirstName} {c.AssignedAdminUser.LastName}".Trim() : null,
            CurrentBookingId = c.CurrentBookingId,
            StartedAt = c.StartedAt,
            LastMessageAt = c.LastMessageAt,
            ClosedAt = c.ClosedAt,
            LastMessageSnippet = lastMsg?.Body,
            MessageCount = c.Messages.Count
        };
    }
}
