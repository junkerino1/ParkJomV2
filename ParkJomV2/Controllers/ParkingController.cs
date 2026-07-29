using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.SqlServer.Server;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;

namespace ParkJomV2.Controllers
{
    [ApiController]
    [Route("api/parking")]
    public class ParkingController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly CloudinaryService _cloudinaryService;
        private readonly ILogger<ParkingController> _logger;

        public ParkingController(ApplicationDbContext context, CloudinaryService cloudinaryService, ILogger<ParkingController> logger)
        {
            _context = context;
            _cloudinaryService = cloudinaryService;
            _logger = logger;
        }

        /// <summary>
        /// Register a new parking spot with verification documents
        /// </summary>
        [Authorize]
        [HttpPost("register-parking")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(ParkingRegistrationResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ParkingRegistrationResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ParkingRegistrationResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ParkingRegistrationResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ParkingRegistrationResponse>> RegisterParking([FromForm] ParkingDTO dto)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

            //return Ok(dto);

            //return Ok(user);

            if (!ModelState.IsValid)
            {
                return BadRequest(new ParkingRegistrationResponse
                {
                    Success = false,
                    Message = "Invalid request."
                });
            }

            var allowedTypes = new[]
            {
                "application/pdf",
                "image/jpeg",
                "image/jpg",
                "image/png"
            };

            if (!allowedTypes.Contains(dto.Document.ContentType.ToLower()))
            {
                return BadRequest(new ParkingRegistrationResponse
                {
                    Success = false,
                    Message = "Only PDF, JPG and PNG files are allowed."
                });
            }

            var property = await _context.Properties.FindAsync(dto.PropertyId);

            if (property == null)
            {
                _logger.LogWarning("Property {PropertyId} not found.", dto.PropertyId);

                return NotFound(new ParkingRegistrationResponse
                {
                    Success = false,
                    Message = "Property not found."
                });
            }

            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found.", userId);

                return NotFound(new ParkingRegistrationResponse
                {
                    Success = false,
                    Message = "User not found."
                });
            }

            if (user.UserType != UserType.PropertyOwner && user.UserType != UserType.Renter)
            {
                return BadRequest(new ParkingRegistrationResponse
                {
                    Success = false,
                    Message = "Only property owners can register parking."
                });
            }

            _logger.LogInformation("Parking registration requested. UserId={UserId}, PropertyId={PropertyId}", userId, dto.PropertyId);

            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                var folder = $"parkjom/verification-documents";
                var format = Path.GetExtension(dto.Document.FileName)
                             .TrimStart('.')
                             .ToLowerInvariant();

                dynamic uploadResult;

                if (dto.Document.ContentType.StartsWith("image"))
                {
                    uploadResult = await _cloudinaryService.UploadImageAsync(dto.Document, folder);
                }
                else
                {
                    uploadResult = await _cloudinaryService.UploadPdfAsync(dto.Document, folder);
                }

                if (uploadResult == null)
                {
                    throw new Exception("Cloudinary upload failed.");
                }

                _logger.LogInformation("Cloudinary upload successful. PublicId={PublicId}", (string)uploadResult.PublicId);

                //return Ok(uploadResult);

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

                _logger.LogInformation("MediaFile created. MediaFileId={MediaFileId}", mediaFile.MediaFileId);

                // Create ParkingSpot
                var parkingSpot = new ParkingSpot
                {
                    PropertyId = dto.PropertyId,
                    OwnerId = userId,
                    ParkingLabel = $"{dto.BayNumber}/{dto.Level}",
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
                    DocumentType = dto.DocumentType,
                    UploadedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.VerificationDocuments.Add(verificationDocument);
                await _context.SaveChangesAsync();

                _logger.LogInformation("VerificationDocument created. VerificationDocumentId={VerificationDocumentId}", verificationDocument.VerificationDocumentId);

                // Commit transaction
                await transaction.CommitAsync();

                _logger.LogInformation("Parking registration completed successfully. ParkingSpotId={ParkingSpotId}", parkingSpot.ParkingSpotId);

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

                _logger.LogError(ex, "Parking registration failed for UserId={UserId}", userId);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new ParkingRegistrationResponse
                    {
                        Code = StatusCodes.Status500InternalServerError,
                        Success = false,
                        Message = "An unexpected error occurred while registering the parking spot."
                    });
            }
        }

        /// <summary>
        /// Get all parking verification requests (Admin only) - without documents
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

                // Check if user is Administrator
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

                var verificationRequests = await _context.ParkingVerificationRequests
                    .Include(vr => vr.ParkingSpot)
                    .ThenInclude(ps => ps.Property)
                    .Include(vr => vr.SubmittedByUser)
                    .OrderByDescending(vr => vr.SubmittedAt)
                    .ToListAsync();

                var result = verificationRequests
                    .Select(vr => MapToVerificationRequestListDTO(vr))
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
        /// Get specific verification request with documents (Admin only)
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

                // Check if user is Administrator
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
        [HttpPost("verification-requests/{id}/approve")]
        [ProducesResponseType(typeof(ApprovalResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ApprovalResponse>> ApproveParkingRequest(int id, [FromBody] ApprovalRequest request)
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

                // Check if user is Administrator
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

                var previousStatus = verificationRequest.VerificationStatus;

                // Update verification status based on boolean
                verificationRequest.VerificationStatus = request.IsApproved
                    ? VerificationStatus.Approved
                    : VerificationStatus.Rejected;

                verificationRequest.UpdatedAt = DateTime.UtcNow;

                _context.ParkingVerificationRequests.Update(verificationRequest);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Verification request {VerificationRequestId} status updated from {PreviousStatus} to {NewStatus} by admin {UserId}",
                    id,
                    previousStatus,
                    verificationRequest.VerificationStatus,
                    user.FirstName + user.LastName);

                var statusMessage = request.IsApproved ? "Approved" : "Rejected";

                return Ok(new ApprovalResponse
                {
                    Code = StatusCodes.Status200OK,
                    Success = true,
                    Message = $"Verification request {statusMessage.ToLower()} successfully",
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

        [HttpPost("config-parking/{id}")]
        [Authorize]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(ConfigParkingResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ConfigParkingResponse>> ConfigParking(int id, [FromForm] ConfigParkingRequest request)
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

                //return Ok(user);

                // Check if user is PropertyOwner
                if (user.UserType != UserType.PropertyOwner)
                {
                    _logger.LogWarning("Unauthorized config attempt. UserId={UserId}, UserType={UserType}", userId, user.UserType);
                    return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                    {
                        Code = StatusCodes.Status403Forbidden,
                        Success = false,
                        Message = "Only property owners can configure parking spots"
                    });
                }

                var parkingSpot = await _context.ParkingSpots
                    .FirstOrDefaultAsync(p => p.ParkingSpotId == id);

                // Check if parking spot exists and belongs to the user
                if (parkingSpot == null || parkingSpot.OwnerId != userId)
                {
                    _logger.LogWarning("Parking spot {ParkingSpotId} not found or unauthorized. UserId={UserId}", id, userId);
                    return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                    {
                        Code = StatusCodes.Status403Forbidden,
                        Success = false,
                        Message = "You are not authorized to configure this parking spot"
                    });   
                }

                // check if parking spot is approved
                var pvq = await _context.ParkingVerificationRequests
                    .Include(vr => vr.VerificationDocuments)
                    .FirstOrDefaultAsync(vr => vr.ParkingSpotId == id);

                if (pvq == null || pvq.VerificationStatus != VerificationStatus.Approved)
                {
                    _logger.LogWarning("Parking spot {ParkingSpotId} is not verified. UserId={UserId}", id, userId);
                    return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                    {
                        Code = StatusCodes.Status403Forbidden,
                        Success = false,
                        Message = "Your parking verification request is currently under review and has not yet been approved."
                    });
                }

                var allowedTypes = new[]
                {
                    "image/jpeg",
                    "image/jpg",
                    "image/png"
                };

                // Validate image files if provided
                if (request.ParkingImage != null && request.ParkingImage.Count > 0)
                {
                    foreach (var file in request.ParkingImage)
                    {
                        if (!allowedTypes.Contains(file.ContentType.ToLower()))
                        {
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

                    // Process image uploads if provided
                    if (request.ParkingImage != null && request.ParkingImage.Count > 0)
                    {
                        // Get the current max display order for existing images
                        var existingImages = await _context.ParkingSpotImages
                            .Where(psi => psi.ParkingSpotId == id)
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

                            // Create ParkingSpotImage with primary flag for first image
                            bool isPrimary = (displayOrder == 1 && !existingImages.Any());

                            var parkingSpotImage = new ParkingSpotImage
                            {
                                ParkingSpotId = id,
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
                        return StatusCode(
                       StatusCodes.Status500InternalServerError,
                       new ErrorResponse
                       {
                           Code = StatusCodes.Status500InternalServerError,
                           Success = false,
                           Message = "Please upload parking spot images."
                       });

                    }

                    // Create or update Availability based on DateType
                    var availability = new Availability
                    {
                        ParkingSpotId = id,
                        DayType = request.DayType,
                        StartTime = request.StartTime,
                        EndTime = request.EndTime,
                        EffectiveFrom = request.EffectiveFrom,
                        EffectiveUntil = request.EffectiveUntil,
                    };

                    _context.Availabilities.Add(availability);

                    // Update ParkingSpot pricing and status based on DateType
                    if (request.DayType == DayType.Weekday || request.DayType == DayType.Weekend)
                    {
                        // Use DailyRate for weekday/weekend
                        if (request.DailyRate.HasValue)
                        {
                            parkingSpot.DailyRate = request.DailyRate;
                        }   
                    }
                    else if (request.DayType == DayType.Everyday)
                    {
                        // Use MonthlyRate for everyday
                        if (request.MonthlyPrice.HasValue)
                        {
                            parkingSpot.MonthlyRate = request.MonthlyPrice;
                        }
                    }

                    // Update parking spot status
                    parkingSpot.AvailabilityStatus = AvailabilityStatus.Available;
                    parkingSpot.IsPublished = true;
                    parkingSpot.UpdatedAt = DateTime.UtcNow;

                    _context.ParkingSpots.Update(parkingSpot);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation("Parking spot configured. ParkingSpotId={ParkingSpotId}, DateType={DateType}, AvailabilityStatus={Status}",
                        id, request.DayType, parkingSpot.AvailabilityStatus);

                    // Commit transaction
                    await transaction.CommitAsync();

                    _logger.LogInformation("Parking configuration completed successfully. ParkingSpotId={ParkingSpotId}", id);

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
                    _logger.LogError(ex, "Parking configuration failed for UserId={UserId}, ParkingSpotId={ParkingSpotId}", userId, id);

                    return StatusCode(
                        StatusCodes.Status500InternalServerError,
                        new ErrorResponse
                        {
                            Code = StatusCodes.Status500InternalServerError,
                            Success = false,
                            Message = "An unexpected error occurred while configuring the parking spot."
                        });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in config-parking endpoint for ParkingSpotId={ParkingSpotId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    Code = StatusCodes.Status500InternalServerError,
                    Success = false,
                    Message = "An error occurred while configuring the parking spot"
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
        public async Task<ActionResult<DisplayMyParkingResponse>> DisplayMyParking()
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

                // Check if user is PropertyOwner
                if (user.UserType != UserType.PropertyOwner)
                {
                    _logger.LogWarning("Unauthorized access attempt. UserId={UserId}, UserType={UserType}", userId, user.UserType);
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
                    return Ok(new DisplayMyParkingResponse
                    {
                        Code = StatusCodes.Status200OK,
                        Success = true,
                        Message = "No parking spots found",
                        Data = new List<DisplayParkingSpotDTO>()
                    });
                }

                var result = parkingSpots
                    .Select(ps => MapToDisplayParkingSpotDTO(ps))
                    .ToList();

                _logger.LogInformation("Retrieved {Count} parking spots for owner {UserId}", result.Count, userId);

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
                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    Code = StatusCodes.Status500InternalServerError,
                    Success = false,
                    Message = "An error occurred while retrieving your parking spots"
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
                AvailabilityStatus = parkingSpot.AvailabilityStatus,
                VerificationStatus = parkingSpot.VerificationRequests.Single(v => v.IsCurrent).VerificationStatus,
                MonthlyRate = parkingSpot.MonthlyRate,
                DailyRate = parkingSpot.DailyRate,
                IsPublished = parkingSpot.IsPublished,
                CreatedAt = parkingSpot.CreatedAt,
                UpdatedAt = parkingSpot.UpdatedAt
            };
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
}