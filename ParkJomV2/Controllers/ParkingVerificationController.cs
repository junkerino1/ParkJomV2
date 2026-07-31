using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using System.Security.Claims;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/parking")]
public class ParkingVerificationController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ParkingVerificationController> _logger;

    public ParkingVerificationController(ApplicationDbContext context, ILogger<ParkingVerificationController> logger)
    {
        _context = context;
        _logger = logger;
    }

    

    /// <summary>
    /// Get all parking verification requests (Admin only)
    /// </summary>
    [Authorize]
    [HttpGet("verification-requests")]
    [ProducesResponseType(typeof(VerificationRequestListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<VerificationRequestListResponse>> GetVerificationRequests()
    {
        try
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found", userId);
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            if (user.UserType != UserType.Admin)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Only administrators can access verification requests"
                });
            }

            var verificationRequests = await _context.ParkingVerificationRequests
                .Include(vr => vr.ParkingSpot)
                .ThenInclude(ps => ps.Property)
                .Include(vr => vr.SubmittedByUser)
                .OrderByDescending(vr => vr.SubmittedAt)
                .ToListAsync();

            var result = verificationRequests
                .Select(MapToVerificationRequestListDTO)
                .ToList();

            _logger.LogInformation("Retrieved {Count} verification requests for admin user {UserId}", result.Count, userId);

            return Ok(new VerificationRequestListResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Verification requests retrieved successfully",
                Data = result
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving verification requests");
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
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found", userId);
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            if (user.UserType != UserType.Admin)
            {
                _logger.LogWarning("Unauthorized access attempt. UserId={UserId}, UserType={UserType}", userId, user.UserType);
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
                _logger.LogWarning("Verification request {VerificationRequestId} not found", id);
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Verification request not found"
                });
            }

            var result = MapToVerificationRequestDTO(verificationRequest);

            _logger.LogInformation("Retrieved verification request {VerificationRequestId} for admin user {UserId}", id, userId);

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
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found", userId);
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            if (user.UserType != UserType.Admin)
            {
                _logger.LogWarning("Unauthorized approval attempt. UserId={UserId}, UserType={UserType}", userId, user.UserType);
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
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Verification request not found"
                });
            }

            if (verificationRequest.VerificationStatus != VerificationStatus.Pending)
            {
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

            verificationRequest.ReviewedAt = DateTime.UtcNow;
            verificationRequest.UpdatedAt = DateTime.UtcNow;
            verificationRequest.ReviewNotes = request.ReviewNotes;

            _context.ParkingVerificationRequests.Update(verificationRequest);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Verification request {VerificationRequestId} status updated from {PreviousStatus} to {NewStatus} by admin {UserId}",
                id, previousStatus, verificationRequest.VerificationStatus, user.FirstName + user.LastName);

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
            SubmittedAt = request.SubmittedAt
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
