using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ParkJomV2.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : Controller
    {

        private readonly ApplicationDbContext _context;
        private readonly GoogleAuthService _googleService;
        private readonly JwtTokenService _jwtService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            ApplicationDbContext context,
            GoogleAuthService googleService,
            JwtTokenService jwtService,
            ILogger<AuthController> logger)
        {
            _context = context;
            _googleService = googleService;
            _jwtService = jwtService;
            _logger = logger;
        }

        [HttpPost("google")]
        public async Task<ActionResult<AuthResponseDTO>> GoogleLogin([FromBody] GoogleAuthRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new AuthResponseDTO
                {
                    Success = false,
                    Message = "Invalid request."
                });
            }

            // Step 1 - Validate Google token
            var googleUser = await _googleService.ValidateGoogleTokenAsync(request.GoogleToken);

            if (googleUser == null)
            {
                return Unauthorized(new AuthResponseDTO
                {
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
                user.LastLoginAt = DateTime.UtcNow;
                user.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
            }

            // Step 4 - Profile incomplete
            if (!user.IsProfileComplete)
            {
                return Ok(new AuthResponseDTO
                {
                    Success = true,
                    Message = "Please complete your profile.",
                    IsProfileComplete = false,
                    User = MapUser(user)
                });
            }

            // Step 5 - Generate JWT

            var token = _jwtService.GenerateToken(user);

            return Ok(new AuthResponseDTO
            {
                Success = true,
                Message = "Login successful.",
                JwtToken = token,
                IsProfileComplete = true,
                User = MapUser(user)
            });
        }

        [HttpPost("complete-profile")]
        public async Task<ActionResult<AuthResponseDTO>> CompleteProfile([FromBody] CompleteProfileRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var user = await _context.Users
                .Include(u => u.Wallet)
                .FirstOrDefaultAsync(u => u.UserId == request.UserId);

            if (user == null)
            {
                return NotFound(new AuthResponseDTO
                {
                    Success = false,
                    Message = "User not found."
                });
            }

            user.PhoneNumber = request.PhoneNumber;
            user.IsProfileComplete = true;
            user.UpdatedAt = DateTime.UtcNow;

            if (user.Wallet == null)
            {
                _context.Wallets.Add(new Wallet
                {
                    UserId = user.UserId,
                    Balance = 0,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            var token = _jwtService.GenerateToken(user);

            return Ok(new AuthResponseDTO
            {
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
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt ?? DateTime.UtcNow
            };
        }

    }
}