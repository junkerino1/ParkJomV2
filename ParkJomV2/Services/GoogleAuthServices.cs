using Google.Apis.Auth;
using ParkJomV2.DTOs;

namespace ParkJomV2.Services;

public class GoogleAuthService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleAuthService> _logger;

    public GoogleAuthService(
        IConfiguration configuration,
        ILogger<GoogleAuthService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<GoogleTokenValidationResponse?> ValidateGoogleTokenAsync(string idToken)
    {
        try
        {
            var clientId = _configuration["GoogleOAuth:ClientId"];

            var payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { clientId }
                });

            return new GoogleTokenValidationResponse
            {
                Sub = payload.Subject,
                Email = payload.Email,
                Name = payload.Name,
                Picture = payload.Picture,
                EmailVerified = payload.EmailVerified
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid Google ID Token");
            return null;
        }
    }
}