// ---- DTO: Google login request from frontend ----
namespace ParkJomV2.Web.Models;

public class GoogleLoginRequestDto
{
    /// <summary>Google ID Token (credential) from client</summary>
    public string IdToken { get; set; } = string.Empty;
}

public class GoogleLoginResponseDto
{
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Picture { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
