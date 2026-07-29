// ---- DTO: Google login request from frontend ----
namespace ParkJomV2.Web.Models;

public class GoogleAuthRequest
{
    /// <summary>Google ID Token (credential) from client</summary>
    public string GoogleToken { get; set; } = string.Empty;
}

public class UserDTO
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? ProfilePictureURL { get; set; }
    public int UserType { get; set; }
    public bool IsProfileComplete { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastLoginAt { get; set; }
}

public class AuthResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public UserDTO? User { get; set; }
    public string? JwtToken { get; set; }
    public bool IsProfileComplete { get; set; }
}

public class CompleteProfileRequest
{
    public int UserId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>Persistent user model — stored in App_Data/users.json</summary>
public class StoredUser
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? ProfilePictureURL { get; set; }
    public int UserType { get; set; } // 0=Renter, 1=Owner, 2=Admin
    public string? PhoneNumber { get; set; }
    public bool IsProfileComplete { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastLoginAt { get; set; }
    public string Role { get; set; } = "Commuter";
}
