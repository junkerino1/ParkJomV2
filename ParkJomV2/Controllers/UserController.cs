using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    private const long MaxProfilePictureSizeBytes = 5 * 1024 * 1024; // 5 MB
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };
    private static readonly HashSet<string> AllowedAccountStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Active", "Suspended", "Deactivated"
    };

    private readonly ApplicationDbContext _context;
    private readonly CurrentUserService _currentUser;
    private readonly JwtTokenService _jwtService;
    private readonly CloudinaryService _cloudinaryService;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<UserController> _logger;

    public UserController(
        ApplicationDbContext context,
        CurrentUserService currentUser,
        JwtTokenService jwtService,
        CloudinaryService cloudinaryService,
        AccessLogService accessLogService,
        ILogger<UserController> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _jwtService = jwtService;
        _cloudinaryService = cloudinaryService;
        _accessLogService = accessLogService;
        _logger = logger;
    }

    // =========================================================================
    // 1. SELF-SERVICE PROFILE ENDPOINTS (/api/user/*)
    // =========================================================================

    /// <summary>
    /// Retrieve the authenticated user's full profile and live summary metrics.
    /// </summary>
    [Authorize]
    [HttpGet("profile")]
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserProfileResponse>> GetMyProfile(CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
        {
            await _accessLogService.LogAsync(User, "GetMyProfile", false, "Unauthorized");
            return Unauthorized(new ErrorResponse
            {
                Code = StatusCodes.Status401Unauthorized,
                Success = false,
                Message = "Unauthorized."
            });
        }

        var profileDto = await BuildUserProfileDtoAsync(userId.Value, cancellationToken);
        if (profileDto == null)
        {
            await _accessLogService.LogAsync(User, "GetMyProfile", false, $"User not found (id={userId.Value})");
            return NotFound(new ErrorResponse
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "User profile not found."
            });
        }

        await _accessLogService.LogAsync(User, "GetMyProfile", true, $"UserId={userId.Value}");
        return Ok(new UserProfileResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "User profile retrieved successfully.",
            Data = profileDto
        });
    }

    /// <summary>
    /// Update the authenticated user's personal profile details (FirstName, LastName, PhoneNumber).
    /// Returns the updated profile and a refreshed JWT token.
    /// </summary>
    [Authorize]
    [HttpPut("profile")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserProfileResponse>> UpdateMyProfile(
        [FromBody] UpdateUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await _accessLogService.LogAsync(User, "UpdateMyProfile", false, "Invalid request");
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Invalid profile update request."
            });
        }

        var userId = _currentUser.UserId;
        if (!userId.HasValue)
        {
            return Unauthorized(new ErrorResponse
            {
                Code = StatusCodes.Status401Unauthorized,
                Success = false,
                Message = "Unauthorized."
            });
        }

        var user = await _context.Users
            .Include(u => u.Wallet)
            .FirstOrDefaultAsync(u => u.UserId == userId.Value, cancellationToken);

        if (user == null)
        {
            await _accessLogService.LogAsync(User, "UpdateMyProfile", false, $"User not found (id={userId.Value})");
            return NotFound(new ErrorResponse
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "User not found."
            });
        }

        if (request.FirstName != null)
        {
            var trimmedFirst = request.FirstName.Trim();
            if (string.IsNullOrWhiteSpace(trimmedFirst))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "First name cannot be empty."
                });
            }
            user.FirstName = trimmedFirst;
        }

        if (request.LastName != null)
        {
            user.LastName = request.LastName.Trim();
        }

        if (request.PhoneNumber != null)
        {
            var trimmedPhone = request.PhoneNumber.Trim();
            if (string.IsNullOrWhiteSpace(trimmedPhone))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Phone number cannot be empty."
                });
            }
            user.PhoneNumber = trimmedPhone;
        }

        if (!string.IsNullOrWhiteSpace(user.FirstName) && !string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            user.IsProfileComplete = true;

            if (user.Wallet == null)
            {
                _context.Wallets.Add(new Wallet
                {
                    UserId = user.UserId,
                    Status = WalletStatus.Active,
                    Balance = 0m,
                    OnHold = 0m,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        var refreshedToken = _jwtService.GenerateToken(user);
        var profileDto = await BuildUserProfileDtoAsync(user.UserId, cancellationToken);

        await _accessLogService.LogAsync(User, "UpdateMyProfile", true, $"UserId={user.UserId}");
        return Ok(new UserProfileResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Profile updated successfully.",
            Data = profileDto,
            JwtToken = refreshedToken
        });
    }

    /// <summary>
    /// Upload and set a new profile picture for the authenticated user via Cloudinary.
    /// </summary>
    [Authorize]
    [HttpPost("profile/picture")]
    [HttpPut("profile/picture")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserProfileResponse>> UploadProfilePicture(
        [FromForm] UploadProfilePictureRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _currentUser.GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return NotFound(new ErrorResponse
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "User not found."
            });
        }

        var file = request.Picture;
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Please provide a valid image file."
            });
        }

        if (file.Length > MaxProfilePictureSizeBytes)
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Profile picture size cannot exceed 5 MB."
            });
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedImageExtensions.Contains(extension))
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Only .jpg, .jpeg, .png, and .webp image files are allowed."
            });
        }

        try
        {
            var folder = $"parkjom/users/{user.UserId}/avatar";
            var uploadResult = await _cloudinaryService.UploadImageAsync(file, folder);

            if (uploadResult.Error != null || string.IsNullOrWhiteSpace(uploadResult.SecureUrl?.ToString()))
            {
                _logger.LogWarning("Cloudinary upload failed for UserId={UserId}: {Error}", user.UserId, uploadResult.Error?.Message);
                await _accessLogService.LogAsync(User, "UploadProfilePicture", false, uploadResult.Error?.Message ?? "Upload failed");
                return StatusCode(StatusCodes.Status502BadGateway, new ErrorResponse
                {
                    Code = StatusCodes.Status502BadGateway,
                    Success = false,
                    Message = "Failed to upload profile picture to media storage."
                });
            }

            var mediaFile = new MediaFile
            {
                PublicId = uploadResult.PublicId ?? string.Empty,
                SecureUrl = uploadResult.SecureUrl.ToString(),
                ResourceType = string.IsNullOrWhiteSpace(uploadResult.ResourceType) ? "image" : uploadResult.ResourceType,
                Format = string.IsNullOrWhiteSpace(uploadResult.Format) ? extension.TrimStart('.').ToLowerInvariant() : uploadResult.Format,
                OriginalFileName = file.FileName,
                Folder = folder,
                UploadedBy = user.UserId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.MediaFiles.Add(mediaFile);
            user.ProfilePictureURL = mediaFile.SecureUrl;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            var profileDto = await BuildUserProfileDtoAsync(user.UserId, cancellationToken);
            await _accessLogService.LogAsync(User, "UploadProfilePicture", true, $"UserId={user.UserId}, MediaFileId={mediaFile.MediaFileId}");

            return Ok(new UserProfileResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Profile picture updated successfully.",
                Data = profileDto
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading profile picture for UserId={UserId}", user.UserId);
            await _accessLogService.LogAsync(User, "UploadProfilePicture", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while uploading the profile picture."
            });
        }
    }

    /// <summary>
    /// Remove the authenticated user's profile picture.
    /// </summary>
    [Authorize]
    [HttpDelete("profile/picture")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserProfileResponse>> RemoveProfilePicture(CancellationToken cancellationToken)
    {
        var user = await _currentUser.GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return NotFound(new ErrorResponse
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "User not found."
            });
        }

        user.ProfilePictureURL = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        var profileDto = await BuildUserProfileDtoAsync(user.UserId, cancellationToken);
        await _accessLogService.LogAsync(User, "RemoveProfilePicture", true, $"UserId={user.UserId}");

        return Ok(new UserProfileResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Profile picture removed successfully.",
            Data = profileDto
        });
    }

    /// <summary>
    /// Deactivate the authenticated user's account if there are no active/confirmed bookings.
    /// Automatically unpublishes any owned parking spots.
    /// </summary>
    [Authorize]
    [HttpDelete("profile")]
    [HttpPost("profile/deactivate")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserProfileResponse>> DeactivateMyAccount(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] DeactivateAccountRequest? request,
        CancellationToken cancellationToken)
    {
        var user = await _currentUser.GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return NotFound(new ErrorResponse
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "User not found."
            });
        }

        if (user.UserType == UserType.Admin)
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Admin accounts cannot be self-deactivated."
            });
        }

        if (request != null && !request.ConfirmDeactivation)
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Account deactivation must be confirmed."
            });
        }

        var now = DateTime.UtcNow;

        // Check active/upcoming bookings as a commuter
        var hasActiveCommuterBookings = await _context.Bookings.AnyAsync(
            b => b.RenterId == user.UserId &&
                 (b.BookingStatus == BookingStatus.Confirmed || b.BookingStatus == BookingStatus.Active) &&
                 b.EndDate >= now,
            cancellationToken);

        if (hasActiveCommuterBookings)
        {
            await _accessLogService.LogAsync(User, "DeactivateMyAccount", false, "Has active commuter bookings");
            return Conflict(new ErrorResponse
            {
                Code = StatusCodes.Status409Conflict,
                Success = false,
                Message = "Cannot deactivate account while you have active or upcoming confirmed bookings."
            });
        }

        // Check active/upcoming bookings on owned parking spots
        var hasActiveOwnerBookings = await _context.Bookings.AnyAsync(
            b => b.ParkingSpot.OwnerId == user.UserId &&
                 (b.BookingStatus == BookingStatus.Confirmed || b.BookingStatus == BookingStatus.Active) &&
                 b.EndDate >= now,
            cancellationToken);

        if (hasActiveOwnerBookings)
        {
            await _accessLogService.LogAsync(User, "DeactivateMyAccount", false, "Has active bookings on owned spots");
            return Conflict(new ErrorResponse
            {
                Code = StatusCodes.Status409Conflict,
                Success = false,
                Message = "Cannot deactivate account while your parking spots have active or upcoming confirmed bookings."
            });
        }

        // Unpublish all owned spots
        var ownedSpots = await _context.ParkingSpots
            .Where(p => p.OwnerId == user.UserId && p.AvailabilityStatus != AvailabilityStatus.Deleted)
            .ToListAsync(cancellationToken);

        foreach (var spot in ownedSpots)
        {
            spot.IsPublished = false;
            if (spot.AvailabilityStatus == AvailabilityStatus.Available)
            {
                spot.AvailabilityStatus = AvailabilityStatus.Inactive;
            }
            spot.UpdatedAt = now;
        }

        user.AccountStatus = "Deactivated";
        user.UpdatedAt = now;

        await _context.SaveChangesAsync(cancellationToken);

        var profileDto = await BuildUserProfileDtoAsync(user.UserId, cancellationToken);
        var reasonDetail = string.IsNullOrWhiteSpace(request?.Reason) ? "No reason provided" : request.Reason.Trim();
        await _accessLogService.LogAsync(User, "DeactivateMyAccount", true, $"UserId={user.UserId}, Reason={reasonDetail}");

        return Ok(new UserProfileResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Your account has been deactivated.",
            Data = profileDto
        });
    }

    // =========================================================================
    // 2. ADMIN USER PROFILE GOVERNANCE ENDPOINTS (/api/admin/users/*)
    // =========================================================================

    /// <summary>
    /// Admin: List and search all user profiles with filtering and pagination.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpGet("/api/admin/users")]
    [ProducesResponseType(typeof(AdminUserListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AdminUserListResponse>> GetAllUsers(
        [FromQuery] string? search = null,
        [FromQuery] UserType? userType = null,
        [FromQuery] string? accountStatus = null,
        [FromQuery] bool? isProfileComplete = null,
        [FromQuery, Range(1, 100000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(u =>
                u.Email.ToLower().Contains(term) ||
                (u.FirstName != null && u.FirstName.ToLower().Contains(term)) ||
                (u.LastName != null && u.LastName.ToLower().Contains(term)) ||
                (u.PhoneNumber != null && u.PhoneNumber.ToLower().Contains(term)));
        }

        if (userType.HasValue)
        {
            query = query.Where(u => u.UserType == userType.Value);
        }

        if (!string.IsNullOrWhiteSpace(accountStatus))
        {
            var normalizedStatus = accountStatus.Trim().ToLower();
            query = query.Where(u => u.AccountStatus.ToLower() == normalizedStatus);
        }

        if (isProfileComplete.HasValue)
        {
            query = query.Where(u => u.IsProfileComplete == isProfileComplete.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);
        var now = DateTime.UtcNow;

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserProfileDTO
            {
                UserId = u.UserId,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                FullName = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
                PhoneNumber = u.PhoneNumber,
                ProfilePictureURL = u.ProfilePictureURL,
                UserType = u.UserType,
                RoleName = u.UserType.ToString(),
                IsProfileComplete = u.IsProfileComplete,
                AccountStatus = u.AccountStatus,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt,
                LastLoginAt = u.LastLoginAt,
                Summary = new UserProfileSummaryDTO
                {
                    WalletBalance = u.Wallet != null ? u.Wallet.Balance : 0m,
                    WalletOnHold = u.Wallet != null ? u.Wallet.OnHold : 0m,
                    WalletCurrency = "MYR",
                    WalletStatus = u.Wallet != null ? u.Wallet.Status.ToString() : "None",
                    VehicleCount = u.Vehicles.Count(),
                    ActiveBookingsCount = u.Bookings.Count(b =>
                        (b.BookingStatus == BookingStatus.Confirmed || b.BookingStatus == BookingStatus.Active) &&
                        b.EndDate >= now),
                    TotalBookingsCount = u.Bookings.Count(),
                    FavoritesCount = u.Favorites.Count(),
                    OwnedParkingSpotsCount = u.OwnedParkingSpots.Count(p => p.AvailabilityStatus != AvailabilityStatus.Deleted),
                    OpenSupportTicketsCount = _context.SupportTickets.Count(t =>
                        t.CustomerUserId == u.UserId &&
                        t.Status != SupportTicketStatus.Resolved &&
                        t.Status != SupportTicketStatus.Closed &&
                        t.Status != SupportTicketStatus.Cancelled &&
                        t.Status != SupportTicketStatus.Duplicate)
                }
            })
            .ToListAsync(cancellationToken);

        await _accessLogService.LogAsync(User, "AdminGetAllUsers", true, $"Count={users.Count}, Page={page}");

        return Ok(new AdminUserListResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Users retrieved successfully.",
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            HasNextPage = page < totalPages,
            Data = users
        });
    }

    /// <summary>
    /// Admin: Retrieve full profile details, vehicles, wallet summary, and owned spots for a specific user.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpGet("/api/admin/users/{userId:int}")]
    [ProducesResponseType(typeof(AdminUserDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDetailResponse>> GetUserByIdForAdmin(
        int userId,
        CancellationToken cancellationToken)
    {
        var detailDto = await BuildAdminUserDetailDtoAsync(userId, cancellationToken);
        if (detailDto == null)
        {
            await _accessLogService.LogAsync(User, "AdminGetUserById", false, $"User not found (id={userId})");
            return NotFound(new ErrorResponse
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "User not found."
            });
        }

        await _accessLogService.LogAsync(User, "AdminGetUserById", true, $"TargetUserId={userId}");
        return Ok(new AdminUserDetailResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "User profile detail retrieved successfully.",
            Data = detailDto
        });
    }

    /// <summary>
    /// Admin: Update a user's profile fields, role (UserType), completion flag, or AccountStatus.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPut("/api/admin/users/{userId:int}")]
    [ProducesResponseType(typeof(AdminUserDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDetailResponse>> AdminUpdateUserProfile(
        int userId,
        [FromBody] AdminUpdateUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Invalid admin user update request."
            });
        }

        var targetUser = await _context.Users
            .Include(u => u.Wallet)
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);

        if (targetUser == null)
        {
            await _accessLogService.LogAsync(User, "AdminUpdateUserProfile", false, $"User not found (id={userId})");
            return NotFound(new ErrorResponse
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "User not found."
            });
        }

        var currentAdminId = _currentUser.UserId;
        var isSelf = currentAdminId.HasValue && currentAdminId.Value == targetUser.UserId;

        if (request.FirstName != null)
        {
            var trimmedFirst = request.FirstName.Trim();
            if (string.IsNullOrWhiteSpace(trimmedFirst))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "First name cannot be empty."
                });
            }
            targetUser.FirstName = trimmedFirst;
        }

        if (request.LastName != null)
        {
            targetUser.LastName = request.LastName.Trim();
        }

        if (request.PhoneNumber != null)
        {
            targetUser.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
                ? null
                : request.PhoneNumber.Trim();
        }

        if (request.UserType.HasValue)
        {
            if (isSelf && request.UserType.Value != UserType.Admin)
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "You cannot demote your own admin role."
                });
            }
            targetUser.UserType = request.UserType.Value;
        }

        if (request.IsProfileComplete.HasValue)
        {
            targetUser.IsProfileComplete = request.IsProfileComplete.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.AccountStatus))
        {
            var trimmedStatus = request.AccountStatus.Trim();
            if (!AllowedAccountStatuses.Contains(trimmedStatus))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "AccountStatus must be 'Active', 'Suspended', or 'Deactivated'."
                });
            }

            if (isSelf && !string.Equals(trimmedStatus, "Active", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "You cannot suspend or deactivate your own admin account."
                });
            }

            // Normalize casing
            var normalizedStatus = AllowedAccountStatuses.First(s =>
                string.Equals(s, trimmedStatus, StringComparison.OrdinalIgnoreCase));

            var previousStatus = targetUser.AccountStatus;
            targetUser.AccountStatus = normalizedStatus;

            // Sync parking spot suspension state when owner status changes
            if (targetUser.UserType == UserType.PropertyOwner &&
                !string.Equals(previousStatus, normalizedStatus, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(normalizedStatus, "Suspended", StringComparison.OrdinalIgnoreCase))
                {
                    var availableSpots = await _context.ParkingSpots
                        .Where(p => p.OwnerId == targetUser.UserId && p.AvailabilityStatus == AvailabilityStatus.Available)
                        .ToListAsync(cancellationToken);

                    foreach (var spot in availableSpots)
                    {
                        spot.AvailabilityStatus = AvailabilityStatus.Suspended;
                        spot.UpdatedAt = DateTime.UtcNow;
                    }
                }
                else if (string.Equals(normalizedStatus, "Active", StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(previousStatus, "Suspended", StringComparison.OrdinalIgnoreCase))
                {
                    var suspendedSpots = await _context.ParkingSpots
                        .Where(p => p.OwnerId == targetUser.UserId && p.AvailabilityStatus == AvailabilityStatus.Suspended)
                        .ToListAsync(cancellationToken);

                    foreach (var spot in suspendedSpots)
                    {
                        spot.AvailabilityStatus = AvailabilityStatus.Available;
                        spot.UpdatedAt = DateTime.UtcNow;
                    }
                }
            }
        }

        if (targetUser.IsProfileComplete && targetUser.Wallet == null)
        {
            _context.Wallets.Add(new Wallet
            {
                UserId = targetUser.UserId,
                Status = WalletStatus.Active,
                Balance = 0m,
                OnHold = 0m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        targetUser.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        var updatedDetail = await BuildAdminUserDetailDtoAsync(targetUser.UserId, cancellationToken);
        await _accessLogService.LogAsync(
            User,
            "AdminUpdateUserProfile",
            true,
            $"TargetUserId={targetUser.UserId}, Role={targetUser.UserType}, Status={targetUser.AccountStatus}");

        return Ok(new AdminUserDetailResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "User profile updated successfully by admin.",
            Data = updatedDetail
        });
    }

    // =========================================================================
    // HELPER PROJECTION METHODS
    // =========================================================================

    private async Task<UserProfileDTO?> BuildUserProfileDtoAsync(int userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        return await _context.Users
            .AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => new UserProfileDTO
            {
                UserId = u.UserId,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                FullName = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
                PhoneNumber = u.PhoneNumber,
                ProfilePictureURL = u.ProfilePictureURL,
                UserType = u.UserType,
                RoleName = u.UserType.ToString(),
                IsProfileComplete = u.IsProfileComplete,
                AccountStatus = u.AccountStatus,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt,
                LastLoginAt = u.LastLoginAt,
                Summary = new UserProfileSummaryDTO
                {
                    WalletBalance = u.Wallet != null ? u.Wallet.Balance : 0m,
                    WalletOnHold = u.Wallet != null ? u.Wallet.OnHold : 0m,
                    WalletCurrency = "MYR",
                    WalletStatus = u.Wallet != null ? u.Wallet.Status.ToString() : "None",
                    VehicleCount = u.Vehicles.Count(),
                    ActiveBookingsCount = u.Bookings.Count(b =>
                        (b.BookingStatus == BookingStatus.Confirmed || b.BookingStatus == BookingStatus.Active) &&
                        b.EndDate >= now),
                    TotalBookingsCount = u.Bookings.Count(),
                    FavoritesCount = u.Favorites.Count(),
                    OwnedParkingSpotsCount = u.OwnedParkingSpots.Count(p => p.AvailabilityStatus != AvailabilityStatus.Deleted),
                    OpenSupportTicketsCount = _context.SupportTickets.Count(t =>
                        t.CustomerUserId == u.UserId &&
                        t.Status != SupportTicketStatus.Resolved &&
                        t.Status != SupportTicketStatus.Closed &&
                        t.Status != SupportTicketStatus.Cancelled &&
                        t.Status != SupportTicketStatus.Duplicate)
                }
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<AdminUserDetailDTO?> BuildAdminUserDetailDtoAsync(int userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        return await _context.Users
            .AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => new AdminUserDetailDTO
            {
                UserId = u.UserId,
                GoogleId = u.GoogleId,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                FullName = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
                PhoneNumber = u.PhoneNumber,
                ProfilePictureURL = u.ProfilePictureURL,
                UserType = u.UserType,
                RoleName = u.UserType.ToString(),
                IsProfileComplete = u.IsProfileComplete,
                AccountStatus = u.AccountStatus,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt,
                LastLoginAt = u.LastLoginAt,
                Summary = new UserProfileSummaryDTO
                {
                    WalletBalance = u.Wallet != null ? u.Wallet.Balance : 0m,
                    WalletOnHold = u.Wallet != null ? u.Wallet.OnHold : 0m,
                    WalletCurrency = "MYR",
                    WalletStatus = u.Wallet != null ? u.Wallet.Status.ToString() : "None",
                    VehicleCount = u.Vehicles.Count(),
                    ActiveBookingsCount = u.Bookings.Count(b =>
                        (b.BookingStatus == BookingStatus.Confirmed || b.BookingStatus == BookingStatus.Active) &&
                        b.EndDate >= now),
                    TotalBookingsCount = u.Bookings.Count(),
                    FavoritesCount = u.Favorites.Count(),
                    OwnedParkingSpotsCount = u.OwnedParkingSpots.Count(p => p.AvailabilityStatus != AvailabilityStatus.Deleted),
                    OpenSupportTicketsCount = _context.SupportTickets.Count(t =>
                        t.CustomerUserId == u.UserId &&
                        t.Status != SupportTicketStatus.Resolved &&
                        t.Status != SupportTicketStatus.Closed &&
                        t.Status != SupportTicketStatus.Cancelled &&
                        t.Status != SupportTicketStatus.Duplicate)
                },
                Vehicles = u.Vehicles
                    .OrderByDescending(v => v.CreatedAt)
                    .Select(v => new VehicleResponseDTO
                    {
                        VehicleId = v.VehicleId,
                        NumberPlate = v.NumberPlate,
                        VehicleBrand = v.VehicleBrand,
                        VehicleModel = v.VehicleModel,
                        VehicleColor = v.VehicleColor,
                        CreatedAt = v.CreatedAt,
                        UpdatedAt = v.UpdatedAt,
                        OwnerEmail = u.Email,
                        OwnerName = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim()
                    })
                    .ToList(),
                OwnedParkingSpots = u.OwnedParkingSpots
                    .Where(p => p.AvailabilityStatus != AvailabilityStatus.Deleted)
                    .OrderByDescending(p => p.CreatedAt)
                    .Select(p => new AdminUserParkingSpotSummaryDTO
                    {
                        ParkingSpotId = p.ParkingSpotId,
                        ParkingLabel = p.ParkingLabel ?? string.Empty,
                        PropertyName = p.Property != null ? p.Property.PropertyName : string.Empty,
                        AvailabilityStatus = p.AvailabilityStatus.ToString(),
                        IsPublished = p.IsPublished,
                        DailyRate = p.DailyRate,
                        MonthlyRate = p.MonthlyRate,
                        CreatedAt = p.CreatedAt
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);
    }
}
