using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/parking")]
public class ParkingVerificationController : ControllerBase
{
    private const int PageSize = 100;

    private readonly ApplicationDbContext _context;
    private readonly CurrentUserService _currentUser;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<ParkingVerificationController> _logger;

    public ParkingVerificationController(ApplicationDbContext context, CurrentUserService currentUser, AccessLogService accessLogService, ILogger<ParkingVerificationController> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _accessLogService = accessLogService;
        _logger = logger;
    }

    

    /// <summary>
    /// Get all parking verification requests (Admin only), with optional status filter and paging.
    /// status: pending | completed (approved + rejected) | approved | rejected (default: all)
    /// page: 1-based, 100 results per page.
    /// </summary>
    [Authorize]
    [HttpGet("verification-requests")]
    [ProducesResponseType(typeof(VerificationRequestListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<VerificationRequestListResponse>> GetVerificationRequests(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1)
    {
        try
        {
            var user = await _currentUser.GetCurrentUserAsync();

            if (user == null)
            {
                await _accessLogService.LogAsync(User, "GetVerificationRequests", false, "User not found");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            if (user.UserType != UserType.Admin)
            {
                await _accessLogService.LogAsync(User, "GetVerificationRequests", false, "Not an admin");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Only administrators can access verification requests"
                });
            }

            // Normalize the status filter (case-insensitive)
            var normalizedStatus = string.IsNullOrWhiteSpace(status)
                ? string.Empty
                : status.Trim().ToLowerInvariant();

            if (normalizedStatus.Length > 0
                && normalizedStatus is not ("pending" or "completed" or "approved" or "rejected"))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Status must be one of: pending, completed, approved, rejected"
                });
            }

            if (page < 1)
            {
                page = 1;
            }

            var query = _context.ParkingVerificationRequests.AsNoTracking();

            // pending = Pending only; completed = Approved + Rejected
            query = normalizedStatus switch
            {
                "pending" => query.Where(vr => vr.VerificationStatus == VerificationStatus.Pending),
                "approved" => query.Where(vr => vr.VerificationStatus == VerificationStatus.Approved),
                "rejected" => query.Where(vr => vr.VerificationStatus == VerificationStatus.Rejected),
                "completed" => query.Where(vr => vr.VerificationStatus == VerificationStatus.Approved
                                              || vr.VerificationStatus == VerificationStatus.Rejected),
                _ => query
            };

            var total = await query.CountAsync();
            var totalPages = PageSize > 0
                ? (int)Math.Ceiling(total / (double)PageSize)
                : 0;

            var verificationRequests = await query
                .OrderByDescending(vr => vr.SubmittedAt)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .AsSplitQuery()
                .Include(vr => vr.ParkingSpot)
                    .ThenInclude(ps => ps.Property)
                .Include(vr => vr.SubmittedByUser)
                .Include(vr => vr.VerificationDocuments)
                    .ThenInclude(vd => vd.MediaFile)
                .ToListAsync();

            var result = verificationRequests
                .Select(MapToVerificationRequestListDTO)
                .ToList();

            await _accessLogService.LogAsync(User, "GetVerificationRequests", true, $"total={total} status={normalizedStatus}");

            return Ok(new VerificationRequestListResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Verification requests retrieved successfully",
                Data = result,
                Page = page,
                PageSize = PageSize,
                Total = total,
                TotalPages = totalPages,
                Status = normalizedStatus
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving verification requests");
            await _accessLogService.LogAsync(User, "GetVerificationRequests", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving verification requests"
            });
        }
    }

    /// <summary>
    /// Get a specific verification request with documents (Admin only)
    /// </summary>
    [Authorize]
    [HttpGet("verification-requests/{id}")]
    [ProducesResponseType(typeof(VerificationRequestDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<VerificationRequestDetailResponse>> GetVerificationRequestById(int id)
    {
        try
        {
            var user = await _currentUser.GetCurrentUserAsync();

            if (user == null)
            {
                await _accessLogService.LogAsync(User, "GetVerificationRequestById", false, $"User not found (id={id})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            if (user.UserType != UserType.Admin)
            {
                await _accessLogService.LogAsync(User, "GetVerificationRequestById", false, $"Not an admin (id={id})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Only administrators can access verification requests"
                });
            }

            var verificationRequest = await _context.ParkingVerificationRequests
                .Include(vr => vr.ParkingSpot)
                    .ThenInclude(ps => ps.Property)
                .Include(vr => vr.SubmittedByUser)
                .Include(vr => vr.VerificationDocuments)
                    .ThenInclude(vd => vd.MediaFile)
                .FirstOrDefaultAsync(vr => vr.VerificationRequestId == id);

            if (verificationRequest == null)
            {
                await _accessLogService.LogAsync(User, "GetVerificationRequestById", false, $"Verification request not found (id={id})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Verification request not found"
                });
            }

            var result = MapToVerificationRequestDTO(verificationRequest);

            await _accessLogService.LogAsync(User, "GetVerificationRequestById", true, $"VerificationRequestId={id}");

            return Ok(new VerificationRequestDetailResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Verification request retrieved successfully",
                Data = result
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving verification request {VerificationRequestId}", id);
            await _accessLogService.LogAsync(User, "GetVerificationRequestById", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving the verification request"
            });
        }
    }

    /// <summary>
    /// Approve or reject a parking verification request (Admin only)
    /// </summary>
    [Authorize]
    [HttpPost("verification-requests/{id}/decision")]
    [ProducesResponseType(typeof(ApprovalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApprovalResponse>> DecideVerificationRequest(int id, [FromBody] DecisionRequest request)
    {
        try
        {
            var user = await _currentUser.GetCurrentUserAsync();

            if (user == null)
            {
                await _accessLogService.LogAsync(User, "DecideVerificationRequest", false, $"User not found (id={id})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            if (user.UserType != UserType.Admin)
            {
                await _accessLogService.LogAsync(User, "DecideVerificationRequest", false, $"Not an admin (id={id})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Only administrators can approve or reject verification requests"
                });
            }

            var verificationRequest = await _context.ParkingVerificationRequests
                .Include(vr => vr.ParkingSpot)
                .FirstOrDefaultAsync(vr => vr.VerificationRequestId == id);

            if (verificationRequest == null)
            {
                _logger.LogWarning("Verification request {VerificationRequestId} not found", id);
                await _accessLogService.LogAsync(User, "DecideVerificationRequest", false, $"Verification request not found (id={id})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Verification request not found"
                });
            }

            if (verificationRequest.VerificationStatus != VerificationStatus.Pending)
            {
                await _accessLogService.LogAsync(User, "DecideVerificationRequest", false, $"Already processed (id={id})");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Verification request has already been processed"
                });
            }

            var normalizedDecision = request.Decision.Trim().ToLowerInvariant();
            if (normalizedDecision != "approved" && normalizedDecision != "rejected")
            {
                await _accessLogService.LogAsync(User, "DecideVerificationRequest", false, $"Invalid decision '{request.Decision}'");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Decision must be 'approved' or 'rejected'"
                });
            }

            var previousStatus = verificationRequest.VerificationStatus;

            verificationRequest.VerificationStatus = normalizedDecision == "approved"
                ? VerificationStatus.Approved
                : VerificationStatus.Rejected;

            // On approval, mark the parking spot as Pending (approved, awaiting owner configuration/publishing).
            if (verificationRequest.VerificationStatus == VerificationStatus.Approved
                && verificationRequest.ParkingSpot != null)
            {
                verificationRequest.ParkingSpot.AvailabilityStatus = AvailabilityStatus.Pending;
                verificationRequest.ParkingSpot.UpdatedAt = DateTime.UtcNow;
            }

            verificationRequest.ReviewedAt = DateTime.UtcNow;
            verificationRequest.UpdatedAt = DateTime.UtcNow;
            verificationRequest.ReviewNotes = request.ReviewNotes;

            _context.ParkingVerificationRequests.Update(verificationRequest);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Verification request {VerificationRequestId} status updated from {PreviousStatus} to {NewStatus} by admin {UserId}",
                id, previousStatus, verificationRequest.VerificationStatus, user.FirstName + user.LastName);

            await _accessLogService.LogAsync(User, "DecideVerificationRequest", true, $"VerificationRequestId={id} {normalizedDecision}");

            return Ok(new ApprovalResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = $"Verification request {normalizedDecision} successfully",
                VerificationRequestId = id,
                ParkingSpotId = verificationRequest.ParkingSpotId,
                VerificationStatus = verificationRequest.VerificationStatus.ToString(),
                UpdatedAt = verificationRequest.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving/rejecting verification request {VerificationRequestId}", id);
            await _accessLogService.LogAsync(User, "DecideVerificationRequest", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while processing the verification request"
            });
        }
    }

    private static VerificationRequestListDTO MapToVerificationRequestListDTO(ParkingVerificationRequest request)
    {
        return new VerificationRequestListDTO
        {
            VerificationRequestId = request.VerificationRequestId,
            ParkingSpotId = request.ParkingSpotId,
            ParkingLabel = request.ParkingSpot?.ParkingLabel,
            PropertyId = request.ParkingSpot?.PropertyId,
            PropertyName = request.ParkingSpot?.Property?.PropertyName,
            SubmittedByUserId = request.SubmittedByUserId,
            SubmittedByEmail = request.SubmittedByUser?.Email,
            SubmittedByName = $"{request.SubmittedByUser?.FirstName} {request.SubmittedByUser?.LastName}".Trim(),
            VerificationStatus = request.VerificationStatus.ToString(),
            SubmittedAt = request.SubmittedAt,
            Documents = request.VerificationDocuments?.Select(vd => new VerificationDocumentDTO
            {
                VerificationDocumentId = vd.VerificationDocumentId,
                DocumentType = vd.DocumentType,
                MediaFileId = vd.MediaFile?.MediaFileId ?? 0,
                ResourceType = vd.MediaFile?.ResourceType,
                Format = vd.MediaFile?.Format,
                OriginalFileName = vd.MediaFile?.OriginalFileName,
                UploadedAt = vd.UploadedAt
            }).ToList() ?? new List<VerificationDocumentDTO>()
        };
    }

    private static VerificationRequestDTO MapToVerificationRequestDTO(ParkingVerificationRequest request)
    {
        return new VerificationRequestDTO
        {
            VerificationRequestId = request.VerificationRequestId,
            ParkingSpotId = request.ParkingSpotId,
            ParkingLabel = request.ParkingSpot?.ParkingLabel,
            PropertyId = request.ParkingSpot?.PropertyId,
            PropertyName = request.ParkingSpot?.Property?.PropertyName,
            SubmittedByUserId = request.SubmittedByUserId,
            SubmittedByEmail = request.SubmittedByUser?.Email,
            SubmittedByName = $"{request.SubmittedByUser?.FirstName} {request.SubmittedByUser?.LastName}".Trim(),
            VerificationStatus = request.VerificationStatus.ToString(),
            SubmittedAt = request.SubmittedAt,
            Documents = request.VerificationDocuments?.Select(vd => new VerificationDocumentDTO
            {
                VerificationDocumentId = vd.VerificationDocumentId,
                DocumentType = vd.DocumentType,
                MediaFileId = vd.MediaFile?.MediaFileId ?? 0,
                ResourceType = vd.MediaFile?.ResourceType,
                Format = vd.MediaFile?.Format,
                OriginalFileName = vd.MediaFile?.OriginalFileName,
                UploadedAt = vd.UploadedAt
            }).ToList() ?? new List<VerificationDocumentDTO>()
        };
    }
}
