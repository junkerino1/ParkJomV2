using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;

namespace ParkJomV2.Services.Support;

public class DisputeService
{
    private readonly ApplicationDbContext _context;
    private readonly SupportAuditService _auditService;
    private readonly WalletService _walletService;
    private readonly TransactionService _transactionService;
    private readonly CloudinaryService _cloudinaryService;
    private readonly ISupportRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<DisputeService> _logger;

    public DisputeService(
        ApplicationDbContext context,
        SupportAuditService auditService,
        WalletService walletService,
        TransactionService transactionService,
        CloudinaryService cloudinaryService,
        ISupportRealtimeNotifier realtimeNotifier,
        ILogger<DisputeService> logger)
    {
        _context = context;
        _auditService = auditService;
        _walletService = walletService;
        _transactionService = transactionService;
        _cloudinaryService = cloudinaryService;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    public async Task<List<DisputeCustomerSummaryDto>> GetMyDisputesAsync(int userId, string? status = null)
    {
        var query = _context.DisputeInvestigations
            .AsNoTracking()
            .Where(d => d.CustomerUserId == userId);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<DisputeStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(d => d.Status == parsedStatus);
        }

        return await query
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DisputeCustomerSummaryDto
            {
                DisputeId = d.DisputeId,
                DisputeReference = d.DisputeReference,
                DisputeType = d.DisputeType.ToString(),
                Status = d.Status.ToString(),
                Amount = d.Amount,
                Currency = d.Currency,
                CreatedAt = d.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<DisputeCustomerDto?> GetDisputeDetailForCustomerAsync(int disputeId, int userId)
    {
        var dispute = await _context.DisputeInvestigations
            .Include(d => d.Evidences)
            .FirstOrDefaultAsync(d => d.DisputeId == disputeId && d.CustomerUserId == userId);

        if (dispute == null) return null;

        return new DisputeCustomerDto
        {
            DisputeId = dispute.DisputeId,
            DisputeReference = dispute.DisputeReference,
            DisputeType = dispute.DisputeType.ToString(),
            Status = dispute.Status.ToString(),
            Amount = dispute.Amount,
            Currency = dispute.Currency,
            Reason = dispute.Reason,
            TicketId = dispute.TicketId,
            BookingId = dispute.BookingId,
            Decision = dispute.Decision,
            DecisionReason = dispute.DecisionReason,
            DecidedAt = dispute.DecidedAt,
            CreatedAt = dispute.CreatedAt,
            UpdatedAt = dispute.UpdatedAt,
            Evidences = dispute.Evidences.Select(e => new DisputeEvidenceDto
            {
                DisputeEvidenceId = e.DisputeEvidenceId,
                DisputeId = e.DisputeId,
                EvidenceType = e.EvidenceType,
                FileName = e.FileName,
                FileUrl = e.FileUrl,
                UploadedRole = e.UploadedRole,
                IsVerified = e.IsVerified,
                Description = e.Description,
                CreatedAt = e.CreatedAt
            }).ToList()
        };
    }

    public async Task<(List<DisputeCustomerSummaryDto> Items, int TotalCount)> GetAdminDisputesAsync(
        string? status = null,
        string? type = null,
        string? team = null,
        int page = 1,
        int pageSize = 25)
    {
        var query = _context.DisputeInvestigations.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<DisputeStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(d => d.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<DisputeType>(type, true, out var parsedType))
        {
            query = query.Where(d => d.DisputeType == parsedType);
        }

        if (!string.IsNullOrWhiteSpace(team))
        {
            query = query.Where(d => d.AssignedTeam == team);
        }

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DisputeCustomerSummaryDto
            {
                DisputeId = d.DisputeId,
                DisputeReference = d.DisputeReference,
                DisputeType = d.DisputeType.ToString(),
                Status = d.Status.ToString(),
                Amount = d.Amount,
                Currency = d.Currency,
                CreatedAt = d.CreatedAt
            })
            .ToListAsync();

        return (items, total);
    }

    public async Task<DisputeAdminDto?> GetAdminDisputeDetailAsync(int disputeId)
    {
        var dispute = await _context.DisputeInvestigations
            .Include(d => d.CustomerUser)
            .Include(d => d.AssignedUser)
            .Include(d => d.DecidedByUser)
            .Include(d => d.Evidences)
                .ThenInclude(e => e.UploadedByUser)
            .FirstOrDefaultAsync(d => d.DisputeId == disputeId);

        if (dispute == null) return null;

        var auditTimeline = await _context.SupportAuditEvents
            .AsNoTracking()
            .Where(a => a.ObjectType == "Dispute" && a.ObjectId == disputeId)
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

        return new DisputeAdminDto
        {
            DisputeId = dispute.DisputeId,
            DisputeReference = dispute.DisputeReference,
            DisputeType = dispute.DisputeType.ToString(),
            Status = dispute.Status.ToString(),
            CustomerUserId = dispute.CustomerUserId,
            CustomerName = $"{dispute.CustomerUser.FirstName} {dispute.CustomerUser.LastName}".Trim(),
            CustomerEmail = dispute.CustomerUser.Email,
            Amount = dispute.Amount,
            Currency = dispute.Currency,
            Reason = dispute.Reason,
            TicketId = dispute.TicketId,
            BookingId = dispute.BookingId,
            PaymentId = dispute.PaymentId,
            TransactionId = dispute.TransactionId,
            AssignedTeam = dispute.AssignedTeam,
            AssignedUserId = dispute.AssignedUserId,
            AssignedUserName = dispute.AssignedUser != null ? $"{dispute.AssignedUser.FirstName} {dispute.AssignedUser.LastName}".Trim() : null,
            Decision = dispute.Decision,
            DecisionReason = dispute.DecisionReason,
            DecidedByUserId = dispute.DecidedByUserId,
            DecidedByUserName = dispute.DecidedByUser != null ? $"{dispute.DecidedByUser.FirstName} {dispute.DecidedByUser.LastName}".Trim() : null,
            DecidedAt = dispute.DecidedAt,
            CreatedAt = dispute.CreatedAt,
            UpdatedAt = dispute.UpdatedAt,
            Evidences = dispute.Evidences.Select(e => new DisputeEvidenceDto
            {
                DisputeEvidenceId = e.DisputeEvidenceId,
                DisputeId = e.DisputeId,
                EvidenceType = e.EvidenceType,
                FileName = e.FileName,
                FileUrl = e.FileUrl,
                UploadedRole = e.UploadedRole,
                UploadedByName = $"{e.UploadedByUser.FirstName} {e.UploadedByUser.LastName}".Trim(),
                IsVerified = e.IsVerified,
                Description = e.Description,
                CreatedAt = e.CreatedAt
            }).ToList(),
            AuditTimeline = auditTimeline
        };
    }

    public async Task<DisputeEvidenceDto> UploadEvidenceAsync(int disputeId, int userId, string role, UploadDisputeEvidenceRequestDto request)
    {
        var dispute = await _context.DisputeInvestigations.FindAsync(disputeId);
        if (dispute == null) throw new KeyNotFoundException("Dispute not found");

        var uploadResult = await _cloudinaryService.UploadPrivateDocumentAsync(request.File, "dispute_evidence");
        var now = DateTime.UtcNow;

        var evidence = new DisputeEvidence
        {
            DisputeId = disputeId,
            EvidenceType = string.IsNullOrWhiteSpace(request.EvidenceType) ? "Receipt" : request.EvidenceType.Trim(),
            FileName = request.File.FileName,
            FileUrl = uploadResult.SecureUrl?.ToString() ?? uploadResult.Url?.ToString() ?? string.Empty,
            UploadedByUserId = userId,
            UploadedRole = role,
            IsVerified = role == "Admin",
            Description = request.Description,
            CreatedAt = now
        };

        _context.DisputeEvidences.Add(evidence);
        dispute.UpdatedAt = now;
        await _context.SaveChangesAsync();

        await _auditService.LogAsync("Dispute", disputeId, dispute.DisputeReference, "EvidenceAdded", userId, role, null, null, $"Evidence uploaded: {request.File.FileName}");
        await _realtimeNotifier.BroadcastEventAsync("dispute.updated", new { disputeId = disputeId, reference = dispute.DisputeReference });

        var user = await _context.Users.FindAsync(userId);
        return new DisputeEvidenceDto
        {
            DisputeEvidenceId = evidence.DisputeEvidenceId,
            DisputeId = evidence.DisputeId,
            EvidenceType = evidence.EvidenceType,
            FileName = evidence.FileName,
            FileUrl = evidence.FileUrl,
            UploadedRole = evidence.UploadedRole,
            UploadedByName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : role,
            IsVerified = evidence.IsVerified,
            Description = evidence.Description,
            CreatedAt = evidence.CreatedAt
        };
    }

    public async Task RequestEvidenceAsync(int disputeId, int adminUserId, RequestDisputeEvidenceRequestDto request)
    {
        var dispute = await _context.DisputeInvestigations
            .Include(d => d.Ticket)
            .FirstOrDefaultAsync(d => d.DisputeId == disputeId);

        if (dispute == null) throw new KeyNotFoundException("Dispute not found");

        var now = DateTime.UtcNow;
        var oldStatus = dispute.Status.ToString();
        dispute.Status = DisputeStatus.MoreInfo;
        dispute.UpdatedAt = now;

        // Add notice to linked ticket if exists
        if (dispute.TicketId.HasValue)
        {
            var msg = new SupportTicketMessage
            {
                TicketId = dispute.TicketId.Value,
                SenderUserId = adminUserId,
                SenderRole = "Admin",
                Body = $"[Dispute Investigation Update] Evidence Requested: {request.RequiredEvidence}\nMessage: {request.CustomerMessage}\nDeadline: {request.Deadline?.ToString("yyyy-MM-dd") ?? "Within 48 hours"}",
                CreatedAt = now
            };
            _context.SupportTicketMessages.Add(msg);
        }

        await _context.SaveChangesAsync();

        await _auditService.LogAsync("Dispute", disputeId, dispute.DisputeReference, "EvidenceRequested", adminUserId, "Admin", oldStatus, "MoreInfo", request.RequiredEvidence);
        await _realtimeNotifier.BroadcastEventAsync("dispute.updated", new { disputeId = disputeId, status = "MoreInfo" }, dispute.CustomerUserId);
    }

    public async Task<DisputeAdminDto> AssignDisputeAsync(int disputeId, int adminUserId, AssignDisputeRequestDto request)
    {
        var dispute = await _context.DisputeInvestigations.FindAsync(disputeId);
        if (dispute == null) throw new KeyNotFoundException("Dispute not found");

        var oldState = dispute.Status.ToString();
        var now = DateTime.UtcNow;

        if (request.AssignedUserId.HasValue)
        {
            dispute.AssignedUserId = request.AssignedUserId.Value;
        }
        if (!string.IsNullOrWhiteSpace(request.AssignedTeam))
        {
            dispute.AssignedTeam = request.AssignedTeam.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<DisputeStatus>(request.Status, true, out var newStatus))
        {
            dispute.Status = newStatus;
        }

        dispute.UpdatedAt = now;
        await _context.SaveChangesAsync();

        await _auditService.LogAsync("Dispute", disputeId, dispute.DisputeReference, "Assigned", adminUserId, "Admin", oldState, dispute.Status.ToString(), $"Assigned to {dispute.AssignedUserId} / {dispute.AssignedTeam}");
        await _realtimeNotifier.BroadcastEventAsync("dispute.updated", new { disputeId = disputeId, status = dispute.Status.ToString() });

        return (await GetAdminDisputeDetailAsync(disputeId))!;
    }

    public async Task<DisputeAdminDto> MakeDecisionAsync(int disputeId, int adminUserId, DisputeDecisionRequestDto request)
    {
        var dispute = await _context.DisputeInvestigations
            .Include(d => d.CustomerUser)
                .ThenInclude(u => u.Wallet)
            .Include(d => d.Booking)
            .FirstOrDefaultAsync(d => d.DisputeId == disputeId);

        if (dispute == null) throw new KeyNotFoundException("Dispute not found");

        var now = DateTime.UtcNow;
        var oldStatus = dispute.Status.ToString();

        dispute.Decision = request.Decision;
        dispute.DecisionReason = request.Reason;
        dispute.DecidedByUserId = adminUserId;
        dispute.DecidedAt = now;
        dispute.UpdatedAt = now;

        if (string.Equals(request.Decision, "ApproveReversal", StringComparison.OrdinalIgnoreCase))
        {
            dispute.Status = DisputeStatus.Approved;
            var refundAmount = request.Amount ?? (dispute.Amount > 0 ? dispute.Amount : dispute.Booking?.TotalAmount ?? 0m);

            if (refundAmount > 0)
            {
                var wallet = dispute.CustomerUser.Wallet;
                if (wallet != null)
                {
                    wallet.Balance += refundAmount;
                    wallet.UpdatedAt = now;

                    _transactionService.Create(
                        wallet.WalletId,
                        null,
                        dispute.Booking,
                        TransactionType.Refund,
                        refundAmount,
                        PaymentMethod.Wallet,
                        $"REF-DSP-{dispute.DisputeReference}",
                        now
                    );
                }
            }
        }
        else if (string.Equals(request.Decision, "Decline", StringComparison.OrdinalIgnoreCase))
        {
            dispute.Status = DisputeStatus.Declined;
        }
        else if (string.Equals(request.Decision, "NeedMoreInfo", StringComparison.OrdinalIgnoreCase))
        {
            dispute.Status = DisputeStatus.MoreInfo;
        }

        await _context.SaveChangesAsync();

        await _auditService.LogAsync("Dispute", disputeId, dispute.DisputeReference, "DecisionMade", adminUserId, "Admin", oldStatus, dispute.Status.ToString(), $"Decision: {request.Decision}. Reason: {request.Reason}");
        await _realtimeNotifier.BroadcastEventAsync("dispute.updated", new { disputeId = disputeId, status = dispute.Status.ToString(), decision = dispute.Decision }, dispute.CustomerUserId);

        return (await GetAdminDisputeDetailAsync(disputeId))!;
    }
}
