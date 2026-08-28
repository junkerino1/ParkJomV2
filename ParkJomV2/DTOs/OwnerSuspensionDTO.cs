using System.ComponentModel.DataAnnotations;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.DTOs;

public class OwnerSuspensionRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class SuspendedOwnerDTO
{
    public int UserId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string AccountStatus { get; set; } = string.Empty;

    public UserType UserType { get; set; }

    public int SuspendedParkingSpotCount { get; set; }

    public DateTime UpdatedAt { get; set; }
}
