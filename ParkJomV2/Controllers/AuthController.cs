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
using System.Security.Cryptography;
using System.Text;
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
        private readonly ITokenBlacklistService _blacklistService;
        private readonly AccessLogService _accessLogService;
        private readonly ILogger<AuthController> _logger;
        private readonly Sms360Service _sms360;
        private readonly IConfiguration _configuration;
        private static readonly TimeSpan PhoneOtpLifetime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan PhoneOtpCooldown = TimeSpan.FromSeconds(60);
        private const int MaximumPhoneOtpAttempts = 5;

        public AuthController(
            ApplicationDbContext context,
            GoogleAuthService googleService,
            JwtTokenService jwtService,
            ITokenBlacklistService blacklistService,
            AccessLogService accessLogService,
            ILogger<AuthController> logger,
            Sms360Service sms360,
            IConfiguration configuration)
        {
            _context = context;
            _googleService = googleService;
            _jwtService = jwtService;
            _blacklistService = blacklistService;
            _accessLogService = accessLogService;
            _logger = logger;
            _sms360 = sms360;
            _configuration = configuration;
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

        /// <summary>Saves the submitted phone number and sends a six-digit verification code. Does not complete the profile.</summary>
        [Authorize]
        [HttpPost("complete-profile")]
        [ProducesResponseType(typeof(PhoneOtpFlowResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(PhoneOtpFlowResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(PhoneOtpFlowResponse), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(PhoneOtpFlowResponse), StatusCodes.Status502BadGateway)]
        public async Task<ActionResult<PhoneOtpFlowResponse>> CompleteProfile(
            [FromBody] CompleteProfileRequest request,
            CancellationToken cancellationToken)
        {
            var claimUserIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(claimUserIdText, out var authenticatedUserId))
            {
                await _accessLogService.LogAsync(User, "CompleteProfile", false, "Missing authenticated user claim");
                return Unauthorized(PhoneOtpError(StatusCodes.Status401Unauthorized, "Authentication required."));
            }

            if (request.UserId.HasValue && request.UserId.Value != authenticatedUserId)
            {
                await _accessLogService.LogAsync(User, "CompleteProfile", false, $"UserId mismatch (claim={authenticatedUserId}, request={request.UserId})");
                return StatusCode(StatusCodes.Status403Forbidden, PhoneOtpError(StatusCodes.Status403Forbidden, "You can only complete your own profile."));
            }

            var user = await _context.Users.Include(u => u.Wallet)
                .FirstOrDefaultAsync(u => u.UserId == authenticatedUserId, cancellationToken);

            if (user == null)
            {
                await _accessLogService.LogAsync(User, "CompleteProfile", false, $"User not found (id={authenticatedUserId})");
                return NotFound(PhoneOtpError(StatusCodes.Status404NotFound, "User not found."));
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

                return StatusCode(StatusCodes.Status403Forbidden, PhoneOtpError(StatusCodes.Status403Forbidden, "Your account is suspended. Please contact support."));
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

                return StatusCode(StatusCodes.Status403Forbidden, PhoneOtpError(StatusCodes.Status403Forbidden, "Your account is deactivated. Please contact support to reactivate."));
            }

            if (user.IsProfileComplete)
            {
                await _accessLogService.LogAsync(User, "CompleteProfile", false, $"Profile already complete: UserId={user.UserId}");
                return BadRequest(PhoneOtpError(StatusCodes.Status400BadRequest, "Profile is already completed."));
            }

            if (user.UserType == UserType.Admin || request.UserType == UserType.Admin)
            {
                await _accessLogService.LogAsync(User, "CompleteProfile", false, $"Admin role rejected: UserId={user.UserId}");
                return BadRequest(PhoneOtpError(StatusCodes.Status400BadRequest, "Admin role cannot be self-assigned."));
            }

            if (user.UserType == UserType.PropertyManagement || request.UserType == UserType.PropertyManagement)
            {
                await _accessLogService.LogAsync(User, "CompleteProfile", false, $"PropertyManagement role rejected: UserId={user.UserId}");
                return BadRequest(PhoneOtpError(StatusCodes.Status400BadRequest, "PropertyManagement accounts can only be created by an administrator."));
            }

            var normalizedPhone = NormalizeMalaysianPhone(request.PhoneNumber);
            if (normalizedPhone is null)
                return BadRequest(PhoneOtpError(StatusCodes.Status400BadRequest, "Enter a valid Malaysian mobile number, for example 0123456789 or +60123456789."));

            var phoneInUse = await _context.Users.AnyAsync(
                other => other.UserId != user.UserId && other.PhoneNumber == normalizedPhone,
                cancellationToken);
            if (phoneInUse)
                return Conflict(PhoneOtpError(StatusCodes.Status409Conflict, "This phone number is already linked to another account."));

            var now = DateTime.UtcNow;
            var latestOtp = await _context.PhoneOtpVerifications
                .Where(item => item.UserId == user.UserId)
                .OrderByDescending(item => item.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (latestOtp is not null && latestOtp.SmsDeliveryStatus != "Failed" && now - latestOtp.GeneratedAtUtc < PhoneOtpCooldown)
            {
                var seconds = Math.Max(1, (int)Math.Ceiling((PhoneOtpCooldown - (now - latestOtp.GeneratedAtUtc)).TotalSeconds));
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    PhoneOtpError(StatusCodes.Status429TooManyRequests, $"Wait {seconds} seconds before requesting another OTP."));
            }

            var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            var pepper = _configuration["Sms360:OtpHashPepper"];
            if (string.IsNullOrWhiteSpace(pepper)) pepper = _configuration["JwtSettings:SecretKey"];
            if (string.IsNullOrWhiteSpace(pepper))
                return StatusCode(StatusCodes.Status500InternalServerError,
                    PhoneOtpError(StatusCodes.Status500InternalServerError, "OTP hashing is not configured. Set Sms360:OtpHashPepper in production."));

            user.PhoneNumber = normalizedPhone;
            user.UpdatedAt = now;

            var previousPendingOtps = await _context.PhoneOtpVerifications
                .Where(item => item.UserId == user.UserId && item.VerificationStatus == "Pending")
                .ToListAsync(cancellationToken);
            foreach (var previousOtp in previousPendingOtps)
            {
                previousOtp.VerificationStatus = "Superseded";
                previousOtp.UpdatedAtUtc = now;
            }

            if (request.UserType.HasValue)
            {
                user.UserType = request.UserType.Value;
            }

            var otpRecord = new PhoneOtpVerification
            {
                UserId = user.UserId,
                PhoneNumber = normalizedPhone,
                OtpHash = HashPhoneOtp(pepper, user.UserId, normalizedPhone, otp),
                VerificationStatus = "Pending",
                SmsDeliveryStatus = "Sending",
                AttemptCount = 0,
                GeneratedAtUtc = now,
                ExpiresAtUtc = now.Add(PhoneOtpLifetime),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _context.PhoneOtpVerifications.Add(otpRecord);
            await _context.SaveChangesAsync(cancellationToken);

            var sendResult = await _sms360.SendOtpAsync(normalizedPhone, otp, cancellationToken);
            if (!sendResult.Success)
            {
                otpRecord.SmsDeliveryStatus = "Failed";
                otpRecord.VerificationStatus = "Failed";
                otpRecord.SmsErrorCode = sendResult.ErrorCode;
                otpRecord.SmsErrorDescription = sendResult.ErrorDescription;
                otpRecord.UpdatedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                await _accessLogService.LogAsync(User, "RequestPhoneOtp", false,
                    $"UserId={user.UserId}; SmsErrorCode={sendResult.ErrorCode}");
                return StatusCode(StatusCodes.Status502BadGateway, new PhoneOtpFlowResponse
                {
                    Code = StatusCodes.Status502BadGateway,
                    Success = false,
                    Message = sendResult.ErrorDescription ?? "SMS could not be sent.",
                    Data = ToPhoneOtpFlowData(otpRecord)
                });
            }

            otpRecord.SmsDeliveryStatus = "Accepted";
            otpRecord.ProviderMessageId = sendResult.ProviderMessageId;
            otpRecord.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            await _accessLogService.LogAsync(User, "RequestPhoneOtp", true, $"UserId={user.UserId}; SmsDeliveryStatus=Accepted");

            return Ok(new PhoneOtpFlowResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "SMS provider accepted the OTP message. Delivery status may update shortly.",
                Data = ToPhoneOtpFlowData(otpRecord)
            });
        }

        /// <summary>Verifies the six-digit phone OTP and completes the renter/owner profile.</summary>
        [Authorize]
        [HttpPost("complete-profile/verify-phone")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(PhoneOtpFlowResponse), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<AuthResponse>> VerifyPhoneOtp(
            [FromBody] VerifyPhoneOtpRequest request,
            CancellationToken cancellationToken)
        {
            if (!TryGetAuthenticatedUserId(out var userId))
                return Unauthorized(new AuthResponse { Code = StatusCodes.Status401Unauthorized, Success = false, Message = "Authentication required." });

            var user = await _context.Users.Include(item => item.Wallet)
                .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);
            if (user is null)
                return NotFound(new AuthResponse { Code = StatusCodes.Status404NotFound, Success = false, Message = "User not found." });
            if (user.IsProfileComplete)
                return BadRequest(new AuthResponse { Code = StatusCodes.Status400BadRequest, Success = false, Message = "Profile is already completed." });

            var otpRecord = await _context.PhoneOtpVerifications
                .Where(item => item.UserId == userId && item.VerificationStatus == "Pending")
                .OrderByDescending(item => item.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (otpRecord is null || !string.Equals(otpRecord.PhoneNumber, user.PhoneNumber, StringComparison.Ordinal))
                return BadRequest(new AuthResponse { Code = StatusCodes.Status400BadRequest, Success = false, Message = "Request a new phone verification code first." });

            var now = DateTime.UtcNow;
            if (otpRecord.ExpiresAtUtc <= now)
            {
                otpRecord.VerificationStatus = "Expired";
                otpRecord.UpdatedAtUtc = now;
                await _context.SaveChangesAsync(cancellationToken);
                return BadRequest(new AuthResponse { Code = StatusCodes.Status400BadRequest, Success = false, Message = "The OTP has expired. Request a new one." });
            }
            if (otpRecord.AttemptCount >= MaximumPhoneOtpAttempts)
                return BadRequest(new AuthResponse { Code = StatusCodes.Status400BadRequest, Success = false, Message = "Too many incorrect attempts. Request a new OTP." });

            var pepper = _configuration["Sms360:OtpHashPepper"];
            if (string.IsNullOrWhiteSpace(pepper)) pepper = _configuration["JwtSettings:SecretKey"] ?? string.Empty;
            if (!MatchesPhoneOtp(pepper, userId, otpRecord.PhoneNumber, request.Otp, otpRecord.OtpHash))
            {
                otpRecord.AttemptCount++;
                if (otpRecord.AttemptCount >= MaximumPhoneOtpAttempts)
                    otpRecord.VerificationStatus = "Locked";
                otpRecord.UpdatedAtUtc = now;
                await _context.SaveChangesAsync(cancellationToken);
                await _accessLogService.LogAsync(User, "VerifyPhoneOtp", false, $"UserId={userId}; Attempt={otpRecord.AttemptCount}");
                return BadRequest(new AuthResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = otpRecord.VerificationStatus == "Locked" ? "Too many incorrect attempts. Request a new OTP." : "The OTP is incorrect."
                });
            }

            otpRecord.VerificationStatus = "Verified";
            otpRecord.VerifiedAtUtc = now;
            otpRecord.UpdatedAtUtc = now;
            user.IsProfileComplete = true;
            user.UpdatedAt = now;
            if (user.Wallet is null)
            {
                _context.Wallets.Add(new Wallet
                {
                    UserId = user.UserId,
                    Status = WalletStatus.Active,
                    Balance = 0,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            await _context.SaveChangesAsync(cancellationToken);

            var token = _jwtService.GenerateToken(user);
            await _accessLogService.LogAsync(User, "VerifyPhoneOtp", true, $"UserId={userId}; ProfileCompleted=true");
            return Ok(new AuthResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Phone number verified and profile completed.",
                JwtToken = token,
                IsProfileComplete = true,
                User = MapUser(user)
            });
        }

        /// <summary>Returns the latest SMS delivery and phone verification state for the signed-in user.</summary>
        /// <summary>Polls the latest SMS carrier delivery state for the authenticated user's OTP.</summary>
        [Authorize]
        [HttpGet("phone/otp/status")]
        [ProducesResponseType(typeof(PhoneOtpStatusResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<PhoneOtpStatusResponse>> GetPhoneOtpStatus(CancellationToken cancellationToken)
        {
            if (!TryGetAuthenticatedUserId(out var userId))
                return Unauthorized(new PhoneOtpStatusResponse { Code = StatusCodes.Status401Unauthorized, Success = false, Message = "Authentication required." });

            var otpRecord = await _context.PhoneOtpVerifications.AsNoTracking()
                .Where(item => item.UserId == userId)
                .OrderByDescending(item => item.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (otpRecord is null)
                return NotFound(new PhoneOtpStatusResponse { Code = StatusCodes.Status404NotFound, Success = false, Message = "No phone OTP request was found." });

            return Ok(new PhoneOtpStatusResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Latest phone verification status retrieved.",
                Data = ToPhoneOtpFlowData(otpRecord)
            });
        }

        private bool TryGetAuthenticatedUserId(out int userId)
            => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

        private static string? NormalizeMalaysianPhone(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var digits = new string(value.Where(character => character is >= '0' and <= '9').ToArray());
            if (digits.StartsWith("0", StringComparison.Ordinal)) digits = "60" + digits[1..];
            if (digits.Length is < 10 or > 12 || !digits.StartsWith("60", StringComparison.Ordinal)) return null;
            return digits;
        }

        private static string HashPhoneOtp(string pepper, int userId, string phone, string otp)
        {
            var key = Encoding.UTF8.GetBytes(pepper);
            var data = Encoding.UTF8.GetBytes($"{userId}:{phone}:{otp}");
            return Convert.ToHexString(HMACSHA256.HashData(key, data));
        }

        private static bool MatchesPhoneOtp(string pepper, int userId, string phone, string otp, string storedHash)
        {
            try
            {
                var candidate = Convert.FromHexString(HashPhoneOtp(pepper, userId, phone, otp));
                var expected = Convert.FromHexString(storedHash);
                return candidate.Length == expected.Length && CryptographicOperations.FixedTimeEquals(candidate, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static PhoneOtpFlowData ToPhoneOtpFlowData(PhoneOtpVerification otp)
        {
            var digits = otp.PhoneNumber;
            var masked = digits.Length > 4 ? new string('*', digits.Length - 4) + digits[^4..] : digits;
            var retryAfter = Math.Max(0, (int)Math.Ceiling((PhoneOtpCooldown - (DateTime.UtcNow - otp.GeneratedAtUtc)).TotalSeconds));
            return new PhoneOtpFlowData
            {
                PhoneNumber = masked,
                VerificationStatus = otp.VerificationStatus,
                SmsDeliveryStatus = otp.SmsDeliveryStatus,
                SmsErrorCode = otp.SmsErrorCode,
                SmsErrorDescription = otp.SmsErrorDescription,
                ExpiresAtUtc = otp.ExpiresAtUtc,
                ResendAfterSeconds = retryAfter
            };
        }

        private static PhoneOtpFlowResponse PhoneOtpError(int statusCode, string message)
            => new()
            {
                Code = statusCode,
                Success = false,
                Message = message
            };

        [Authorize]
        [HttpPost("logout")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Logout()
        {
            var rawToken = Request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase)?.Trim();
            if (!string.IsNullOrEmpty(rawToken))
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                int? userId = int.TryParse(userIdStr, out var id) ? id : null;
                var expClaim = User.FindFirst("exp")?.Value;
                DateTime? expiresAt = null;
                if (long.TryParse(expClaim, out var expSeconds))
                {
                    expiresAt = DateTimeOffset.FromUnixTimeSeconds(expSeconds).UtcDateTime;
                }
                await _blacklistService.RevokeTokenAsync(rawToken, userId, expiresAt);
                await _accessLogService.LogAsync(User, "Logout", true, "User logged out; token revoked.");
            }
            return Ok(new { Code = 200, Success = true, Message = "Logged out successfully and token revoked." });
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
