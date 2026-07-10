using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

// ---- JWT Generation Helper ----
namespace ParkJomV2.Web.Helpers;

public static class JwtHelper
{
    // ⚠️ Production: read from User Secrets / Azure Key Vault
    private const string SecretKey = "ParkJom_SuperSecret_Key_2026_AtLeast_32_Characters!!";
    private const string Issuer = "ParkJom";
    private const string Audience = "ParkJom";
    private const int ExpireDays = 7;

    /// <summary>
    /// Generates a custom JWT Token containing userId, email, name, picture, role
    /// </summary>
    public static string GenerateToken(string userId, string email, string name, string picture, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim("name", name),
            new Claim("picture", picture),
            new Claim("role", role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(ExpireDays),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
