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
[Authorize]
[Route("api/owner/parking")]
public class OwnerParkingController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly CloudinaryService _cloudinaryService;
    private readonly IPropertyService _propertyService;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<OwnerParkingController> _logger;

    public OwnerParkingController(
        ApplicationDbContext context,
        CloudinaryService cloudinaryService,
        IPropertyService propertyService,
        AccessLogService accessLogService,
        ILogger<OwnerParkingController> logger)
    {
        _context = context;
        _cloudinaryService = cloudinaryService;
        _propertyService = propertyService;
        _accessLogService = accessLogService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a private parking draft and submits its first verification document.
    /// The spot cannot be published or booked until its verification is approved.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ParkingRegistrationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ParkingRegistrationResponse>> RegisterParking(
        [FromForm] ParkingRegistrationRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (!ModelState.IsValid)
        {
            return BadRequest(new ErrorResponse { Code = 400, Success = false, Message = "Invalid request." });
        }

        var allowedTypes = new[] { "application/pdf", "image/jpeg", "image/jpg", "image/png" };
        if (!allowedTypes.Contains(request.Document.ContentType.ToLowerInvariant()))
        {
            return BadRequest(new ErrorResponse { Code = 400, Success = false, Message = "Only PDF, JPG and PNG files are allowed." });
        }

        var property = await _propertyService.ResolvePropertyAsync(request);
        if (property == null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = 500,
                Success = false,
                Message = "Could not find or create the property. Please check the property name and address."
            });
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            const string folder = "parkjom/verification-documents";
            var uploadResult = await _cloudinaryService.UploadPrivateDocumentAsync(request.Document, folder);
            if (uploadResult == null)
            {
                throw new InvalidOperationException("Cloudinary upload failed.");
            }

            var mediaFile = new MediaFile
            {
                PublicId = uploadResult.PublicId,
                SecureUrl = uploadResult.SecureUrl.ToString(),
                ResourceType = uploadResult.ResourceType,
                Format = uploadResult.Format ?? Path.GetExtension(request.Document.FileName).TrimStart('.').ToLowerInvariant(),
                OriginalFileName = uploadResult.OriginalFilename,
                Folder = folder,
                UploadedBy = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var parkingSpot = new ParkingSpot
            {
                PropertyId = property.PropertyId,
                OwnerId = userId,
                ParkingLabel = $"{request.BayNumber}/{request.Level}",
                AvailabilityStatus = AvailabilityStatus.Inactive,
                IsPublished = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var verificationRequest = new ParkingVerificationRequest
            {
                ParkingSpot = parkingSpot,
                SubmittedByUserId = userId,
                VerificationStatus = VerificationStatus.Pending,
                IsCurrent = true,
                SubmittedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var verificationDocument = new VerificationDocument
            {
                VerificationRequest = verificationRequest,
                MediaFile = mediaFile,
                DocumentType = request.DocumentType,
                UploadedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.MediaFiles.Add(mediaFile);
            _context.ParkingSpots.Add(parkingSpot);
            _context.ParkingVerificationRequests.Add(verificationRequest);
            _context.VerificationDocuments.Add(verificationDocument);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            await _accessLogService.LogAsync(User, "RegisterParking", true,
                $"ParkingSpotId={parkingSpot.ParkingSpotId} VerificationRequestId={verificationRequest.VerificationRequestId}");

            return Ok(new ParkingRegistrationResponse
            {
                Code = 200,
                Success = true,
                Message = "Parking registered successfully. Your verification request has been submitted.",
                ParkingSpotId = parkingSpot.ParkingSpotId,
                VerificationRequestId = verificationRequest.VerificationRequestId
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Verification request submission failed for UserId={UserId}", userId);
            await _accessLogService.LogAsync(User, "RegisterParking", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = 500,
                Success = false,
                Message = "An unexpected error occurred while submitting the verification request."
            });
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(DisplayMyParkingResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DisplayMyParkingResponse>> GetMyParking()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var spots = await _context.ParkingSpots
            .AsNoTracking()
            .Where(p => p.OwnerId == userId)
            .Include(p => p.VerificationRequests.Where(v => v.IsCurrent))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        return Ok(new DisplayMyParkingResponse
        {
            Code = 200,
            Success = true,
            Message = spots.Count == 0 ? "No parking spots found." : "Parking spots retrieved successfully.",
            Data = spots.Select(MapToDisplayParkingSpotDTO).ToList()
        });
    }

    [HttpDelete("{spotId:int}")]
    [ProducesResponseType(typeof(DeleteParkingSpotResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DeleteParkingSpotResponse>> DeleteParking(int spotId)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var spot = await _context.ParkingSpots
            .Include(p => p.Bookings)
            .FirstOrDefaultAsync(p => p.ParkingSpotId == spotId);

        if (spot == null)
        {
            return NotFound(new ErrorResponse { Code = 404, Success = false, Message = "Parking spot not found." });
        }

        if (spot.OwnerId != userId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = 403,
                Success = false,
                Message = "You are not authorized to delete this parking spot."
            });
        }

        if (spot.Bookings.Any(b => b.BookingStatus is BookingStatus.Pending or BookingStatus.Confirmed or BookingStatus.Active))
        {
            return BadRequest(new ErrorResponse
            {
                Code = 400,
                Success = false,
                Message = "Cannot delete a parking spot with active bookings."
            });
        }

        spot.AvailabilityStatus = AvailabilityStatus.Deleted;
        spot.IsPublished = false;
        spot.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        await _accessLogService.LogAsync(User, "DeleteParkingSpot", true, $"ParkingSpotId={spotId}");

        return Ok(new DeleteParkingSpotResponse
        {
            Code = 200,
            Success = true,
            Message = "Parking spot deleted successfully."
        });
    }

    /// <summary>
    /// Saves owner-facing listing content and prices for an approved parking spot.
    /// This endpoint never publishes the listing.
    /// </summary>
    [HttpPut("{spotId:int}/configuration")]
    [ProducesResponseType(typeof(OwnerParkingConfigurationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OwnerParkingConfigurationResponse>> UpdateConfiguration(
        int spotId,
        [FromBody] UpdateOwnerParkingConfigurationRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        try
        {
            var spot = await _context.ParkingSpots
                .Include(p => p.VerificationRequests.Where(v => v.IsCurrent))
                .Include(p => p.ParkingSpotImages)
                .FirstOrDefaultAsync(p => p.ParkingSpotId == spotId);

            if (spot == null)
            {
                await _accessLogService.LogAsync(User, "UpdateOwnerParkingConfiguration", false, $"Spot not found (id={spotId})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Parking spot not found."
                });
            }

            if (spot.OwnerId != userId)
            {
                await _accessLogService.LogAsync(User, "UpdateOwnerParkingConfiguration", false, $"Not owner (id={spotId})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "You are not authorized to configure this parking spot."
                });
            }

            var isVerified = spot.VerificationRequests.Any(v => v.VerificationStatus == VerificationStatus.Approved);
            if (!isVerified)
            {
                await _accessLogService.LogAsync(User, "UpdateOwnerParkingConfiguration", false, $"Not verified (id={spotId})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Parking configuration is available only after verification is approved."
                });
            }

            if (spot.AvailabilityStatus == AvailabilityStatus.Deleted)
            {
                await _accessLogService.LogAsync(User, "UpdateOwnerParkingConfiguration", false, $"Deleted spot (id={spotId})");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "A deleted parking spot cannot be configured."
                });
            }

            spot.Description = request.Description.Trim();
            spot.DailyRate = request.DailyRate;
            spot.MonthlyRate = request.MonthlyRate;
            spot.UpdatedAt = DateTime.UtcNow;

            var missingRequirements = GetConfigurationRequirements(spot);
            spot.IsConfigurationComplete = missingRequirements.Count == 0;
            spot.ConfiguredAt = spot.IsConfigurationComplete ? DateTime.UtcNow : null;

            await _context.SaveChangesAsync();
            await _accessLogService.LogAsync(User, "UpdateOwnerParkingConfiguration", true, $"ParkingSpotId={spotId}");

            return Ok(new OwnerParkingConfigurationResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = spot.IsConfigurationComplete
                    ? "Parking configuration saved. Listing content is complete; availability setup is still required before publication."
                    : "Parking configuration saved. Add at least one listing image before the listing can be published.",
                ParkingSpotId = spot.ParkingSpotId,
                IsConfigurationComplete = spot.IsConfigurationComplete,
                MissingRequirements = missingRequirements,
                UpdatedAt = spot.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating configuration for parking spot {ParkingSpotId}", spotId);
            await _accessLogService.LogAsync(User, "UpdateOwnerParkingConfiguration", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while saving the parking configuration."
            });
        }
    }

    private static List<string> GetConfigurationRequirements(Models.ParkingSpot spot)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(spot.Description)) missing.Add("description");
        if (!spot.DailyRate.HasValue || spot.DailyRate <= 0) missing.Add("dailyRate");
        if (!spot.MonthlyRate.HasValue || spot.MonthlyRate <= 0) missing.Add("monthlyRate");
        if (!spot.ParkingSpotImages.Any()) missing.Add("listingImage");

        return missing;
    }

    private static DisplayParkingSpotDTO MapToDisplayParkingSpotDTO(ParkingSpot parkingSpot)
    {
        return new DisplayParkingSpotDTO
        {
            ParkingSpotId = parkingSpot.ParkingSpotId,
            PropertyId = parkingSpot.PropertyId,
            OwnerId = parkingSpot.OwnerId,
            ParkingLabel = parkingSpot.ParkingLabel,
            AvailabilityStatus = parkingSpot.AvailabilityStatus.ToString(),
            VerificationStatus = parkingSpot.VerificationRequests.FirstOrDefault()?.VerificationStatus.ToString()
                ?? VerificationStatus.Pending.ToString(),
            MonthlyRate = parkingSpot.MonthlyRate,
            DailyRate = parkingSpot.DailyRate,
            IsPublished = parkingSpot.IsPublished,
            CreatedAt = parkingSpot.CreatedAt,
            UpdatedAt = parkingSpot.UpdatedAt
        };
    }
}
