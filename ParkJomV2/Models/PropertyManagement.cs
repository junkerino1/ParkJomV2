using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.Models;

/// <summary>Standalone credentials and property assignment for an admin-created management account.</summary>
public class PropertyManagement
{
    [Key]
    public int ManagementAccountId { get; set; }

    [Required, EmailAddress, StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string AccountStatus { get; set; } = "Active";

    [Required]
    public int PropertyId { get; set; }

    [Required]
    [StringLength(512)]
    public string PasswordHash { get; set; } = string.Empty;

    public bool MustChangePassword { get; set; } = true;

    /// <summary>Incremented on password reset/change to invalidate existing JWTs.</summary>
    public int CredentialVersion { get; set; } = 1;

    public DateTime? LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Property Property { get; set; } = null!;
}
