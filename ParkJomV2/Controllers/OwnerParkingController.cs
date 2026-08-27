using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using System.Globalization;
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

        if (request.Document is null)
        {
            return BadRequest(new ErrorResponse { Code = 400, Success = false, Message = "Document is required." });
        }

        var detectedContentType = await DetectFileContentType(request.Document);
        var allowedTypes = new[] { "application/pdf", "image/jpeg", "image/png" };
        if (!allowedTypes.Contains(detectedContentType))
        {
            _logger.LogWarning(
                "Rejected parking verification document. FileName={FileName}, ClientContentType={ClientContentType}, DetectedContentType={DetectedContentType}",
                request.Document.FileName,
                request.Document.ContentType,
                detectedContentType);
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
                IsConfigurationComplete = false,
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

    /// <summary>
    /// Uploads public, renter-facing listing images after the owner has an approved
    /// verification request. The first image becomes primary when no primary image exists.
    /// Cloudinary uploads are cleaned up if the related database save fails.
    /// </summary>
    [HttpPost("{spotId:int}/images")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(OwnerParkingImagesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OwnerParkingImagesResponse>> UploadImages(
        int spotId,
        [FromForm] List<IFormFile> images)
    {
        if (images.Count == 0)
        {
            return BadRequest(new ErrorResponse { Code = 400, Success = false, Message = "Upload at least one listing image." });
        }

        const long maxImageBytes = 10 * 1024 * 1024;
        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
        foreach (var image in images)
        {
            var detectedContentType = await DetectFileContentType(image);
            if (image.Length == 0 || image.Length > maxImageBytes || !allowedTypes.Contains(detectedContentType))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = 400,
                    Success = false,
                    Message = "Images must be JPG, PNG, or WebP files no larger than 10 MB."
                });
            }
        }

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var spot = await _context.ParkingSpots
            .Include(p => p.VerificationRequests.Where(v => v.IsCurrent))
            .Include(p => p.ParkingSpotImages)
                .ThenInclude(i => i.MediaFile)
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
                Message = "You are not authorized to manage this parking spot's images."
            });
        }

        if (!spot.VerificationRequests.Any(v => v.VerificationStatus == VerificationStatus.Approved))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = 403,
                Success = false,
                Message = "Listing images can be uploaded only after verification is approved."
            });
        }

        var newlyUploadedPublicIds = new List<string>();
        try
        {
            var nextDisplayOrder = spot.ParkingSpotImages.Count == 0
                ? 1
                : spot.ParkingSpotImages.Max(i => i.DisplayOrder) + 1;
            var hasPrimaryImage = spot.ParkingSpotImages.Any(i => i.IsPrimary);

            foreach (var image in images)
            {
                var uploadResult = await _cloudinaryService.UploadImageAsync(image, "parkjom/parking-images");
                newlyUploadedPublicIds.Add(uploadResult.PublicId);

                var mediaFile = new MediaFile
                {
                    PublicId = uploadResult.PublicId,
                    SecureUrl = uploadResult.SecureUrl.ToString(),
                    ResourceType = uploadResult.ResourceType,
                    Format = uploadResult.Format ?? Path.GetExtension(image.FileName).TrimStart('.').ToLowerInvariant(),
                    OriginalFileName = uploadResult.OriginalFilename,
                    Folder = "parkjom/parking-images",
                    UploadedBy = userId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                var parkingSpotImage = new ParkingSpotImage
                {
                    ParkingSpotId = spotId,
                    MediaFile = mediaFile,
                    DisplayOrder = nextDisplayOrder++,
                    IsPrimary = !hasPrimaryImage,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                hasPrimaryImage = true;
                _context.ParkingSpotImages.Add(parkingSpotImage);
            }

            spot.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await _accessLogService.LogAsync(User, "UploadOwnerParkingImages", true, $"ParkingSpotId={spotId}; Count={images.Count}");

            var savedImages = await GetImagesAsync(spotId);
            return Ok(new OwnerParkingImagesResponse
            {
                Code = 200,
                Success = true,
                Message = "Listing images uploaded successfully.",
                ParkingSpotId = spotId,
                Data = savedImages
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading images for parking spot {ParkingSpotId}", spotId);
            foreach (var publicId in newlyUploadedPublicIds)
            {
                try { await _cloudinaryService.DeleteAsync(publicId); } catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Could not remove orphaned listing image {PublicId}", publicId);
                }
            }

            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = 500,
                Success = false,
                Message = "An error occurred while uploading listing images."
            });
        }
    }

    /// <summary>
    /// Changes an image's display position and/or makes it the one primary listing image.
    /// Image order is normalised to consecutive values so consumers never receive duplicates.
    /// </summary>
    [HttpPut("{spotId:int}/images/{imageId:int}")]
    [ProducesResponseType(typeof(OwnerParkingImagesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OwnerParkingImagesResponse>> UpdateImage(
        int spotId,
        int imageId,
        [FromBody] UpdateOwnerParkingImageRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var spot = await _context.ParkingSpots
            .Include(p => p.ParkingSpotImages)
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
                Message = "You are not authorized to manage this parking spot's images."
            });
        }

        var image = spot.ParkingSpotImages.FirstOrDefault(i => i.ParkingSpotImageId == imageId);
        if (image == null)
        {
            return NotFound(new ErrorResponse { Code = 404, Success = false, Message = "Parking image not found." });
        }

        if (image.IsPrimary && !request.IsPrimary)
        {
            return BadRequest(new ErrorResponse
            {
                Code = 400,
                Success = false,
                Message = "Select another image as primary before removing the primary image status."
            });
        }

        if (request.IsPrimary)
        {
            foreach (var existingImage in spot.ParkingSpotImages)
            {
                existingImage.IsPrimary = existingImage.ParkingSpotImageId == imageId;
                existingImage.UpdatedAt = DateTime.UtcNow;
            }
        }

        var reorderedImages = spot.ParkingSpotImages
            .Where(i => i.ParkingSpotImageId != imageId)
            .OrderBy(i => i.DisplayOrder)
            .ThenBy(i => i.ParkingSpotImageId)
            .ToList();
        var targetIndex = Math.Min(request.DisplayOrder - 1, reorderedImages.Count);
        reorderedImages.Insert(targetIndex, image);
        for (var index = 0; index < reorderedImages.Count; index++)
        {
            reorderedImages[index].DisplayOrder = index + 1;
            reorderedImages[index].UpdatedAt = DateTime.UtcNow;
        }

        spot.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        await _accessLogService.LogAsync(User, "UpdateOwnerParkingImage", true, $"ParkingSpotId={spotId}; ImageId={imageId}");

        return Ok(new OwnerParkingImagesResponse
        {
            Code = 200,
            Success = true,
            Message = "Listing image updated successfully.",
            ParkingSpotId = spotId,
            Data = await GetImagesAsync(spotId)
        });
    }

    /// <summary>
    /// Deletes an owner listing image from Cloudinary and the database. Published listings
    /// must retain at least one image; deleting the primary image promotes the next image.
    /// </summary>
    [HttpDelete("{spotId:int}/images/{imageId:int}")]
    [ProducesResponseType(typeof(OwnerParkingImagesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OwnerParkingImagesResponse>> DeleteImage(int spotId, int imageId)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var spot = await _context.ParkingSpots
            .Include(p => p.ParkingSpotImages)
                .ThenInclude(i => i.MediaFile)
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
                Message = "You are not authorized to manage this parking spot's images."
            });
        }

        var image = spot.ParkingSpotImages.FirstOrDefault(i => i.ParkingSpotImageId == imageId);
        if (image == null)
        {
            return NotFound(new ErrorResponse { Code = 404, Success = false, Message = "Parking image not found." });
        }

        if (spot.IsPublished && spot.ParkingSpotImages.Count == 1)
        {
            return BadRequest(new ErrorResponse
            {
                Code = 400,
                Success = false,
                Message = "A published listing must retain at least one image."
            });
        }

        await _cloudinaryService.DeleteAsync(image.MediaFile.PublicId, image.MediaFile.ResourceType);
        _context.ParkingSpotImages.Remove(image);
        _context.MediaFiles.Remove(image.MediaFile);

        var remainingImages = spot.ParkingSpotImages
            .Where(i => i.ParkingSpotImageId != imageId)
            .OrderBy(i => i.DisplayOrder)
            .ThenBy(i => i.ParkingSpotImageId)
            .ToList();
        if (image.IsPrimary && remainingImages.Count > 0)
        {
            remainingImages[0].IsPrimary = true;
        }

        for (var index = 0; index < remainingImages.Count; index++)
        {
            remainingImages[index].DisplayOrder = index + 1;
            remainingImages[index].UpdatedAt = DateTime.UtcNow;
        }

        spot.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        await _accessLogService.LogAsync(User, "DeleteOwnerParkingImage", true, $"ParkingSpotId={spotId}; ImageId={imageId}");

        return Ok(new OwnerParkingImagesResponse
        {
            Code = 200,
            Success = true,
            Message = "Listing image deleted successfully.",
            ParkingSpotId = spotId,
            Data = await GetImagesAsync(spotId)
        });
    }

    /// <summary>
    /// Creates one or more availability rules after validating their date, time, and day coverage
    /// and rejecting duplicate or overlapping rules for the parking spot.
    /// </summary>
    [HttpPost("{spotId:int}/availability-rules")]
    [ProducesResponseType(typeof(OwnerAvailabilityRulesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OwnerAvailabilityRulesResponse>> CreateAvailabilityRules(
        int spotId,
        [FromBody] CreateOwnerAvailabilityRulesRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (request.Rules.Count == 0)
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "At least one availability rule is required."
            });
        }

        var spot = await _context.ParkingSpots
            .Include(p => p.VerificationRequests.Where(v => v.IsCurrent))
            .Include(p => p.ParkingAvailabilities)
            .FirstOrDefaultAsync(p => p.ParkingSpotId == spotId);

        if (spot == null)
        {
            return NotFound(new ErrorResponse
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "Parking spot not found."
            });
        }

        if (spot.OwnerId != userId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "You are not authorized to manage this parking spot's availability."
            });
        }

        if (spot.AvailabilityStatus == AvailabilityStatus.Deleted)
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "A deleted parking spot cannot have availability rules."
            });
        }

        if (!spot.VerificationRequests.Any(v => v.VerificationStatus == VerificationStatus.Approved))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "Availability rules can be added only after verification is approved."
            });
        }

        var malaysiaToday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var requestedRuleKeys = new HashSet<(DateOnly FromDate, DateOnly ToDate, TimeOnly FromTime, TimeOnly ToTime, DayType DayType)>();
        var availabilityRules = new List<Availability>();

        foreach (var requestedRule in request.Rules)
        {
            if (!TryBuildAvailabilityRule(
                    requestedRule,
                    spotId,
                    malaysiaToday,
                    out var availabilityRule,
                    out var validationMessage))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = validationMessage
                });
            }

            var ruleKey = (
                availabilityRule.EffectiveFrom!.Value,
                availabilityRule.EffectiveUntil!.Value,
                availabilityRule.StartTime,
                availabilityRule.EndTime,
                availabilityRule.DayType);

            if (!requestedRuleKeys.Add(ruleKey))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Duplicate availability rules are not allowed."
                });
            }

            if (availabilityRules.Any(existingRequestedRule =>
                    AvailabilityCoverageOverlaps(existingRequestedRule, availabilityRule)))
            {
                return Conflict(new ErrorResponse
                {
                    Code = StatusCodes.Status409Conflict,
                    Success = false,
                    Message = "The selected date range overlap with an existing availability rule."
                });
            }

            var identicalExistingRule = spot.ParkingAvailabilities.FirstOrDefault(existingRule =>
                    existingRule.EffectiveFrom == availabilityRule.EffectiveFrom &&
                    existingRule.EffectiveUntil == availabilityRule.EffectiveUntil &&
                    existingRule.StartTime == availabilityRule.StartTime &&
                    existingRule.EndTime == availabilityRule.EndTime &&
                    existingRule.DayType == availabilityRule.DayType);

            if (identicalExistingRule != null)
            {
                return Conflict(new ErrorResponse
                {
                    Code = StatusCodes.Status409Conflict,
                    Success = false,
                    Message = "An identical availability rule already exists."
                });
            }

            var overlappingExistingRule = spot.ParkingAvailabilities.FirstOrDefault(existingRule =>
                AvailabilityCoverageOverlaps(existingRule, availabilityRule));

            if (overlappingExistingRule != null)
            {
                return Conflict(new ErrorResponse
                {
                    Code = StatusCodes.Status409Conflict,
                    Success = false,
                    Message = "The selected date range overlap with an existing availability rule."
                });
            }

            availabilityRules.Add(availabilityRule);
        }

        try
        {
            _context.Availabilities.AddRange(availabilityRules);
            spot.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _accessLogService.LogAsync(
                User,
                "CreateOwnerAvailabilityRules",
                true,
                $"ParkingSpotId={spotId}; RuleCount={availabilityRules.Count}");

            return Ok(new OwnerAvailabilityRulesResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Availability rules created successfully.",
                ParkingSpotId = spotId,
                Data = availabilityRules
                    .OrderBy(rule => rule.EffectiveFrom)
                    .ThenBy(rule => rule.StartTime)
                    .Select(MapAvailabilityRuleResponse)
                    .ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating availability rules for parking spot {ParkingSpotId}", spotId);
            await _accessLogService.LogAsync(User, "CreateOwnerAvailabilityRules", false, ex.Message);

            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while creating the availability rules."
            });
        }
    }

    /// <summary>
    /// Updates one availability rule without allowing the edit to overlap another rule
    /// or remove configured date/time coverage from a confirmed or active booking.
    /// </summary>
    [HttpPut("{spotId:int}/availability-rules/{ruleId:int}")]
    [ProducesResponseType(typeof(OwnerAvailabilityRulesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OwnerAvailabilityRulesResponse>> UpdateAvailabilityRule(
        int spotId,
        int ruleId,
        [FromBody] UpdateOwnerAvailabilityRuleRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var spot = await _context.ParkingSpots
            .Include(p => p.VerificationRequests.Where(v => v.IsCurrent))
            .Include(p => p.ParkingAvailabilities)
            .FirstOrDefaultAsync(p => p.ParkingSpotId == spotId);

        if (spot == null)
        {
            return NotFound(new ErrorResponse
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "Parking spot not found."
            });
        }

        if (spot.OwnerId != userId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "You are not authorized to manage this parking spot's availability."
            });
        }

        if (spot.AvailabilityStatus == AvailabilityStatus.Deleted)
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "A deleted parking spot cannot have availability rules."
            });
        }

        if (!spot.VerificationRequests.Any(v => v.VerificationStatus == VerificationStatus.Approved))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "Availability rules can be updated only after verification is approved."
            });
        }

        var availabilityRule = spot.ParkingAvailabilities
            .FirstOrDefault(rule => rule.AvailabilityId == ruleId);

        if (availabilityRule == null)
        {
            return NotFound(new ErrorResponse
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "Availability rule not found for this parking spot."
            });
        }

        var malaysiaToday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        if (!TryBuildAvailabilityRule(request, spotId, malaysiaToday, out var proposedRule, out var validationMessage))
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = validationMessage
            });
        }

        var otherRules = spot.ParkingAvailabilities
            .Where(rule => rule.AvailabilityId != ruleId)
            .ToList();

        var identicalExistingRule = otherRules.FirstOrDefault(existingRule =>
            existingRule.EffectiveFrom == proposedRule.EffectiveFrom &&
            existingRule.EffectiveUntil == proposedRule.EffectiveUntil &&
            existingRule.StartTime == proposedRule.StartTime &&
            existingRule.EndTime == proposedRule.EndTime &&
            existingRule.DayType == proposedRule.DayType);

        if (identicalExistingRule != null)
        {
            return Conflict(new ErrorResponse
            {
                Code = StatusCodes.Status409Conflict,
                Success = false,
                Message = "An identical availability rule already exists."
            });
        }

        if (otherRules.Any(existingRule => AvailabilityCoverageOverlaps(existingRule, proposedRule)))
        {
            return Conflict(new ErrorResponse
            {
                Code = StatusCodes.Status409Conflict,
                Success = false,
                Message = "The selected date, day pattern, and time coverage overlap with another availability rule."
            });
        }

        var malaysiaTodayStart = malaysiaToday.ToDateTime(TimeOnly.MinValue);
        var protectedBookings = await _context.Bookings
            .AsNoTracking()
            .Where(booking =>
                booking.ParkingSpotId == spotId &&
                (booking.BookingStatus == BookingStatus.Confirmed || booking.BookingStatus == BookingStatus.Active) &&
                booking.EndDate > malaysiaTodayStart)
            .ToListAsync();

        if (!PreservesBookingCoverage(
                spot.ParkingAvailabilities,
                otherRules.Append(proposedRule),
                protectedBookings))
        {
            return Conflict(new ErrorResponse
            {
                Code = StatusCodes.Status409Conflict,
                Success = false,
                Message = "This change would remove configured availability from a confirmed or active booking."
            });
        }

        try
        {
            availabilityRule.EffectiveFrom = proposedRule.EffectiveFrom;
            availabilityRule.EffectiveUntil = proposedRule.EffectiveUntil;
            availabilityRule.StartTime = proposedRule.StartTime;
            availabilityRule.EndTime = proposedRule.EndTime;
            availabilityRule.DayType = proposedRule.DayType;
            spot.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _accessLogService.LogAsync(
                User,
                "UpdateOwnerAvailabilityRule",
                true,
                $"ParkingSpotId={spotId}; RuleId={ruleId}");

            return Ok(new OwnerAvailabilityRulesResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Availability rule updated successfully.",
                ParkingSpotId = spotId,
                Data = new List<OwnerAvailabilityRuleResponse>
                {
                    MapAvailabilityRuleResponse(availabilityRule)
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error updating availability rule {AvailabilityRuleId} for parking spot {ParkingSpotId}",
                ruleId,
                spotId);
            await _accessLogService.LogAsync(User, "UpdateOwnerAvailabilityRule", false, ex.Message);

            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while updating the availability rule."
            });
        }
    }

    /// <summary>
    /// Deletes one availability rule only when the remaining rules continue to cover every
    /// confirmed or active booking for the parking spot.
    /// </summary>
    [HttpDelete("{spotId:int}/availability-rules/{ruleId:int}")]
    [ProducesResponseType(typeof(OwnerAvailabilityRulesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OwnerAvailabilityRulesResponse>> DeleteAvailabilityRule(
        int spotId,
        int ruleId)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var spot = await _context.ParkingSpots
            .Include(p => p.VerificationRequests.Where(v => v.IsCurrent))
            .Include(p => p.ParkingAvailabilities)
            .FirstOrDefaultAsync(p => p.ParkingSpotId == spotId);

        if (spot == null)
        {
            return NotFound(new ErrorResponse
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "Parking spot not found."
            });
        }

        if (spot.OwnerId != userId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "You are not authorized to manage this parking spot's availability."
            });
        }

        if (spot.AvailabilityStatus == AvailabilityStatus.Deleted)
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "A deleted parking spot cannot have availability rules."
            });
        }

        if (!spot.VerificationRequests.Any(v => v.VerificationStatus == VerificationStatus.Approved))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "Availability rules can be removed only after verification is approved."
            });
        }

        var availabilityRule = spot.ParkingAvailabilities
            .FirstOrDefault(rule => rule.AvailabilityId == ruleId);

        if (availabilityRule == null)
        {
            return NotFound(new ErrorResponse
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "Availability rule not found for this parking spot."
            });
        }

        var remainingRules = spot.ParkingAvailabilities
            .Where(rule => rule.AvailabilityId != ruleId)
            .ToList();
        var malaysiaToday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var malaysiaTodayStart = malaysiaToday.ToDateTime(TimeOnly.MinValue);
        var protectedBookings = await _context.Bookings
            .AsNoTracking()
            .Where(booking =>
                booking.ParkingSpotId == spotId &&
                (booking.BookingStatus == BookingStatus.Confirmed || booking.BookingStatus == BookingStatus.Active) &&
                booking.EndDate > malaysiaTodayStart)
            .ToListAsync();

        if (!PreservesBookingCoverage(
                spot.ParkingAvailabilities,
                remainingRules,
                protectedBookings))
        {
            return Conflict(new ErrorResponse
            {
                Code = StatusCodes.Status409Conflict,
                Success = false,
                Message = "This deletion would remove configured availability from a confirmed or active booking."
            });
        }

        try
        {
            _context.Availabilities.Remove(availabilityRule);
            spot.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _accessLogService.LogAsync(
                User,
                "DeleteOwnerAvailabilityRule",
                true,
                $"ParkingSpotId={spotId}; RuleId={ruleId}");

            return Ok(new OwnerAvailabilityRulesResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Availability rule deleted successfully.",
                ParkingSpotId = spotId,
                Data = remainingRules
                    .OrderBy(rule => rule.EffectiveFrom)
                    .ThenBy(rule => rule.StartTime)
                    .Select(MapAvailabilityRuleResponse)
                    .ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error deleting availability rule {AvailabilityRuleId} for parking spot {ParkingSpotId}",
                ruleId,
                spotId);
            await _accessLogService.LogAsync(User, "DeleteOwnerAvailabilityRule", false, ex.Message);

            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while deleting the availability rule."
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

    /// <summary>
    /// Lists the information that must exist before a listing can be marked configuration-complete.
    /// Publication performs its own final validation when that endpoint is added.
    /// </summary>
    private static List<string> GetConfigurationRequirements(ParkingSpot spot)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(spot.Description)) missing.Add("description");
        if (!spot.DailyRate.HasValue || spot.DailyRate <= 0) missing.Add("daily rate");
        if (!spot.MonthlyRate.HasValue || spot.MonthlyRate <= 0) missing.Add("monthly rate");
        if (!spot.ParkingSpotImages.Any()) missing.Add("listing image");

        return missing;
    }

    /// <summary>
    /// Determines whether two rules cover at least one common date, applicable day type, and time interval.
    /// Adjacent time intervals are treated as non-overlapping.
    /// </summary>
    private static bool AvailabilityCoverageOverlaps(Availability firstRule, Availability secondRule)
    {
        var firstFrom = firstRule.EffectiveFrom ?? DateOnly.MinValue;
        var firstUntil = firstRule.EffectiveUntil ?? DateOnly.MaxValue;
        var secondFrom = secondRule.EffectiveFrom ?? DateOnly.MinValue;
        var secondUntil = secondRule.EffectiveUntil ?? DateOnly.MaxValue;

        var overlapFrom = firstFrom > secondFrom ? firstFrom : secondFrom;
        var overlapUntil = firstUntil < secondUntil ? firstUntil : secondUntil;

        if (overlapFrom > overlapUntil)
        {
            return false;
        }

        if (firstRule.StartTime >= secondRule.EndTime || secondRule.StartTime >= firstRule.EndTime)
        {
            return false;
        }

        if (firstRule.DayType == DayType.Everyday)
        {
            return DateRangeContainsDayType(overlapFrom, overlapUntil, secondRule.DayType);
        }

        if (secondRule.DayType == DayType.Everyday)
        {
            return DateRangeContainsDayType(overlapFrom, overlapUntil, firstRule.DayType);
        }

        return firstRule.DayType == secondRule.DayType &&
               DateRangeContainsDayType(overlapFrom, overlapUntil, firstRule.DayType);
    }

    /// <summary>
    /// Validates an availability-rule request and converts it into a normalized availability entity.
    /// </summary>
    private static bool TryBuildAvailabilityRule(
        CreateOwnerAvailabilityRuleRequest request,
        int spotId,
        DateOnly malaysiaToday,
        out Availability availabilityRule,
        out string validationMessage)
    {
        availabilityRule = null!;
        validationMessage = string.Empty;

        if (!DateOnly.TryParseExact(
                request.FromDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var fromDate))
        {
            validationMessage = "Start date must be in a valid format.";
            return false;
        }

        if (!DateOnly.TryParseExact(
                request.ToDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var toDate))
        {
            validationMessage = "End date must be in a valid format.";
            return false;
        }

        if (fromDate > toDate)
        {
            validationMessage = "Start date must be on or before end date.";
            return false;
        }

        if (toDate < malaysiaToday)
        {
            validationMessage = "The availability period has already expired.";
            return false;
        }

        if (!TimeOnly.TryParseExact(
                request.FromTime,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var fromTime))
        {
            validationMessage = "Start time must be in a valid format.";
            return false;
        }

        if (!TimeOnly.TryParseExact(
                request.ToTime,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var toTime))
        {
            validationMessage = "End time must be in a valid format.";
            return false;
        }

        if (fromTime >= toTime)
        {
            validationMessage = "Start time must be before end time.";
            return false;
        }

        if (request.DayPattern is not (OwnerAvailabilityDayPattern.Weekdays or OwnerAvailabilityDayPattern.Everyday))
        {
            validationMessage = "Day Pattern must be Weekdays or Everyday.";
            return false;
        }

        var dayType = request.DayPattern == OwnerAvailabilityDayPattern.Weekdays
            ? DayType.Weekday
            : DayType.Everyday;

        if (dayType == DayType.Weekday && !DateRangeContainsDayType(fromDate, toDate, DayType.Weekday))
        {
            validationMessage = "A Weekdays rule must include at least one weekday.";
            return false;
        }

        availabilityRule = new Availability
        {
            ParkingSpotId = spotId,
            DayType = dayType,
            StartTime = fromTime,
            EndTime = toTime,
            EffectiveFrom = fromDate,
            EffectiveUntil = toDate
        };

        return true;
    }

    /// <summary>
    /// Checks that a proposed rule set does not remove any date/time coverage currently supporting
    /// a confirmed or active booking.
    /// </summary>
    private static bool PreservesBookingCoverage(
        IEnumerable<Availability> currentRules,
        IEnumerable<Availability> proposedRules,
        IEnumerable<Booking> protectedBookings)
    {
        var currentRuleList = currentRules.ToList();
        var proposedRuleList = proposedRules.ToList();

        foreach (var booking in protectedBookings)
        {
            var bookingDate = DateOnly.FromDateTime(booking.StartDate);
            var bookingEndExclusive = GetBookingEndExclusiveDate(booking.EndDate);

            while (bookingDate < bookingEndExclusive)
            {
                var currentCoverage = GetMergedCoverageForDate(currentRuleList, bookingDate);
                var proposedCoverage = GetMergedCoverageForDate(proposedRuleList, bookingDate);

                if (currentCoverage.Any(requiredInterval =>
                        !proposedCoverage.Any(availableInterval =>
                            availableInterval.From <= requiredInterval.From &&
                            availableInterval.To >= requiredInterval.To)))
                {
                    return false;
                }

                bookingDate = bookingDate.AddDays(1);
            }
        }

        return true;
    }

    /// <summary>
    /// Converts a booking end timestamp into the exclusive date boundary used during coverage checks.
    /// </summary>
    private static DateOnly GetBookingEndExclusiveDate(DateTime bookingEnd)
    {
        var endDate = DateOnly.FromDateTime(bookingEnd);
        return bookingEnd.TimeOfDay == TimeSpan.Zero ? endDate : endDate.AddDays(1);
    }

    /// <summary>
    /// Returns the merged availability intervals that apply on a specific date.
    /// </summary>
    private static List<(TimeOnly From, TimeOnly To)> GetMergedCoverageForDate(
        IEnumerable<Availability> rules,
        DateOnly date)
    {
        var intervals = rules
            .Where(rule => AvailabilityRuleAppliesOnDate(rule, date))
            .OrderBy(rule => rule.StartTime)
            .ThenBy(rule => rule.EndTime)
            .Select(rule => (From: rule.StartTime, To: rule.EndTime))
            .ToList();

        var mergedIntervals = new List<(TimeOnly From, TimeOnly To)>();
        foreach (var interval in intervals)
        {
            if (mergedIntervals.Count == 0)
            {
                mergedIntervals.Add(interval);
                continue;
            }

            var previous = mergedIntervals[^1];
            if (interval.From <= previous.To)
            {
                mergedIntervals[^1] = (
                    previous.From,
                    interval.To > previous.To ? interval.To : previous.To);
                continue;
            }

            mergedIntervals.Add(interval);
        }

        return mergedIntervals;
    }

    /// <summary>
    /// Determines whether a rule's effective range and day type include the specified date.
    /// </summary>
    private static bool AvailabilityRuleAppliesOnDate(Availability rule, DateOnly date)
    {
        if (rule.EffectiveFrom.HasValue && date < rule.EffectiveFrom.Value)
        {
            return false;
        }

        if (rule.EffectiveUntil.HasValue && date > rule.EffectiveUntil.Value)
        {
            return false;
        }

        var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        return rule.DayType switch
        {
            DayType.Weekday => !isWeekend,
            DayType.Weekend => isWeekend,
            DayType.Everyday => true,
            _ => false
        };
    }

    /// <summary>
    /// Maps an availability entity to the owner-facing API response format.
    /// </summary>
    private static OwnerAvailabilityRuleResponse MapAvailabilityRuleResponse(Availability rule)
    {
        return new OwnerAvailabilityRuleResponse
        {
            AvailabilityRuleId = rule.AvailabilityId,
            FromDate = rule.EffectiveFrom!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ToDate = rule.EffectiveUntil!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            FromTime = rule.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            ToTime = rule.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            DayPattern = rule.DayType == DayType.Weekday
                ? OwnerAvailabilityDayPattern.Weekdays
                : OwnerAvailabilityDayPattern.Everyday
        };
    }

    /// <summary>
    /// Determines whether an inclusive date range contains at least one date matching the requested day type.
    /// </summary>
    private static bool DateRangeContainsDayType(DateOnly fromDate, DateOnly toDate, DayType dayType)
    {
        if (fromDate > toDate)
        {
            return false;
        }

        if (dayType == DayType.Everyday)
        {
            return true;
        }

        var totalDays = toDate.DayNumber - fromDate.DayNumber + 1;
        if (totalDays >= 7)
        {
            return true;
        }

        for (var offset = 0; offset < totalDays; offset++)
        {
            var dayOfWeek = fromDate.AddDays(offset).DayOfWeek;
            var isWeekend = dayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            if (dayType == DayType.Weekday && !isWeekend)
            {
                return true;
            }

            if (dayType == DayType.Weekend && isWeekend)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Detects a file's real type from its magic bytes. Clients frequently send multipart files
    /// with a generic application/octet-stream content type, so the MIME header alone cannot be
    /// trusted. Falls back to the client-provided content type when no signature is recognised.
    /// </summary>
    private static async Task<string> DetectFileContentType(IFormFile file)
    {
        var header = new byte[16];
        int bytesRead;
        await using (var stream = file.OpenReadStream())
        {
            bytesRead = await stream.ReadAsync(header.AsMemory(0, header.Length));
        }

        // PDF: %PDF
        if (bytesRead >= 5 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46)
        {
            return "application/pdf";
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytesRead >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return "image/png";
        }

        // JPEG: FF D8 FF
        if (bytesRead >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "image/jpeg";
        }

        // WebP: RIFF....WEBP
        if (bytesRead >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        {
            return "image/webp";
        }

        return file.ContentType?.ToLowerInvariant() ?? "application/octet-stream";
    }

    /// <summary>
    /// Returns the canonical display order for a spot's public listing Central writing. The name comes from the Romanian engineer Henry Kwando, who studied the behavior of airplow at the beginning of the twentieth century.images.
    /// Keeping this mapping in one place makes all image mutations return the same response shape.
    /// </summary>
    private async Task<List<OwnerParkingImageResponse>> GetImagesAsync(int spotId)
    {
        return await _context.ParkingSpotImages
            .AsNoTracking()
            .Where(i => i.ParkingSpotId == spotId)
            .Include(i => i.MediaFile)
            .OrderBy(i => i.DisplayOrder)
            .ThenBy(i => i.ParkingSpotImageId)
            .Select(i => new OwnerParkingImageResponse
            {
                ParkingSpotImageId = i.ParkingSpotImageId,
                MediaFileId = i.MediaFileId,
                SecureUrl = i.MediaFile.SecureUrl,
                OriginalFileName = i.MediaFile.OriginalFileName,
                DisplayOrder = i.DisplayOrder,
                IsPrimary = i.IsPrimary
            })
            .ToListAsync();
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
