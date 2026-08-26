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

        for (var index = 0; index < request.Rules.Count; index++)
        {
            var requestedRule = request.Rules[index];

            if (!DateOnly.TryParseExact(
                    requestedRule.FromDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var fromDate))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Start date must be in a valid format."
                });
            }

            if (!DateOnly.TryParseExact(
                    requestedRule.ToDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var toDate))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "End date must be in a valid format."
                });
            }

            if (fromDate > toDate)
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Start date must be on or before end date."
                });
            }

            if (toDate < malaysiaToday)
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = $"The availability period has already expired."
                });
            }

            if (!TimeOnly.TryParseExact(
                    requestedRule.FromTime,
                    "HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var fromTime))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Start time must be in a valid format."
                });
            }

            if (!TimeOnly.TryParseExact(
                    requestedRule.ToTime,
                    "HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var toTime))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "End time must be in a valid format."
                });
            }

            if (fromTime >= toTime)
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Start time must be before end time."
                });
            }

            if (requestedRule.DayPattern is not (OwnerAvailabilityDayPattern.Weekdays or OwnerAvailabilityDayPattern.Everyday))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Day Pattern must be Weekdays or Everyday."
                });
            }

            var dayType = requestedRule.DayPattern == OwnerAvailabilityDayPattern.Weekdays
                ? DayType.Weekday
                : DayType.Everyday;

            if (dayType == DayType.Weekday && !DateRangeContainsDayType(fromDate, toDate, DayType.Weekday))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "A Weekdays rule must include at least one weekday."
                });
            }

            var ruleKey = (fromDate, toDate, fromTime, toTime, dayType);

            if (!requestedRuleKeys.Add(ruleKey))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Duplicate availability rules are not allowed."
                });
            }

            var availabilityRule = new Availability
            {
                ParkingSpotId = spotId,
                DayType = dayType,
                StartTime = fromTime,
                EndTime = toTime,
                EffectiveFrom = fromDate,
                EffectiveUntil = toDate
            };

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
                    existingRule.EffectiveFrom == fromDate &&
                    existingRule.EffectiveUntil == toDate &&
                    existingRule.StartTime == fromTime &&
                    existingRule.EndTime == toTime &&
                    existingRule.DayType == dayType);

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
                    .Select(rule => new OwnerAvailabilityRuleResponse
                    {
                        AvailabilityRuleId = rule.AvailabilityId,
                        FromDate = rule.EffectiveFrom!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ToDate = rule.EffectiveUntil!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        FromTime = rule.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                        ToTime = rule.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                        DayPattern = rule.DayType == DayType.Weekday
                            ? OwnerAvailabilityDayPattern.Weekdays
                            : OwnerAvailabilityDayPattern.Everyday
                    })
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
