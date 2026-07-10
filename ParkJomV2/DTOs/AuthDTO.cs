using ParkJomV2.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public class GoogleAuthRequest
{
    [Required(ErrorMessage = "Google token is required")]
    public string GoogleToken { get; set; } = string.Empty;
}

public class CompleteProfileRequest
{
    [Required]
    public int UserId { get; set; }

    [Required]
    [Phone(ErrorMessage = "Invalid phone number")]
    public string PhoneNumber { get; set; } = string.Empty;
}

public class UserDTO
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? ProfilePictureURL { get; set; }
    public UserType UserType { get; set; }
    public bool IsProfileComplete { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastLoginAt { get; set; }
}

public class AuthResponseDTO
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public UserDTO? User { get; set; }
    public string? JwtToken { get; set; }
    public bool IsProfileComplete { get; set; }
}

public class GoogleTokenValidationResponse
{
    // google ID    
    public string Sub { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Picture { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
}