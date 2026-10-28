using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.DTOs;

public class UserProfileDTO
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? ProfilePictureURL { get; set; }
    public UserType UserType { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public bool IsProfileComplete { get; set; }
    public string AccountStatus { get; set; } = "Active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public UserProfileSummaryDTO? Summary { get; set; }
}

public class UserProfileSummaryDTO
{
    public decimal WalletBalance { get; set; }
    public decimal WalletOnHold { get; set; }
    public string WalletCurrency { get; set; } = "MYR";
    public string WalletStatus { get; set; } = "Active";
    public int VehicleCount { get; set; }
    public int ActiveBookingsCount { get; set; }
    public int TotalBookingsCount { get; set; }
    public int FavoritesCount { get; set; }
    public int OwnedParkingSpotsCount { get; set; }
    public int OpenSupportTicketsCount { get; set; }
}

public class UpdateUserProfileRequest
{
    [StringLength(100, ErrorMessage = "First name cannot exceed 100 characters.")]
    public string? FirstName { get; set; }

    [StringLength(100, ErrorMessage = "Last name cannot exceed 100 characters.")]
    public string? LastName { get; set; }

    [Phone(ErrorMessage = "Invalid phone number format.")]
    [StringLength(20, ErrorMessage = "Phone number cannot exceed 20 characters.")]
    public string? PhoneNumber { get; set; }
}

public class UploadProfilePictureRequest
{
    [Required(ErrorMessage = "Profile picture image file is required.")]
    public IFormFile Picture { get; set; } = null!;
}


public class DeactivateAccountRequest
{
    [StringLength(500, ErrorMessage = "Reason cannot exceed 500 characters.")]
    public string? Reason { get; set; }

    public bool ConfirmDeactivation { get; set; } = true;
}

public class AdminUpdateUserProfileRequest
{
    [StringLength(100, ErrorMessage = "First name cannot exceed 100 characters.")]
    public string? FirstName { get; set; }

    [StringLength(100, ErrorMessage = "Last name cannot exceed 100 characters.")]
    public string? LastName { get; set; }

    [Phone(ErrorMessage = "Invalid phone number format.")]
    [StringLength(20, ErrorMessage = "Phone number cannot exceed 20 characters.")]
    public string? PhoneNumber { get; set; }

    public UserType? UserType { get; set; }

    public bool? IsProfileComplete { get; set; }

    [StringLength(20, ErrorMessage = "Account status cannot exceed 20 characters.")]
    public string? AccountStatus { get; set; }
}

public class AdminUserParkingSpotSummaryDTO
{
    public int ParkingSpotId { get; set; }
    public string ParkingLabel { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public string AvailabilityStatus { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public decimal? DailyRate { get; set; }
    public decimal? MonthlyRate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminUserDetailDTO : UserProfileDTO
{
    public string GoogleId { get; set; } = string.Empty;
    public List<VehicleResponseDTO> Vehicles { get; set; } = new();
    public List<AdminUserParkingSpotSummaryDTO> OwnedParkingSpots { get; set; } = new();
}

public class UserProfileResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public UserProfileDTO? Data { get; set; }
    public string? JwtToken { get; set; }
}

public class AdminUserDetailResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public AdminUserDetailDTO? Data { get; set; }
}

public class AdminUserListResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public List<UserProfileDTO> Data { get; set; } = new();
}
