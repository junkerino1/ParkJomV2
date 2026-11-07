using Microsoft.IdentityModel.Tokens;
using ParkJomV2.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ParkJomV2.Services;

public class JwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateToken(User user, int? credentialVersion = null)
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]!));

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
        new Claim(JwtRegisteredClaimNames.Email, user.Email),
        new Claim("UserType", user.UserType.ToString()),
        new Claim("FullName",
            $"{user.FirstName} {user.LastName}".Trim())
    };

        if (!string.IsNullOrWhiteSpace(user.GoogleId))
            claims.Add(new Claim("GoogleId", user.GoogleId));

        if (user.UserType == ParkJomV2.Models.Enums.UserType.PropertyManagement && credentialVersion.HasValue)
            claims.Add(new Claim("CredentialVersion", credentialVersion.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"],
            audience: jwtSettings["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(
                int.Parse(jwtSettings["ExpirationHours"]!)),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Creates a JWT for the independent PropertyManagement credential store.</summary>
    public string GenerateManagementToken(PropertyManagement account)
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, account.ManagementAccountId.ToString()),
            new(JwtRegisteredClaimNames.Email, account.Email),
            new("UserType", ParkJomV2.Models.Enums.UserType.PropertyManagement.ToString()),
            new("FullName", account.Username),
            new("CredentialVersion", account.CredentialVersion.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"],
            audience: jwtSettings["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(int.Parse(jwtSettings["ExpirationHours"]!)),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Creates a 14-day, single-use invitation token for setting a management account's first password.</summary>
    public string GenerateManagementInvitationToken(PropertyManagement account, out DateTime expiresAtUtc)
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        expiresAtUtc = now.AddDays(14);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, account.ManagementAccountId.ToString()),
            new(JwtRegisteredClaimNames.Email, account.Email),
            new("UserType", ParkJomV2.Models.Enums.UserType.PropertyManagement.ToString()),
            new("purpose", "management-invitation"),
            new("CredentialVersion", account.CredentialVersion.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"],
            audience: jwtSettings["Audience"],
            claims: claims,
            notBefore: now,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Validates signature, issuer, audience, and expiry of a management invitation JWT.</summary>
    public ClaimsPrincipal ValidateManagementInvitationToken(string token)
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        var validation = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSettings["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]!)),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        var principal = new JwtSecurityTokenHandler().ValidateToken(token, validation, out _);
        if (!string.Equals(principal.FindFirst("purpose")?.Value, "management-invitation", StringComparison.Ordinal) ||
            !string.Equals(principal.FindFirst("UserType")?.Value,
                ParkJomV2.Models.Enums.UserType.PropertyManagement.ToString(), StringComparison.Ordinal))
            throw new SecurityTokenException("This is not a PropertyManagement invitation token.");

        return principal;
    }

}
