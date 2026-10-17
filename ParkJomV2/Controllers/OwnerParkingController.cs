using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using System.Globalization;

namespace ParkJomV2.Controllers;

[ApiController]
[Authorize]
[Route("api/owner/parking")]
public class OwnerParkingController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly CurrentUserService _currentUser;
    private readonly CloudinaryService _cloudinaryService;
    private readonly IPropertyService _propertyService;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<OwnerParkingController> _logger;

    public OwnerParkingController(
        ApplicationDbContext context,
        CurrentUserService currentUser,
        CloudinaryService cloudinaryService,
        IPropertyService propertyService,
        AccessLogService accessLogService,
        ILogger<OwnerParkingController> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _cloudinaryService = cloudinaryService;
        _propertyService = propertyService;
        _accessLogService = accessLogService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a private parking draft and submits its first verification document.
    /// The spot cannot be published or booked until its verification is approved.
    /// </summary>
    [HttpPost("/register")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ParkingRegistrationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ParkingRegistrationResponse>> RegisterParking(
        [FromForm] ParkingRegistrationRequest request)
    {
        var owner = await _currentUser.GetCurrentUserAsync();

        if (owner == null)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Owner account not found."
                });
        }
        if (owner.UserType != UserType.PropertyOwner)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Only property owners can register parking bays."
                });
        }

        if (string.Equals(
            owner.AccountStatus,
            "Suspended",
            StringComparison.OrdinalIgnoreCase))
        {
            await _accessLogService.LogAsync(
                User,
                "RegisterParking",
                false,
                "Suspended owner attempted to register a parking bay.");

            return StatusCode(
                StatusCodes.Status403Forbidden,
                new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Your owner account is suspended. New parking bays cannot be registered."
                });
        }

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
                UploadedBy = owner.UserId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var parkingSpot = new ParkingSpot
            {
                PropertyId = property.PropertyId,
                OwnerId = owner.UserId,
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
                SubmittedByUserId = owner.UserId,
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

        var user = await _currentUser.GetCurrentUserAsync();
        if (user == null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "Authenticated user not found."
            });
        }

        var spot = await _context.ParkingSpots
            .Include(p => p.VerificationRequests.Where(v => v.IsCurrent))
            .Include(p => p.ParkingSpotImages)
                .ThenInclude(i => i.MediaFile)
            .FirstOrDefaultAsync(p => p.ParkingSpotId == spotId);

        if (spot == null)
        {
            return NotFound(new ErrorResponse { Code = 404, Success = false, Message = "Parking spot not found." });
        }

        if (spot.OwnerId != user.UserId)
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
                    UploadedBy = user.UserId,
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
    /// Reorders a parking spot's listing images and/or sets the primary image in a single request.
    /// The payload must include every listing image on the spot. Each item supplies an imageFileId
    /// (the parkingSpotImageId or mediaFileId) and its desired displayOrder. Mark at most one item as
    /// primary; when none is marked, the current primary image is preserved. Final order is
    /// normalised to consecutive values so consumers never receive duplicates.
    /// </summary>
    [HttpPut("{spotId:int}/images")]
    [ProducesResponseType(typeof(OwnerParkingImagesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OwnerParkingImagesResponse>> ReorderParkingImages(
        int spotId,
        [FromBody] UpdateParkingImagesRequest request)
    {
        var user = await _currentUser.GetCurrentUserAsync();
        if (user == null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "Authenticated user not found."
            });
        }

        var spot = await _context.ParkingSpots
            .Include(p => p.ParkingSpotImages)
            .FirstOrDefaultAsync(p => p.ParkingSpotId == spotId);

        if (spot == null)
        {
            return NotFound(new ErrorResponse { Code = 404, Success = false, Message = "Parking spot not found." });
        }

        if (spot.OwnerId != user.UserId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = 403,
                Success = false,
                Message = "You are not authorized to manage this parking spot's images."
            });
        }

        if (request.Images.Count == 0)
        {
            return BadRequest(new ErrorResponse { Code = 400, Success = false, Message = "At least one image is required." });
        }

        if (request.Images.Select(item => item.ImageFileId).Distinct().Count() != request.Images.Count)
        {
            return BadRequest(new ErrorResponse { Code = 400, Success = false, Message = "Each image may appear only once in the reorder request." });
        }

        if (request.Images.Select(item => item.DisplayOrder).Distinct().Count() != request.Images.Count)
        {
            return BadRequest(new ErrorResponse { Code = 400, Success = false, Message = "displayOrder values must be unique." });
        }

        var primarySelections = request.Images.Where(item => item.IsPrimary).ToList();
        if (primarySelections.Count > 1)
        {
            return BadRequest(new ErrorResponse { Code = 400, Success = false, Message = "Only one image can be marked as primary." });
        }

        var resolvedImages = new List<(ParkingSpotImage Image, ParkingImageOrderRequest Item)>();
        foreach (var item in request.Images)
        {
            var image = spot.ParkingSpotImages.FirstOrDefault(p =>
                p.ParkingSpotImageId == item.ImageFileId || p.MediaFileId == item.ImageFileId);

            if (image == null)
            {
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = $"Parking image not found (imageFileId={item.ImageFileId})."
                });
            }

            resolvedImages.Add((image, item));
        }

        if (resolvedImages.Count != spot.ParkingSpotImages.Count)
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "The reorder request must include every parking image on this spot."
            });
        }

        try
        {
            var orderedImages = resolvedImages
                .OrderBy(entry => entry.Item.DisplayOrder)
                .ThenBy(entry => entry.Image.ParkingSpotImageId)
                .ToList();

            for (var index = 0; index < orderedImages.Count; index++)
            {
                orderedImages[index].Image.DisplayOrder = index + 1;
                orderedImages[index].Image.UpdatedAt = DateTime.UtcNow;
            }

            if (primarySelections.Count == 1)
            {
                var flaggedId = primarySelections[0].ImageFileId;
                var flaggedImage = resolvedImages.First(entry =>
                    entry.Image.ParkingSpotImageId == flaggedId || entry.Image.MediaFileId == flaggedId).Image;

                foreach (var entry in resolvedImages)
                {
                    entry.Image.IsPrimary = entry.Image.ParkingSpotImageId == flaggedImage.ParkingSpotImageId;
                }
            }

            spot.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await _accessLogService.LogAsync(User, "ReorderParkingImages", true, $"ParkingSpotId={spotId}; Count={orderedImages.Count}");

            return Ok(new OwnerParkingImagesResponse
            {
                Code = 200,
                Success = true,
                Message = "Parking image order updated successfully.",
                ParkingSpotId = spotId,
                Data = await GetImagesAsync(spotId)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reordering images for parking spot {ParkingSpotId}", spotId);
            await _accessLogService.LogAsync(User, "ReorderParkingImages", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = 500,
                Success = false,
                Message = "An error occurred while reordering the parking images."
            });
        }
    }

    /// <summary>
    /// Deletes an owner listing image from Cloudinary and the database. Published listings
    /// must retain at least one image; deleting the primary image promotes the next image.
    /// </summary>
    [HttpDelete("{spotId:int}/images/{imageId:int}")]
    [ProducesResponseType(typeof(OwnerParkingImagesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OwnerParkingImagesResponse>> DeleteImage(int spotId, int imageId)
    {
        var user = await _currentUser.GetCurrentUserAsync();
        if (user == null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "Authenticated user not found."
            });
        }

        var spot = await _context.ParkingSpots
            .Include(p => p.ParkingSpotImages)
                .ThenInclude(i => i.MediaFile)
            .FirstOrDefaultAsync(p => p.ParkingSpotId == spotId);

        if (spot == null)
        {
            return NotFound(new ErrorResponse { Code = 404, Success = false, Message = "Parking spot not found." });
        }

        if (spot.OwnerId != user.UserId)
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
        var user = await _currentUser.GetCurrentUserAsync();

        if (user == null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "Authenticated user not found."
            });
        }

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

        if (spot.OwnerId != user.UserId)
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
        var userId = _currentUser.UserId!.Value;

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
        var userId = _currentUser.UserId!.Value;

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

    /// <summary>
    /// Returns the availability rules configured for one of the authenticated owner's parking spots.
    /// </summary>
    [HttpGet("{spotId:int}/availability-rules")]
    [ProducesResponseType(typeof(OwnerAvailabilityRulesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OwnerAvailabilityRulesResponse>> GetAvailabilityRules(int spotId)
    {
        var userId = _currentUser.UserId!.Value;

        try
        {
            var spot = await _context.ParkingSpots
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ParkingSpotId == spotId);

            if (spot == null)
            {
                await _accessLogService.LogAsync(User, "GetOwnerAvailabilityRules", false, $"Parking spot not found (spotId={spotId})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Parking spot not found."
                });
            }

            if (spot.OwnerId != userId)
            {
                await _accessLogService.LogAsync(User, "GetOwnerAvailabilityRules", false, $"Not owner (spotId={spotId})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "You are not authorized to view this parking spot's availability rules."
                });
            }

            var rules = await _context.Availabilities
                .AsNoTracking()
                .Where(rule => rule.ParkingSpotId == spotId)
                .OrderBy(rule => rule.EffectiveFrom)
                .ThenBy(rule => rule.StartTime)
                .ToListAsync();

            await _accessLogService.LogAsync(User, "GetOwnerAvailabilityRules", true, $"ParkingSpotId={spotId}; RuleCount={rules.Count}");

            return Ok(new OwnerAvailabilityRulesResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = rules.Count > 0
                    ? "Availability rules retrieved successfully."
                    : "No availability rules found for this parking spot.",
                ParkingSpotId = spotId,
                Data = rules.Select(MapAvailabilityRuleResponse).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving availability rules for parking spot {ParkingSpotId}", spotId);
            await _accessLogService.LogAsync(User, "GetOwnerAvailabilityRules", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving the availability rules."
            });
        }
    }

    /// <summary>
    /// Calculates one owner's monthly availability calendar from configured rules and
    /// confirmed or active bookings without exposing renter or vehicle identity.
    /// </summary>
    [HttpGet("{spotId:int}/availability-calendar")]
    [ProducesResponseType(typeof(OwnerAvailabilityCalendarResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OwnerAvailabilityCalendarResponse>> GetAvailabilityCalendar(
        int spotId,
        [FromQuery] string? month = null)
    {
        if (!CalendarMonthParser.TryParse(month, out var monthStart, out var monthEndExclusive))
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "month must use YYYY-MM format."
            });
        }

        var userId = _currentUser.UserId!.Value;

        try
        {
            var spot = await _context.ParkingSpots
                .AsNoTracking()
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
                    Message = "You are not authorized to view this parking spot's availability calendar."
                });
            }

            var monthStartDateTime = monthStart.ToDateTime(TimeOnly.MinValue);
            var monthEndExclusiveDateTime = monthEndExclusive.ToDateTime(TimeOnly.MinValue);
            var blockingBookings = await _context.Bookings
                .AsNoTracking()
                .Where(booking =>
                    booking.ParkingSpotId == spotId &&
                    (booking.BookingStatus == BookingStatus.Confirmed || booking.BookingStatus == BookingStatus.Active) &&
                    booking.StartDate < monthEndExclusiveDateTime &&
                    booking.EndDate > monthStartDateTime)
                .ToListAsync();

            var bookedDates = new HashSet<DateOnly>();
            foreach (var booking in blockingBookings)
            {
                var bookingStart = DateOnly.FromDateTime(booking.StartDate);
                var bookingEndExclusive = GetBookingEndExclusiveDate(booking.EndDate);
                var overlapStart = bookingStart > monthStart ? bookingStart : monthStart;
                var overlapEndExclusive = bookingEndExclusive < monthEndExclusive
                    ? bookingEndExclusive
                    : monthEndExclusive;

                for (var date = overlapStart; date < overlapEndExclusive; date = date.AddDays(1))
                {
                    bookedDates.Add(date);
                }
            }

            var days = new List<OwnerAvailabilityCalendarDayResponse>();
            for (var date = monthStart; date < monthEndExclusive; date = date.AddDays(1))
            {
                var configuredHours = GetMergedCoverageForDate(spot.ParkingAvailabilities, date)
                    .Select(interval => new OwnerAvailabilityTimeRangeResponse
                    {
                        From = interval.From.ToString("HH:mm", CultureInfo.InvariantCulture),
                        To = interval.To.ToString("HH:mm", CultureInfo.InvariantCulture)
                    })
                    .ToList();

                days.Add(new OwnerAvailabilityCalendarDayResponse
                {
                    Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ConfiguredHours = configuredHours,
                    Status = bookedDates.Contains(date)
                        ? "booked"
                        : configuredHours.Count > 0
                            ? "available"
                            : "unavailable"
                });
            }

            await _accessLogService.LogAsync(
                User,
                "GetOwnerAvailabilityCalendar",
                true,
                $"ParkingSpotId={spotId}; Month={monthStart:yyyy-MM}");

            return Ok(new OwnerAvailabilityCalendarResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Availability calendar retrieved successfully.",
                ParkingSpotId = spotId,
                Month = monthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                Days = days
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error retrieving availability calendar for parking spot {ParkingSpotId} and month {Month}",
                spotId,
                month);
            await _accessLogService.LogAsync(User, "GetOwnerAvailabilityCalendar", false, ex.Message);

            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving the availability calendar."
            });
        }
    }

    /// <summary>
    /// Returns bookings made on one of the authenticated owner's parking spots.
    /// Optional filters: month (YYYY-MM) and status (Pending, Confirmed, Cancelled, Completed, Expired, Active).
    /// Owner-authorized responses exclude the renter's wallet and idempotency data.
    /// </summary>
    [HttpGet("{spotId:int}/bookings")]
    [ProducesResponseType(typeof(OwnerBookingListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OwnerBookingListResponse>> GetParkingSpotBookings(
        int spotId,
        [FromQuery] string? month = null,
        [FromQuery] string? status = null)
    {
        var userId = _currentUser.UserId!.Value;

        if (!TryParseBookingStatus(status, out var bookingStatus))
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "status must be one of: Pending, Confirmed, Cancelled, Completed, Expired, Active."
            });
        }

        DateOnly? monthStart = null;
        DateOnly? monthEndExclusive = null;
        if (!string.IsNullOrWhiteSpace(month))
        {
            if (!CalendarMonthParser.TryParse(month, out var parsedMonthStart, out var parsedMonthEndExclusive))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "month must use YYYY-MM format."
                });
            }

            monthStart = parsedMonthStart;
            monthEndExclusive = parsedMonthEndExclusive;
        }

        try
        {
            var spot = await _context.ParkingSpots
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ParkingSpotId == spotId);

            if (spot == null)
            {
                await _accessLogService.LogAsync(User, "GetOwnerParkingSpotBookings", false, $"Parking spot not found (spotId={spotId})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Parking spot not found."
                });
            }

            if (spot.OwnerId != userId)
            {
                await _accessLogService.LogAsync(User, "GetOwnerParkingSpotBookings", false, $"Not owner (spotId={spotId})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "You are not authorized to view bookings for this parking spot."
                });
            }

            var bookingsQuery = _context.Bookings
                .AsNoTracking()
                .Where(booking => booking.ParkingSpotId == spotId);

            if (monthStart.HasValue && monthEndExclusive.HasValue)
            {
                var monthStartDateTime = monthStart.Value.ToDateTime(TimeOnly.MinValue);
                var monthEndExclusiveDateTime = monthEndExclusive.Value.ToDateTime(TimeOnly.MinValue);
                bookingsQuery = bookingsQuery.Where(booking =>
                    booking.StartDate < monthEndExclusiveDateTime &&
                    booking.EndDate > monthStartDateTime);
            }

            if (bookingStatus.HasValue)
            {
                bookingsQuery = bookingsQuery.Where(booking => booking.BookingStatus == bookingStatus.Value);
            }

            var bookings = await bookingsQuery
                .Include(booking => booking.ParkingSpot)
                .Include(booking => booking.Renter)
                .Include(booking => booking.Vehicle)
                .OrderByDescending(booking => booking.StartDate)
                .ThenByDescending(booking => booking.CreatedAt)
                .ToListAsync();

            var data = bookings.Select(MapOwnerBookingSummary).ToList();

            await _accessLogService.LogAsync(
                User,
                "GetOwnerParkingSpotBookings",
                true,
                $"ParkingSpotId={spotId}; Month={monthStart?.ToString("yyyy-MM", CultureInfo.InvariantCulture) ?? "all"}; " +
                $"Status={bookingStatus?.ToString() ?? "all"}; Count={data.Count}");

            return Ok(new OwnerBookingListResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = data.Count > 0
                    ? $"Retrieved {data.Count} booking(s) for this parking spot successfully."
                    : "No bookings found for this parking spot.",
                ParkingSpotId = spotId,
                Month = monthStart?.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                Status = bookingStatus?.ToString(),
                TotalCount = data.Count,
                Data = data
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving bookings for parking spot {ParkingSpotId}", spotId);
            await _accessLogService.LogAsync(User, "GetOwnerParkingSpotBookings", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving bookings for this parking spot."
            });
        }
    }

    /// <summary>
    /// Returns full booking, renter, vehicle, and financial detail for one booking made on one of the
    /// authenticated owner's parking spots. Wallet and idempotency data are excluded.
    /// </summary>
    [HttpGet("{spotId:int}/bookings/{bookingId:int}")]
    [ProducesResponseType(typeof(OwnerBookingDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OwnerBookingDetailResponse>> GetParkingSpotBooking(int spotId, int bookingId)
    {
        var userId = _currentUser.UserId!.Value;

        try
        {
            var booking = await _context.Bookings
                .AsNoTracking()
                .Include(item => item.ParkingSpot)
                .Include(item => item.Renter)
                .Include(item => item.Vehicle)
                .Include(item => item.Transactions)
                .FirstOrDefaultAsync(item => item.BookingId == bookingId);

            if (booking == null || booking.ParkingSpotId != spotId)
            {
                await _accessLogService.LogAsync(User, "GetOwnerParkingSpotBooking", false, $"Booking not found (spotId={spotId}, bookingId={bookingId})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Booking not found."
                });
            }

            if (booking.ParkingSpot.OwnerId != userId)
            {
                await _accessLogService.LogAsync(User, "GetOwnerParkingSpotBooking", false, $"Not owner (spotId={spotId}, bookingId={bookingId})", bookingId);
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "You are not authorized to view this booking."
                });
            }

            await _accessLogService.LogAsync(User, "GetOwnerParkingSpotBooking", true, $"ParkingSpotId={spotId}; BookingId={bookingId}", bookingId);

            return Ok(new OwnerBookingDetailResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Booking detail retrieved successfully.",
                Data = MapOwnerBookingDetail(booking)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving booking {BookingId} for parking spot {ParkingSpotId}", bookingId, spotId);
            await _accessLogService.LogAsync(User, "GetOwnerParkingSpotBooking", false, ex.Message, bookingId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving the booking detail."
            });
        }
    }

    /// <summary>
    /// Lists every parking spot owned by the authenticated owner.
    /// </summary>
    [HttpGet("my-parking")]
    [ProducesResponseType(typeof(DisplayMyParkingResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DisplayMyParkingResponse>> GetMyParking()
    {
        var userId = _currentUser.UserId!.Value;
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
        var userId = _currentUser.UserId!.Value;
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
        var userId = _currentUser.UserId!.Value;

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
                    : "Parking configuration saved. Complete the missing listing requirements before publication.",
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
    /// Publishes an owner listing only after freshly validating its current verification,
    /// listing content, prices, image, and future availability instead of trusting a stale flag.
    /// </summary>
    [HttpPost("{spotId:int}/publish")]
    [ProducesResponseType(typeof(OwnerParkingPublicationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OwnerParkingPublicationResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OwnerParkingPublicationResponse>> PublishParking(int spotId)
    {
        if (spotId <= 0)
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "spotId must be greater than zero."
            });
        }

        var userId = _currentUser.UserId!.Value;

        try
        {
            var spot = await _context.ParkingSpots
                .Include(item => item.VerificationRequests.Where(request => request.IsCurrent))
                .Include(item => item.ParkingSpotImages)
                .Include(item => item.ParkingAvailabilities)
                .AsSplitQuery()
                .FirstOrDefaultAsync(item => item.ParkingSpotId == spotId);

            if (spot == null)
            {
                await _accessLogService.LogAsync(User, "PublishOwnerParking", false, $"Spot not found (id={spotId})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Parking spot not found."
                });
            }

            if (spot.OwnerId != userId)
            {
                await _accessLogService.LogAsync(User, "PublishOwnerParking", false, $"Not owner (id={spotId})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "You are not authorized to publish this parking spot."
                });
            }

            if (spot.AvailabilityStatus == AvailabilityStatus.Deleted)
            {
                await _accessLogService.LogAsync(User, "PublishOwnerParking", false, $"Deleted spot (id={spotId})");
                return BadRequest(MapParkingPublicationResponse(
                    spot,
                    StatusCodes.Status400BadRequest,
                    false,
                    "A deleted parking spot cannot be published.",
                    spot.IsConfigurationComplete));
            }

            var firstBookableDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)).AddDays(1);
            var missingRequirements = GetPublicationRequirements(
                spot,
                firstBookableDate,
                out var isConfigurationComplete);

            if (missingRequirements.Count > 0)
            {
                await _accessLogService.LogAsync(
                    User,
                    "PublishOwnerParking",
                    false,
                    $"ParkingSpotId={spotId}; Missing={string.Join(", ", missingRequirements)}");

                return BadRequest(MapParkingPublicationResponse(
                    spot,
                    StatusCodes.Status400BadRequest,
                    false,
                    "Parking spot is not ready to publish.",
                    isConfigurationComplete,
                    missingRequirements));
            }

            var isAlreadyPublished = spot.IsPublished && spot.AvailabilityStatus == AvailabilityStatus.Available;
            if (!isAlreadyPublished || !spot.IsConfigurationComplete || !spot.ConfiguredAt.HasValue)
            {
                var now = DateTime.UtcNow;
                spot.IsConfigurationComplete = true;
                spot.ConfiguredAt ??= now;
                spot.IsPublished = true;
                spot.AvailabilityStatus = AvailabilityStatus.Available;
                spot.UpdatedAt = now;
                await _context.SaveChangesAsync();
            }

            await _accessLogService.LogAsync(User, "PublishOwnerParking", true, $"ParkingSpotId={spotId}");

            return Ok(MapParkingPublicationResponse(
                spot,
                StatusCodes.Status200OK,
                true,
                isAlreadyPublished
                    ? "Parking spot is already published and remains available."
                    : "Parking spot published successfully.",
                true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing parking spot {ParkingSpotId}", spotId);
            await _accessLogService.LogAsync(User, "PublishOwnerParking", false, ex.Message);

            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while publishing the parking spot."
            });
        }
    }

    /// <summary>
    /// Removes an owner listing from public discovery without changing availability rules or any
    /// pending, confirmed, active, or historical bookings. Repeated calls are idempotent.
    /// </summary>
    [HttpPost("{spotId:int}/unpublish")]
    [ProducesResponseType(typeof(OwnerParkingPublicationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OwnerParkingPublicationResponse>> UnpublishParking(int spotId)
    {
        if (spotId <= 0)
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "spotId must be greater than zero."
            });
        }

        var userId = _currentUser.UserId!.Value;

        try
        {
            var spot = await _context.ParkingSpots
                .FirstOrDefaultAsync(item => item.ParkingSpotId == spotId);

            if (spot == null)
            {
                await _accessLogService.LogAsync(User, "UnpublishOwnerParking", false, $"Spot not found (id={spotId})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Parking spot not found."
                });
            }

            if (spot.OwnerId != userId)
            {
                await _accessLogService.LogAsync(User, "UnpublishOwnerParking", false, $"Not owner (id={spotId})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "You are not authorized to unpublish this parking spot."
                });
            }

            if (spot.AvailabilityStatus == AvailabilityStatus.Deleted)
            {
                await _accessLogService.LogAsync(User, "UnpublishOwnerParking", false, $"Deleted spot (id={spotId})");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "A deleted parking spot cannot be unpublished."
                });
            }

            var isAlreadyUnpublished = !spot.IsPublished && spot.AvailabilityStatus == AvailabilityStatus.Inactive;
            if (!isAlreadyUnpublished)
            {
                spot.IsPublished = false;
                spot.AvailabilityStatus = AvailabilityStatus.Inactive;
                spot.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            await _accessLogService.LogAsync(User, "UnpublishOwnerParking", true, $"ParkingSpotId={spotId}");

            return Ok(MapParkingPublicationResponse(
                spot,
                StatusCodes.Status200OK,
                true,
                isAlreadyUnpublished
                    ? "Parking spot is already unpublished."
                    : "Parking spot unpublished successfully.",
                spot.IsConfigurationComplete));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unpublishing parking spot {ParkingSpotId}", spotId);
            await _accessLogService.LogAsync(User, "UnpublishOwnerParking", false, ex.Message);

            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while unpublishing the parking spot."
            });
        }
    }

    /// <summary>
    /// Recomputes every condition required for publication and reports the missing conditions in
    /// a stable order so the owner can correct the listing without relying on a cached flag.
    /// </summary>
    private static List<string> GetPublicationRequirements(
        ParkingSpot spot,
        DateOnly firstBookableDate,
        out bool isConfigurationComplete)
    {
        var missing = new List<string>();

        if (!spot.VerificationRequests.Any(request =>
                request.IsCurrent && request.VerificationStatus == VerificationStatus.Approved))
        {
            missing.Add("approved verification");
        }

        var configurationRequirements = GetConfigurationRequirements(spot);
        missing.AddRange(configurationRequirements);
        isConfigurationComplete = configurationRequirements.Count == 0;

        if (!HasFutureAvailabilityRule(spot.ParkingAvailabilities, firstBookableDate))
        {
            missing.Add("future availability rule");
        }

        return missing;
    }

    /// <summary>
    /// Determines whether at least one availability rule covers a real date from the first
    /// bookable day onward, including the rule's configured weekday or weekend pattern.
    /// </summary>
    private static bool HasFutureAvailabilityRule(
        IEnumerable<Availability> availabilityRules,
        DateOnly firstBookableDate)
    {
        foreach (var rule in availabilityRules)
        {
            var effectiveFrom = rule.EffectiveFrom ?? DateOnly.MinValue;
            var effectiveUntil = rule.EffectiveUntil ?? DateOnly.MaxValue;
            var futureFrom = effectiveFrom > firstBookableDate
                ? effectiveFrom
                : firstBookableDate;

            if (futureFrom <= effectiveUntil &&
                DateRangeContainsDayType(futureFrom, effectiveUntil, rule.DayType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Creates the common response used by publish and unpublish operations, including the
    /// resulting listing state and any publication requirements that remain unsatisfied.
    /// </summary>
    private static OwnerParkingPublicationResponse MapParkingPublicationResponse(
        ParkingSpot spot,
        int code,
        bool success,
        string message,
        bool isConfigurationComplete,
        IEnumerable<string>? missingRequirements = null)
    {
        return new OwnerParkingPublicationResponse
        {
            Code = code,
            Success = success,
            Message = message,
            ParkingSpotId = spot.ParkingSpotId,
            IsPublished = spot.IsPublished,
            IsConfigurationComplete = isConfigurationComplete,
            AvailabilityStatus = spot.AvailabilityStatus.ToString(),
            MissingRequirements = missingRequirements?.ToList() ?? new List<string>(),
            UpdatedAt = spot.UpdatedAt
        };
    }

    /// <summary>
    /// Lists the information that must exist before a listing can be marked configuration-complete.
    /// Publication performs its own final validation immediately before changing listing state.
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

    /// <summary>
    /// Parses the optional status query string into a BookingStatus enum value.
    /// Returns true when the value is blank or valid, false otherwise.
    /// </summary>
    private static bool TryParseBookingStatus(string? status, out BookingStatus? bookingStatus)
    {
        bookingStatus = null;
        if (string.IsNullOrWhiteSpace(status))
        {
            return true;
        }

        var matchingStatusName = Enum.GetNames<BookingStatus>()
            .FirstOrDefault(candidate => string.Equals(
                candidate,
                status.Trim(),
                StringComparison.OrdinalIgnoreCase));

        if (matchingStatusName == null)
        {
            return false;
        }

        bookingStatus = Enum.Parse<BookingStatus>(matchingStatusName);
        return true;
    }

    /// <summary>
    /// Maps an owner-authorized booking to a compact summary using inclusive customer-facing dates.
    /// </summary>
    private static OwnerBookingSummaryResponse MapOwnerBookingSummary(Booking booking)
    {
        var (startDate, endDate, bookedDays) = GetInclusiveBookingPeriod(booking);

        return new OwnerBookingSummaryResponse
        {
            BookingId = booking.BookingId,
            BookingReference = booking.BookingReference,
            ParkingSpotId = booking.ParkingSpotId,
            ParkingLabel = booking.ParkingSpot.ParkingLabel,
            RenterId = booking.RenterId,
            RenterName = $"{booking.Renter.FirstName} {booking.Renter.LastName}".Trim(),
            RenterEmail = booking.Renter.Email,
            RenterPhoneNumber = booking.Renter.PhoneNumber,
            VehicleId = booking.VehicleId,
            VehicleNumberPlate = booking.Vehicle.NumberPlate,
            StartDate = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EndDate = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            BookedDays = bookedDays,
            BookingStatus = booking.BookingStatus.ToString(),
            RenterTotal = booking.TotalAmount,
            OwnerPayoutAmount = booking.OwnerPayoutAmount,
            CreatedAt = booking.CreatedAt
        };
    }

    /// <summary>
    /// Maps an owner-authorized booking to the detailed response without exposing wallet identifiers,
    /// booking quote identifiers, or the renter's idempotency key.
    /// </summary>
    private static OwnerBookingDetailDataResponse MapOwnerBookingDetail(Booking booking)
    {
        var (startDate, endDate, bookedDays) = GetInclusiveBookingPeriod(booking);
        var renterFirstName = booking.Renter.FirstName ?? string.Empty;
        var renterLastName = booking.Renter.LastName ?? string.Empty;

        return new OwnerBookingDetailDataResponse
        {
            BookingId = booking.BookingId,
            BookingReference = booking.BookingReference,
            ParkingSpotId = booking.ParkingSpotId,
            ParkingLabel = booking.ParkingSpot.ParkingLabel,
            Renter = new OwnerBookingRenterResponse
            {
                RenterId = booking.RenterId,
                FirstName = renterFirstName,
                LastName = renterLastName,
                FullName = $"{renterFirstName} {renterLastName}".Trim(),
                Email = booking.Renter.Email,
                PhoneNumber = booking.Renter.PhoneNumber
            },
            Vehicle = new OwnerBookingVehicleResponse
            {
                VehicleId = booking.VehicleId,
                NumberPlate = booking.Vehicle.NumberPlate,
                Brand = booking.Vehicle.VehicleBrand,
                Model = booking.Vehicle.VehicleModel,
                Color = booking.Vehicle.VehicleColor
            },
            StartDate = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EndDate = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            BookedDays = bookedDays,
            BookingStatus = booking.BookingStatus.ToString(),
            CancellationReason = booking.CancellationReason,
            CancelledAt = booking.CancelledAt,
            CheckedInAt = booking.CheckedInAt,
            ActualExitAt = booking.ActualExitAt,
            Financial = new OwnerBookingFinancialResponse
            {
                RateType = booking.RateType.ToString(),
                RatePerDaySnapshot = booking.RatePerDaySnapshot,
                RentalSubtotal = booking.RentalSubtotal,
                RenterTotal = booking.TotalAmount,
                PlatformCommissionRate = booking.PlatformCommissionRate,
                PlatformCommissionAmount = booking.PlatformCommissionAmount,
                OwnerPayoutAmount = booking.OwnerPayoutAmount,
                RefundAmount = booking.RefundAmount,
                OverstayHours = booking.OverstayHours,
                OverstayPenaltyAmount = booking.OverstayPenaltyAmount
            },
            Transactions = booking.Transactions
                .OrderBy(transaction => transaction.CreatedAt)
                .ThenBy(transaction => transaction.TransactionId)
                .Select(transaction => new OwnerBookingTransactionResponse
                {
                    TransactionId = transaction.TransactionId,
                    TransactionType = transaction.TransactionType.ToString(),
                    Amount = transaction.Amount,
                    PaymentMethod = transaction.PaymentMethod.ToString(),
                    TransactionStatus = transaction.TransactionStatus.ToString(),
                    ReferenceNumber = transaction.ReferenceNumber,
                    CreatedAt = transaction.CreatedAt,
                    UpdatedAt = transaction.UpdatedAt
                })
                .ToList(),
            CreatedAt = booking.CreatedAt,
            UpdatedAt = booking.UpdatedAt
        };
    }

    /// <summary>
    /// Converts the internal exclusive booking end boundary into inclusive customer-facing dates
    /// and falls back to a calculated day count for legacy bookings without a stored snapshot.
    /// </summary>
    private static (DateOnly StartDate, DateOnly EndDate, int BookedDays) GetInclusiveBookingPeriod(Booking booking)
    {
        var startDate = DateOnly.FromDateTime(booking.StartDate);
        var endDate = DateOnly.FromDateTime(booking.EndDate);
        if (booking.EndDate.TimeOfDay == TimeSpan.Zero && endDate > startDate)
        {
            endDate = endDate.AddDays(-1);
        }

        var bookedDays = booking.BookedDays > 0
            ? booking.BookedDays
            : Math.Max(1, endDate.DayNumber - startDate.DayNumber + 1);

        return (startDate, endDate, bookedDays);
    }
}
