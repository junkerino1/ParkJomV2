using Google.Apis.Auth;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.Web.Helpers;
using ParkJomV2.Web.Models;
using ParkJomV2.Web.Services;
using System.ComponentModel.DataAnnotations;

// ---- Google OAuth Authentication Controller ----
namespace ParkJomV2.Web.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly ILogger<AuthController> _logger;
    private readonly IConfiguration _configuration;
    private readonly UserStoreService _userStore;

    public AuthController(
        ILogger<AuthController> logger,
        IConfiguration configuration,
        UserStoreService userStore)
    {
        _logger = logger;
        _configuration = configuration;
        _userStore = userStore;
    }

    /// <summary>
    /// POST /api/auth/google
    /// Validates Google ID Token, creates or finds user, returns custom JWT
    /// </summary>
    [HttpPost("google")]
    public async Task<ActionResult<AuthResponse>> GoogleLogin([FromBody] GoogleAuthRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GoogleToken))
            return BadRequest(new AuthResponse
            {
                Success = false,
                Message = "Google token is required."
            });

        try
        {
            // ---------- 1. Validate Google ID Token ----------
            var clientId = _configuration["GoogleOAuth:ClientId"];

            var payload = await GoogleJsonWebSignature.ValidateAsync(
                request.GoogleToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { clientId }
                });

            if (payload == null)
                return Unauthorized(new AuthResponse
                {
                    Success = false,
                    Message = "Invalid Google token."
                });

            var googleUserId = payload.Subject;
            var email = payload.Email;
            var name = payload.Name ?? email;
            var picture = payload.Picture ?? "";

            // ---------- 2. Find or create user ----------
            StoredUser? user = await _userStore.FindByEmailAsync(email);

            if (user != null)
            {
                user.LastLoginAt = DateTime.UtcNow;
                await _userStore.SaveUserAsync(user);
                _logger.LogInformation("Existing user login: {Email} (Role: {Role})", email, user.Role);
            }
            else
            {
                var names = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var firstName = names.FirstOrDefault() ?? name;
                var lastName = names.Length > 1 ? string.Join(" ", names.Skip(1)) : "";

                var nextId = await _userStore.GetNextUserIdAsync();
                user = new StoredUser
                {
                    UserId = nextId,
                    Email = email,
                    FirstName = firstName,
                    LastName = lastName,
                    ProfilePictureURL = picture,
                    UserType = 0, // Renter/Commuter
                    PhoneNumber = null,
                    IsProfileComplete = false,
                    CreatedAt = DateTime.UtcNow,
                    LastLoginAt = DateTime.UtcNow,
                    Role = "Commuter"
                };

                await _userStore.SaveUserAsync(user);
                _logger.LogInformation("New user registered: {Email} | ID: {UserId} | Role: {Role}", email, user.UserId, user.Role);
            }

            // ---------- 3. Return profile-complete response or full login ----------
            if (!user.IsProfileComplete)
            {
                return Ok(new AuthResponse
                {
                    Success = true,
                    Message = "Please complete your profile.",
                    IsProfileComplete = false,
                    JwtToken = null,
                    User = MapUser(user)
                });
            }

            // ---------- 4. Generate JWT ----------
            var token = JwtHelper.GenerateToken(
                user.UserId.ToString(),
                user.Email,
                $"{user.FirstName} {user.LastName}".Trim(),
                user.ProfilePictureURL ?? "",
                user.Role
            );

            return Ok(new AuthResponse
            {
                Success = true,
                Message = "Login successful.",
                JwtToken = token,
                IsProfileComplete = user.IsProfileComplete,
                User = MapUser(user)
            });
        }
        catch (InvalidJwtException)
        {
            return Unauthorized(new AuthResponse
            {
                Success = false,
                Message = "Google token validation failed."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auth error during Google login");
            return StatusCode(500, new AuthResponse
            {
                Success = false,
                Message = "Internal server error during authentication."
            });
        }
    }

    /// <summary>
    /// POST /api/auth/complete-profile
    /// Sets phone number and marks profile complete, then returns JWT
    /// </summary>
    [HttpPost("complete-profile")]
    public async Task<ActionResult<AuthResponse>> CompleteProfile([FromBody] CompleteProfileRequest request)
    {
        var user = await _userStore.FindByUserIdAsync(request.UserId);

        if (user == null)
        {
            return NotFound(new AuthResponse
            {
                Success = false,
                Message = "User not found. Please login first."
            });
        }

        user.PhoneNumber = request.PhoneNumber;
        user.IsProfileComplete = true;
        user.LastLoginAt = DateTime.UtcNow;

        await _userStore.SaveUserAsync(user);

        var token = JwtHelper.GenerateToken(
            user.UserId.ToString(),
            user.Email,
            $"{user.FirstName} {user.LastName}".Trim(),
            user.ProfilePictureURL ?? "",
            user.Role
        );

        _logger.LogInformation("Profile completed for {Email}", user.Email);

        return Ok(new AuthResponse
        {
            Success = true,
            Message = "Profile completed.",
            JwtToken = token,
            IsProfileComplete = true,
            User = MapUser(user)
        });
    }

    private static UserDTO MapUser(StoredUser user)
    {
        return new UserDTO
        {
            UserId = user.UserId,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            ProfilePictureURL = user.ProfilePictureURL,
            UserType = user.UserType,
            IsProfileComplete = user.IsProfileComplete,
            PhoneNumber = user.PhoneNumber,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt
        };
    }
}
