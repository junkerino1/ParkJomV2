using Google.Apis.Auth;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.Web.Helpers;
using ParkJomV2.Web.Models;

// ---- Google OAuth Authentication Controller ----
namespace ParkJomV2.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    // Simple user store (production should use EF Core + database)
    // TODO: Replace with DbContext.Users
    private static readonly Dictionary<string, (string UserId, string Name, string Picture, string Role)> _users = new();

    /// <summary>
    /// POST /api/auth/google-login
    /// Validates Google ID Token, creates or finds user, returns custom JWT
    /// </summary>
    [HttpPost("google-login")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
            return BadRequest(new { error = "IdToken is required." });

        try
        {
            // ---------- 1. Validate Google ID Token ----------
            // GoogleJsonWebSignature.ValidateAsync automatically validates signature, expiration,
            // audience (Client ID), etc. No extra configuration needed.
            var payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken);

            // Confirm token is valid and not tampered
            if (payload == null)
                return Unauthorized(new { error = "Invalid Google token." });

            var googleUserId = payload.Subject;   // Google's unique user ID
            var email = payload.Email;
            var name = payload.Name ?? email;
            var picture = payload.Picture ?? "";

            // ---------- 2. Find or create user ----------
            // TODO: Replace with await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email)
            string userId;
            string role;

            if (_users.TryGetValue(email, out var existingUser))
            {
                // Already exists: use directly
                userId = existingUser.UserId;
                role = existingUser.Role;
                Console.WriteLine($"🔑 Existing user login: {email} (Role: {role})");
            }
            else
            {
                // New user: auto-register with default role "Commuter"
                userId = Guid.NewGuid().ToString();
                role = "Commuter";

                _users[email] = (userId, name, picture, role);

                // TODO: Insert into database
                // _dbContext.Users.Add(new User { Id = userId, Email = email, Name = name, ... });
                // await _dbContext.SaveChangesAsync();

                Console.WriteLine($"🆕 New user registered: {email} | ID: {userId} | Role: {role}");
            }

            // ---------- 3. Generate custom JWT ----------
            var token = JwtHelper.GenerateToken(userId, email, name, picture, role);

            // ---------- 4. Return result ----------
            return Ok(new GoogleLoginResponseDto
            {
                Token = token,
                Email = email,
                Name = name,
                Picture = picture,
                Role = role,
            });
        }
        catch (InvalidJwtException)
        {
            return Unauthorized(new { error = "Google token validation failed." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Auth Error: {ex.Message}");
            return StatusCode(500, new { error = "Internal server error during authentication." });
        }
    }
}
