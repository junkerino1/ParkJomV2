using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;

namespace ParkJomV2.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {

        private readonly ApplicationDbContext _context;
        private readonly GoogleAuthService _googleService;
        private readonly JwtTokenService _jwtService;
        private readonly AccessLogService _accessLogService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            ApplicationDbContext context,
            GoogleAuthService googleService,
            JwtTokenService jwtService,
            AccessLogService accessLogService,
            ILogger<AuthController> logger)
        {
            _context = context;
            _googleService = googleService;
            _jwtService = jwtService;
            _accessLogService = accessLogService;
            _logger = logger;
        }

        [HttpPost("google")]
        public async Task<ActionResult<AuthResponse>> GoogleLogin([FromBody] GoogleAuthRequest request)
        {
            if (!ModelState.IsValid)
            {
                await _accessLogService.LogAsync(User, "GoogleLogin", false, "Invalid request");
                return BadRequest(new AuthResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Invalid request."
                });
            }

            // Step 1 - Validate Google token
            var googleUser = await _googleService.ValidateGoogleTokenAsync(request.GoogleToken);

            if (googleUser == null)
            {
                await _accessLogService.LogAsync(User, "GoogleLogin", false, "Invalid Google token");
                return Unauthorized(new AuthResponse
                {
                    Code = StatusCodes.Status401Unauthorized,
                    Success = false,
                    Message = "Invalid Google token."
                });
            }

            // Step 2 - Search user
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.GoogleId == googleUser.Sub);

            // Step 3 - New user
            if (user == null)
            {
                var names = googleUser.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                var firstName = names.FirstOrDefault() ?? "";
                var lastName = names.Length > 1
                    ? string.Join(" ", names.Skip(1))
                    : "";

                user = new User
                {
                    GoogleId = googleUser.Sub,
                    Email = googleUser.Email,
                    FirstName = firstName,
                    LastName = lastName,
                    ProfilePictureURL = googleUser.Picture,

                    UserType = UserType.Renter,
                    PhoneNumber = null,

                    IsProfileComplete = false,

                    AccountStatus = "Active",

                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    LastLoginAt = DateTime.UtcNow
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                _logger.LogInformation("New user created: {Email}", user.Email);
            }
            else
            {
                if (string.Equals(
                    user.AccountStatus,
                    "Suspended",
                    StringComparison.OrdinalIgnoreCase))
                {
                    await _accessLogService.LogAsync(
                        User,
                        "GoogleLogin",
                        false,
                        $"Suspended account login attempt: UserId={user.UserId}");

                    return StatusCode(StatusCodes.Status403Forbidden, new AuthResponse
                    {
                        Code = StatusCodes.Status403Forbidden,
                        Success = false,
                        Message = "Your account is suspended. Please contact support."
                    });
                }

                if (string.Equals(
                    user.AccountStatus,
                    "Deactivated",
                    StringComparison.OrdinalIgnoreCase))
                {
                    await _accessLogService.LogAsync(
                        User,
                        "GoogleLogin",
                        false,
                        $"Deactivated account login attempt: UserId={user.UserId}");

                    return StatusCode(StatusCodes.Status403Forbidden, new AuthResponse
                    {
                        Code = StatusCodes.Status403Forbidden,
                        Success = false,
                        Message = "Your account is deactivated. Please contact support to reactivate."
                    });
                }

                user.LastLoginAt = DateTime.UtcNow;
                user.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
            }

            var token = _jwtService.GenerateToken(user);

            // Step 4 - Profile incomplete (return JWT so CompleteProfile is authenticated)
            if (!user.IsProfileComplete)
            {
                await _accessLogService.LogAsync(User, "GoogleLogin", true, "Profile incomplete");
                return Ok(new AuthResponse
                {
                    Code = StatusCodes.Status200OK,
                    Success = true,
                    Message = "Please complete your profile.",
                    JwtToken = token,
                    IsProfileComplete = false,
                    User = MapUser(user)
                });
            }

            // Step 5 - Login complete
            await _accessLogService.LogAsync(User, "GoogleLogin", true, $"UserId={user.UserId}");

            return Ok(new AuthResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Login successful.",
                JwtToken = token,
                IsProfileComplete = true,
                User = MapUser(user)
            });
        }

        [Authorize]
        [HttpPost("complete-profile")]
        public async Task<ActionResult<AuthResponse>> CompleteProfile([FromBody] CompleteProfileRequest request)
        {
            if (!ModelState.IsValid)
            {
                await _accessLogService.LogAsync(User, "CompleteProfile", false, "Invalid request");
                return BadRequest(new AuthResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Invalid request."
                });
            }

            var claimUserIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(claimUserIdText, out var authenticatedUserId))
            {
                await _accessLogService.LogAsync(User, "CompleteProfile", false, "Missing authenticated user claim");
                return Unauthorized(new AuthResponse
                {
                    Code = StatusCodes.Status401Unauthorized,
                    Success = false,
                    Message = "Authentication required."
                });
            }

            if (request.UserId.HasValue && request.UserId.Value != authenticatedUserId)
            {
                await _accessLogService.LogAsync(User, "CompleteProfile", false, $"UserId mismatch (claim={authenticatedUserId}, request={request.UserId})");
                return StatusCode(StatusCodes.Status403Forbidden, new AuthResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "You can only complete your own profile."
                });
            }

            var user = await _context.Users
                .Include(u => u.Wallet)
                .FirstOrDefaultAsync(u => u.UserId == authenticatedUserId);

            if (user == null)
            {
                await _accessLogService.LogAsync(User, "CompleteProfile", false, $"User not found (id={authenticatedUserId})");
                return NotFound(new AuthResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "User not found."
                });
            }

            if (string.Equals(
                user.AccountStatus,
                "Suspended",
                StringComparison.OrdinalIgnoreCase))
            {
                await _accessLogService.LogAsync(
                    User,
                    "CompleteProfile",
                    false,
                    $"Suspended account attempted profile completion: UserId={user.UserId}");

                return StatusCode(StatusCodes.Status403Forbidden, new AuthResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Your account is suspended. Please contact support."
                });
            }

            if (string.Equals(
                user.AccountStatus,
                "Deactivated",
                StringComparison.OrdinalIgnoreCase))
            {
                await _accessLogService.LogAsync(
                    User,
                    "CompleteProfile",
                    false,
                    $"Deactivated account attempted profile completion: UserId={user.UserId}");

                return StatusCode(StatusCodes.Status403Forbidden, new AuthResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Your account is deactivated. Please contact support to reactivate."
                });
            }

            if (user.IsProfileComplete)
            {
                await _accessLogService.LogAsync(User, "CompleteProfile", false, $"Profile already complete: UserId={user.UserId}");
                return BadRequest(new AuthResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Profile is already completed."
                });
            }

            if (user.UserType == UserType.Admin || request.UserType == UserType.Admin)
            {
                await _accessLogService.LogAsync(User, "CompleteProfile", false, $"Admin role rejected: UserId={user.UserId}");
                return BadRequest(new AuthResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Admin role cannot be self-assigned."
                });
            }

            user.PhoneNumber = request.PhoneNumber;
            user.IsProfileComplete = true;
            user.UpdatedAt = DateTime.UtcNow;

            if (request.UserType.HasValue)
            {
                user.UserType = request.UserType.Value;
            }

            if (user.Wallet == null)
            {
                _context.Wallets.Add(new Wallet
                {
                    UserId = user.UserId,
                    Status = WalletStatus.Active,
                    Balance = 0,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            var token = _jwtService.GenerateToken(user);

            await _accessLogService.LogAsync(User, "CompleteProfile", true, $"UserId={user.UserId}");

            return Ok(new AuthResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Profile completed.",
                JwtToken = token,
                IsProfileComplete = true,
                User = MapUser(user)
            });
        }

        private static UserDTO MapUser(User user)
        {
            return new UserDTO
            {
                UserId = user.UserId,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                ProfilePictureURL = user.ProfilePictureURL,
                UserType = user.UserType,
                PhoneNumber = user.PhoneNumber,
                IsProfileComplete = user.IsProfileComplete,
                AccountStatus = user.AccountStatus,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt ?? DateTime.UtcNow
            };
        }

    }
}
