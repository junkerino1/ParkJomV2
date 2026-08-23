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
public class ParkingController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly CloudinaryService _cloudinaryService;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<ParkingController> _logger;
    private readonly IPropertyService _propertyService;

    public ParkingController(ApplicationDbContext context, CloudinaryService cloudinaryService,
        AccessLogService accessLogService,
        ILogger<ParkingController> logger, IPropertyService propertyService)
    {
        _context = context;
        _cloudinaryService = cloudinaryService;
        _accessLogService = accessLogService;
        _logger = logger;
        _propertyService = propertyService;
    }

    /// <summary>
    /// Get a parking spot by ID
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ParkingSpotDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ParkingSpotDetailResponse>> GetParkingSpot(int id)
    {
        try
        {
            var spot = await _context.ParkingSpots
                .Include(ps => ps.Property)
                .Include(ps => ps.Owner)
                .Include(ps => ps.VerificationRequests.Where(vr => vr.IsCurrent))
                .Include(ps => ps.ParkingSpotImages.OrderBy(psi => psi.DisplayOrder))
                    .ThenInclude(psi => psi.MediaFile)
                .FirstOrDefaultAsync(ps => ps.ParkingSpotId == id);

            if (spot == null)
            {
                await _accessLogService.LogAsync(User, "GetParkingSpot", false, $"Parking spot not found (id={id})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Parking spot not found"
                });
            }

            var dto = new ParkingSpotDetailDTO
            {
                ParkingSpotId = spot.ParkingSpotId,
                PropertyId = spot.PropertyId,
                PropertyName = spot.Property?.PropertyName,
                Address = spot.Property?.Address,
                OwnerId = spot.OwnerId,
                OwnerName = $"{spot.Owner?.FirstName} {spot.Owner?.LastName}".Trim(),
                ParkingLabel = spot.ParkingLabel,
                AvailabilityStatus = spot.AvailabilityStatus,
                VerificationStatus = spot.VerificationRequests.FirstOrDefault()?.VerificationStatus ?? VerificationStatus.Pending,
                MonthlyRate = spot.MonthlyRate,
                DailyRate = spot.DailyRate,
                IsPublished = spot.IsPublished,
                CreatedAt = spot.CreatedAt,
                UpdatedAt = spot.UpdatedAt,
                Images = spot.ParkingSpotImages.Select(psi => new ParkingSpotImageDTO
                {
                    ParkingSpotId = psi.ParkingSpotId,
                    MediaFileId = psi.MediaFileId,
                    ResourceType = psi.MediaFile?.ResourceType,
                    Format = psi.MediaFile?.Format,
                    OriginalFileName = psi.MediaFile?.OriginalFileName,
                    UploadedAt = psi.CreatedAt
                }).ToList()
            };

            await _accessLogService.LogAsync(User, "GetParkingSpot", true, $"ParkingSpotId={id}");

            return Ok(new ParkingSpotDetailResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Parking spot retrieved successfully",
                Data = dto
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving parking spot {ParkingSpotId}", id);
            await _accessLogService.LogAsync(User, "GetParkingSpot", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving the parking spot"
            });
        }
    }

    /// <summary>
    /// owner register a new parking
    /// search if property exist, if not, create a new property
    /// then use the property id to create a new parking spot
    /// </summary>
    [Authorize]
    [HttpPost("create")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ParkingRegistrationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ParkingRegistrationResponse>> RegisterParking([FromForm] ParkingRegistrationRequest request)
    {
        // check if request is valid
        if (!ModelState.IsValid)
        {
            await _accessLogService.LogAsync(User, "RegisterParking", false, "Invalid request");
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Invalid request."
            });
        }

        // verify user identity
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null)
        {
            await _accessLogService.LogAsync(User, "RegisterParking", false, "User not found");
            return NotFound(new ErrorResponse
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "User not found."
            });
        }

        if (user.UserType != UserType.PropertyOwner)
        {
            await _accessLogService.LogAsync(User, "RegisterParking", false, "Not a property owner");
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Only property owners can register parking."
            });
        }

        // verify the document type and content type
        var allowedTypes = new[] { "application/pdf", "image/jpeg", "image/jpg", "image/png" };

        if (!allowedTypes.Contains(request.Document.ContentType.ToLower()))
        {
            await _accessLogService.LogAsync(User, "RegisterParking", false, "Invalid file type");
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Only PDF, JPG and PNG files are allowed."
            });
        }

        // lookup for property, if not found, create a new property
        var property = await _propertyService.ResolvePropertyAsync(request);
        if (property == null)
        {
            await _accessLogService.LogAsync(User, "RegisterParking", false, "Could not resolve property");
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "Could not find or create the property. Please check the property name and address."
            });
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var folder = "parkjom/verification-documents";
            var format = Path.GetExtension(request.Document.FileName)
                         .TrimStart('.')
                         .ToLowerInvariant();

            dynamic uploadResult;

            uploadResult = await _cloudinaryService.UploadPrivateDocumentAsync(request.Document, folder);
            if (uploadResult == null)
            {
                throw new Exception("Cloudinary upload failed.");
            }

            _logger.LogInformation("Cloudinary upload successful. PublicId={PublicId}", (string)uploadResult.PublicId);

            var mediaFile = new MediaFile
            {
                PublicId = uploadResult.PublicId,
                SecureUrl = uploadResult.SecureUrl.ToString(),
                ResourceType = uploadResult.ResourceType,
                Format = uploadResult.Format ?? format,
                OriginalFileName = uploadResult.OriginalFilename,
                Folder = folder,
                UploadedBy = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.MediaFiles.Add(mediaFile);

            // Create ParkingSpot
            var parkingSpot = new ParkingSpot
            {
                PropertyId = property.PropertyId,
                OwnerId = userId,
                ParkingLabel = $"{request.BayNumber}/{request.Level}",
                AvailabilityStatus = AvailabilityStatus.Inactive,
                MonthlyRate = 0,
                IsPublished = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.ParkingSpots.Add(parkingSpot);
            _logger.LogInformation("ParkingSpot created. ParkingSpotId={ParkingSpotId}", parkingSpot.ParkingSpotId);

            // Create Verification Request
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

            _context.ParkingVerificationRequests.Add(verificationRequest);
            _logger.LogInformation("VerificationRequest created. VerificationRequestId={VerificationRequestId}", verificationRequest.VerificationRequestId);

            // Create Verification Document
            var verificationDocument = new VerificationDocument
            {
                VerificationRequest = verificationRequest,
                MediaFile = mediaFile,
                DocumentType = request.DocumentType,
                UploadedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.VerificationDocuments.Add(verificationDocument);
            await _context.SaveChangesAsync();

            _logger.LogInformation("VerificationDocument created. VerificationDocumentId={VerificationDocumentId}", verificationDocument.VerificationDocumentId);

            await transaction.CommitAsync();

            _logger.LogInformation("Verification request submitted successfully. ParkingSpotId={ParkingSpotId}, VerificationRequestId={VerificationRequestId}",
                parkingSpot.ParkingSpotId, verificationRequest.VerificationRequestId);

            await _accessLogService.LogAsync(User, "RegisterParking", true, $"ParkingSpotId={parkingSpot.ParkingSpotId} VerificationRequestId={verificationRequest.VerificationRequestId}");
            return Ok(new ParkingRegistrationResponse
            {
                Code = StatusCodes.Status200OK,
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
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An unexpected error occurred while submitting the verification request."
            });
        }
    }

    /// <summary>
    /// Update info of a parking spot
    /// </summary>
    [Authorize]
    [HttpPut("edit/{id}")]
    [ProducesResponseType(typeof(UpdateParkingSpotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UpdateParkingSpotResponse>> UpdateParkingSpot(int id, [FromBody] UpdateParkingSpotRequest request)
    {
        try
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                await _accessLogService.LogAsync(User, "UpdateParkingSpot", false, $"User not found (id={id})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            var spot = await _context.ParkingSpots.FirstOrDefaultAsync(ps => ps.ParkingSpotId == id);

            if (spot == null)
            {
                await _accessLogService.LogAsync(User, "UpdateParkingSpot", false, $"Parking spot not found (id={id})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Parking spot not found"
                });
            }

            if (spot.OwnerId != userId)
            {
                await _accessLogService.LogAsync(User, "UpdateParkingSpot", false, $"Not authorized (id={id})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "You are not authorized to update this parking spot"
                });
            }

            if (request.ParkingLabel != null)
                spot.ParkingLabel = request.ParkingLabel;

            if (request.MonthlyRate.HasValue)
                spot.MonthlyRate = request.MonthlyRate;

            if (request.DailyRate.HasValue)
                spot.DailyRate = request.DailyRate;

            spot.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Parking spot updated. ParkingSpotId={ParkingSpotId}", spot.ParkingSpotId);

            await _accessLogService.LogAsync(User, "UpdateParkingSpot", true, $"ParkingSpotId={spot.ParkingSpotId}");
            return Ok(new UpdateParkingSpotResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Parking spot updated successfully",
                ParkingSpotId = spot.ParkingSpotId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating parking spot {ParkingSpotId}", id);
            await _accessLogService.LogAsync(User, "UpdateParkingSpot", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while updating the parking spot"
            });
        }
    }

    /// <summary>
    /// Delete a parking spot
    /// </summary>
    [Authorize]
    [HttpPost("delete-parking/{id}")]
    [ProducesResponseType(typeof(DeleteParkingSpotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeleteParkingSpotResponse>> DeleteParkingSpot(int id)
    {
        try
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                await _accessLogService.LogAsync(User, "DeleteParkingSpot", false, $"User not found (id={id})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            var spot = await _context.ParkingSpots
                .Include(ps => ps.Bookings)
                .Include(ps => ps.VerificationRequests)
                .FirstOrDefaultAsync(ps => ps.ParkingSpotId == id);

            if (spot == null)
            {
                await _accessLogService.LogAsync(User, "DeleteParkingSpot", false, $"Parking spot not found (id={id})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Parking spot not found"
                });
            }

            if (spot.OwnerId != userId && user.UserType != UserType.Admin)
            {
                await _accessLogService.LogAsync(User, "DeleteParkingSpot", false, $"Not authorized (id={id})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "You are not authorized to delete this parking spot"
                });
            }

            if (spot.Bookings.Any(b => b.BookingStatus == BookingStatus.Confirmed || b.BookingStatus == BookingStatus.Pending))
            {
                await _accessLogService.LogAsync(User, "DeleteParkingSpot", false, $"Active bookings exist (id={id})");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Cannot delete parking spot with active bookings"
                });
            }

            spot.AvailabilityStatus = AvailabilityStatus.Deleted;

            _context.ParkingSpots.Update(spot);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Parking spot deleted. ParkingSpotId={ParkingSpotId}", id);

            await _accessLogService.LogAsync(User, "DeleteParkingSpot", true, $"ParkingSpotId={id}");
            return Ok(new DeleteParkingSpotResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Parking spot deleted successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting parking spot {ParkingSpotId}", id);
            await _accessLogService.LogAsync(User, "DeleteParkingSpot", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while deleting the parking spot"
            });
        }
    }

    /// <summary>
    /// Get all parking spots owned by the authenticated user
    /// </summary>
    [Authorize]
    [HttpGet("my-parking")]
    [ProducesResponseType(typeof(DisplayMyParkingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<DisplayMyParkingResponse>> GetMySpots()
    {
        try
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found", userId);
                await _accessLogService.LogAsync(User, "GetMySpots", false, "User not found");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            if (user.UserType != UserType.PropertyOwner)
            {
                _logger.LogWarning("Unauthorized access attempt. UserId={UserId}, UserType={UserType}", userId, user.UserType);
                await _accessLogService.LogAsync(User, "GetMySpots", false, "Not a property owner");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Only property owners can view their parking spots"
                });
            }

            var parkingSpots = await _context.ParkingSpots
                .Where(ps => ps.OwnerId == userId)
                .Include(ps => ps.VerificationRequests)
                .OrderByDescending(ps => ps.CreatedAt)
                .ToListAsync();

                // return Ok(parkingSpots);

            if (!parkingSpots.Any())
            {
                _logger.LogInformation("No parking spots found for user {UserId}", userId);
                await _accessLogService.LogAsync(User, "GetMySpots", true, "0 spots");
                return Ok(new DisplayMyParkingResponse
                {
                    Code = StatusCodes.Status200OK,
                    Success = true,
                    Message = "No parking spots found",
                    Data = new List<DisplayParkingSpotDTO>()
                });
            }

            var result = parkingSpots
                .Select(MapToDisplayParkingSpotDTO)
                .ToList();

            _logger.LogInformation("Retrieved {Count} parking spots for owner {UserId}", result.Count, userId);

            await _accessLogService.LogAsync(User, "GetMySpots", true, $"{result.Count} spot(s)");
            return Ok(new DisplayMyParkingResponse
                {
                    Code = StatusCodes.Status200OK,
                    Success = true,
                    Message = $"Retrieved {result.Count} parking spot(s) successfully",
                    Data = result
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user's parking spots");
            await _accessLogService.LogAsync(User, "GetMySpots", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving your parking spots"
            });
        }
    }

    /// <summary>
    /// Configure a parking spot (pricing, availability, images) — Owner only
    /// </summary>
    [Authorize]
    [HttpPost("configuration")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ConfigParkingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ConfigParkingResponse>> ConfigParking([FromForm] ConfigParkingRequest request)
    {
        try
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

            // return Ok(request);

            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found", userId);
                await _accessLogService.LogAsync(User, "ConfigParking", false, "User not found");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            if (user.UserType != UserType.PropertyOwner)
            {
                _logger.LogWarning("Unauthorized config attempt. UserId={UserId}, UserType={UserType}", userId, user.UserType);
                await _accessLogService.LogAsync(User, "ConfigParking", false, "Not a property owner");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Only property owners can configure parking spots"
                });
            }

            var parkingSpot = await _context.ParkingSpots
                .FirstOrDefaultAsync(p => p.ParkingSpotId == request.ParkingSpotId);

            if (parkingSpot == null || parkingSpot.OwnerId != userId)
            {
                _logger.LogWarning("Parking spot {ParkingSpotId} not found or unauthorized. UserId={UserId}", request.ParkingSpotId, userId);
                await _accessLogService.LogAsync(User, "ConfigParking", false, $"Spot not found/unauthorized (id={request.ParkingSpotId})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "You are not authorized to configure this parking spot"
                });
            }

            var pvq = await _context.ParkingVerificationRequests
                .Include(vr => vr.VerificationDocuments)
                .FirstOrDefaultAsync(vr => vr.ParkingSpotId == request.ParkingSpotId);

            if (pvq == null || pvq.VerificationStatus != VerificationStatus.Approved)
            {
                _logger.LogWarning("Parking spot {ParkingSpotId} is not verified. UserId={UserId}", request.ParkingSpotId, userId);
                await _accessLogService.LogAsync(User, "ConfigParking", false, $"Not verified (id={request.ParkingSpotId})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Your parking verification request is currently under review and has not yet been approved."
                });
            }

            var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png" };

            if (request.ParkingImage != null && request.ParkingImage.Count > 0)
            {
                foreach (var file in request.ParkingImage)
                {
                    if (!allowedTypes.Contains(file.ContentType.ToLower()))
                    {
                        await _accessLogService.LogAsync(User, "ConfigParking", false, "Invalid image file type");
                        return BadRequest(new ErrorResponse
                        {
                            Code = StatusCodes.Status400BadRequest,
                            Success = false,
                            Message = "Only JPG and PNG files are allowed for parking images."
                        });
                    }
                }
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var folder = "parkjom/parking-images";
                int displayOrder = 1;

                if (request.ParkingImage != null && request.ParkingImage.Count > 0)
                {
                    var existingImages = await _context.ParkingSpotImages
                        .Where(psi => psi.ParkingSpotId == request.ParkingSpotId)
                        .ToListAsync();

                    displayOrder = existingImages.Any() ? existingImages.Max(psi => psi.DisplayOrder) + 1 : 1;

                    foreach (var file in request.ParkingImage)
                    {
                        var uploadResult = await _cloudinaryService.UploadImageAsync(file, folder);

                        if (uploadResult == null)
                        {
                            throw new Exception("Cloudinary upload failed.");
                        }

                        _logger.LogInformation("Cloudinary upload successful. PublicId={PublicId}", uploadResult.PublicId);

                        var mediaFile = new MediaFile
                        {
                            PublicId = uploadResult.PublicId,
                            SecureUrl = uploadResult.SecureUrl.ToString(),
                            ResourceType = uploadResult.ResourceType,
                            Format = uploadResult.Format ?? Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant(),
                            OriginalFileName = uploadResult.OriginalFilename,
                            Folder = folder,
                            UploadedBy = userId,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };

                        _context.MediaFiles.Add(mediaFile);
                        await _context.SaveChangesAsync();

                        _logger.LogInformation("MediaFile created. MediaFileId={MediaFileId}", mediaFile.MediaFileId);

                        bool isPrimary = (displayOrder == 1 && !existingImages.Any());

                        var parkingSpotImage = new ParkingSpotImage
                        {
                            ParkingSpotId = request.ParkingSpotId,
                            MediaFileId = mediaFile.MediaFileId,
                            DisplayOrder = displayOrder,
                            IsPrimary = isPrimary,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };

                        _context.ParkingSpotImages.Add(parkingSpotImage);
                        displayOrder++;
                    }

                    await _context.SaveChangesAsync();
                }
                else
                {
                    await _accessLogService.LogAsync(User, "ConfigParking", false, "No parking images uploaded");
                    return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                    {
                        Code = StatusCodes.Status500InternalServerError,
                        Success = false,
                        Message = "Please upload parking spot images."
                    });
                }

                if (Enum.TryParse<DayType>(request.DayType, true, out var dayType))
                {
                    switch (dayType)
                    {
                        case DayType.Weekday:
                            break;

                        case DayType.Weekend:
                            break;

                        case DayType.Everyday:
                            break;
                    }
                }
                else
                {
                    await _accessLogService.LogAsync(User, "ConfigParking", false, $"Invalid day type '{request.DayType}'");
                    return StatusCode(StatusCodes.Status400BadRequest, new ErrorResponse
                    {
                        Code = StatusCodes.Status400BadRequest,
                        Success = false,
                        Message = "Invalid day type."
                    });
                }

                var availability = new Availability
                {
                    ParkingSpotId = request.ParkingSpotId,
                    DayType = dayType,
                    StartTime = request.StartTime,
                    EndTime = request.EndTime,
                    EffectiveFrom = request.EffectiveFrom,
                    EffectiveUntil = request.EffectiveUntil,
                };

                _context.Availabilities.Add(availability);

                if (dayType == DayType.Weekday || dayType == DayType.Weekend)
                {
                    if (request.DailyRate.HasValue)
                    {
                        parkingSpot.DailyRate = request.DailyRate;
                    }
                }
                else if (dayType == DayType.Everyday)
                {
                    if (request.MonthlyRate.HasValue)
                    {
                        parkingSpot.MonthlyRate = request.MonthlyRate;
                    }
                }

                parkingSpot.AvailabilityStatus = AvailabilityStatus.Available;
                parkingSpot.IsPublished = false; // Set to false until the owner decides to publish it
                parkingSpot.UpdatedAt = DateTime.UtcNow;

                _context.ParkingSpots.Update(parkingSpot);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Parking spot configured. ParkingSpotId={ParkingSpotId}, DayType={DayType}, AvailabilityStatus={Status}",
                    request.ParkingSpotId, request.DayType, parkingSpot.AvailabilityStatus);

                await transaction.CommitAsync();

                _logger.LogInformation("Parking configuration completed successfully. ParkingSpotId={ParkingSpotId}", request.ParkingSpotId);

                await _accessLogService.LogAsync(User, "ConfigParking", true, $"ParkingSpotId={parkingSpot.ParkingSpotId}");
                return Ok(new ConfigParkingResponse
                {
                    Code = StatusCodes.Status200OK,
                    Success = true,
                    Message = "Parking spot configured successfully.",
                    ParkingSpotId = parkingSpot.ParkingSpotId
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Parking configuration failed for UserId={UserId}, ParkingSpotId={ParkingSpotId}", userId, request.ParkingSpotId);
                await _accessLogService.LogAsync(User, "ConfigParking", false, ex.Message);

                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    Code = StatusCodes.Status500InternalServerError,
                    Success = false,
                    Message = "An unexpected error occurred while configuring the parking spot."
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in config-parking endpoint for ParkingSpotId={ParkingSpotId}", request.ParkingSpotId);
            await _accessLogService.LogAsync(User, "ConfigParking", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while configuring the parking spot"
            });
        }
    }

    
    /// <summary>
    /// Update the availability status of a parking spot (Owner/Admin only)
    /// PUT /api/parking/availability
    /// </summary>
    [Authorize]
    [HttpPut("availability")]
    [ProducesResponseType(typeof(UpdateAvailabilityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UpdateAvailabilityResponse>> UpdateAvailability([FromBody] UpdateAvailabilityRequest request)
    {
        try
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                await _accessLogService.LogAsync(User, "UpdateAvailability", false, "User not found");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            var spot = await _context.ParkingSpots.FirstOrDefaultAsync(ps => ps.ParkingSpotId == request.ParkingSpotId);

            if (spot == null)
            {
                await _accessLogService.LogAsync(User, "UpdateAvailability", false, $"Parking spot not found (id={request.ParkingSpotId})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Parking spot not found"
                });
            }

            if (spot.OwnerId != userId && user.UserType != UserType.Admin)
            {
                await _accessLogService.LogAsync(User, "UpdateAvailability", false, $"Not authorized (id={request.ParkingSpotId})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "You are not authorized to update this parking spot"
                });
            }

            var verificationRequest = await _context.ParkingVerificationRequests
                .FirstOrDefaultAsync(vr => vr.ParkingSpotId == request.ParkingSpotId && vr.IsCurrent);

            if (verificationRequest == null || verificationRequest.VerificationStatus != VerificationStatus.Approved)
            {
                await _accessLogService.LogAsync(User, "UpdateAvailability", false, $"Not verified (id={request.ParkingSpotId})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Parking verification has not been approved. You are not allowed to perform this action."
                });
            }

            if (Enum.TryParse<AvailabilityStatus>(request.AvailabilityStatus, true, out var availabilityStatus))
            {
                switch (availabilityStatus)
                {
                    case AvailabilityStatus.Available:
                        break;

                    case AvailabilityStatus.Inactive:
                        break;

                    case AvailabilityStatus.Deleted:
                        break;

                    case AvailabilityStatus.Occupied:
                        break;
                }
            }
            else{
                await _accessLogService.LogAsync(User, "UpdateAvailability", false, $"Invalid status '{request.AvailabilityStatus}'");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Invalid availability status"
                });
            }


            if (availabilityStatus == AvailabilityStatus.Deleted)
            {
                await _accessLogService.LogAsync(User, "UpdateAvailability", false, $"Deleted spot (id={request.ParkingSpotId})");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Cannot modify availability of a deleted parking spot"
                });
            }

            spot.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Parking spot availability updated. ParkingSpotId={ParkingSpotId}, AvailabilityStatus={Status}",
                spot.ParkingSpotId, spot.AvailabilityStatus);

            await _accessLogService.LogAsync(User, "UpdateAvailability", true, $"ParkingSpotId={spot.ParkingSpotId} -> {availabilityStatus}");
            return Ok(new UpdateAvailabilityResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = $"Parking spot availability updated from {spot.AvailabilityStatus.ToString()} to {availabilityStatus.ToString()} successfully",
                ParkingSpotId = spot.ParkingSpotId,
                AvailabilityStatus = availabilityStatus.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating parking spot availability {ParkingSpotId}", request.ParkingSpotId);
            await _accessLogService.LogAsync(User, "UpdateAvailability", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while updating the parking spot availability"
            });
        }
    }

    /// <summary>
    /// Publish or unpublish a parking spot (Owner/Admin only)
    /// PUT /api/parking/publish
    /// </summary>
    [Authorize]
    [HttpPut("publish")]
    [ProducesResponseType(typeof(PublishParkingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PublishParkingResponse>> PublishParking([FromBody] PublishParkingRequest request)
    {
        try
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                await _accessLogService.LogAsync(User, "PublishParking", false, "User not found");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            var spot = await _context.ParkingSpots.FirstOrDefaultAsync(ps => ps.ParkingSpotId == request.ParkingSpotId);

            if (spot == null)
            {
                await _accessLogService.LogAsync(User, "PublishParking", false, $"Parking spot not found (id={request.ParkingSpotId})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Parking spot not found"
                });
            }

            if (spot.OwnerId != userId && user.UserType != UserType.Admin)
            {
                await _accessLogService.LogAsync(User, "PublishParking", false, $"Not authorized (id={request.ParkingSpotId})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "You are not authorized to update this parking spot"
                });
            }

            var verificationRequest = await _context.ParkingVerificationRequests
                .FirstOrDefaultAsync(vr => vr.ParkingSpotId == request.ParkingSpotId && vr.IsCurrent);

            if (verificationRequest == null || verificationRequest.VerificationStatus != VerificationStatus.Approved)
            {
                await _accessLogService.LogAsync(User, "PublishParking", false, $"Not verified (id={request.ParkingSpotId})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Parking verification has not been approved. You are not allowed to perform this action."
                });
            }

            if (spot.AvailabilityStatus == AvailabilityStatus.Deleted)
            {
                await _accessLogService.LogAsync(User, "PublishParking", false, $"Deleted spot (id={request.ParkingSpotId})");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Cannot modify publish status of a deleted parking spot"
                });
            }

            spot.IsPublished = request.IsPublished;
            spot.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Parking spot publish status updated. ParkingSpotId={ParkingSpotId}, IsPublished={IsPublished}",
                spot.ParkingSpotId, spot.IsPublished);

            await _accessLogService.LogAsync(User, "PublishParking", true, $"ParkingSpotId={spot.ParkingSpotId} published={spot.IsPublished}");
            return Ok(new PublishParkingResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = request.IsPublished ? "Parking spot published successfully" : "Parking spot unpublished successfully",
                ParkingSpotId = spot.ParkingSpotId,
                IsPublished = spot.IsPublished
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating parking spot publish status {ParkingSpotId}", request.ParkingSpotId);
            await _accessLogService.LogAsync(User, "PublishParking", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while updating the parking spot publish status"
            });
        }
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
            VerificationStatus = parkingSpot.VerificationRequests.Single(v => v.IsCurrent).VerificationStatus.ToString(),
            MonthlyRate = parkingSpot.MonthlyRate,
            DailyRate = parkingSpot.DailyRate,
            IsPublished = parkingSpot.IsPublished,
            CreatedAt = parkingSpot.CreatedAt,
            UpdatedAt = parkingSpot.UpdatedAt
        };
    }
}   