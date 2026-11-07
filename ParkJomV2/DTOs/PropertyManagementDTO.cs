using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public sealed class PropertyManagementAccountCreateRequest
{
    [Required, Range(1, int.MaxValue)]
    public int PropertyId { get; set; }

    [Required, EmailAddress, StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Username { get; set; } = string.Empty;

}

public sealed class PropertyManagementAccountUpdateRequest
{
    [EmailAddress, StringLength(255)]
    public string? Email { get; set; }

    [StringLength(100)]
    public string? Username { get; set; }

    [Range(1, int.MaxValue)]
    public int? PropertyId { get; set; }

    [RegularExpression("^(Active|Deactivated|Suspended)$", ErrorMessage = "Status must be Active, Deactivated, or Suspended.")]
    public string? Status { get; set; }

    /// <summary>Set true to send a fresh first-time password invitation and invalidate prior links.</summary>
    public bool ResendInvitation { get; set; }
}

public sealed class PropertyManagementLoginRequest
{
    [Required, EmailAddress, StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; set; } = string.Empty;
}

public sealed class PropertyManagementInvitationActivationRequest
{
    [Required, StringLength(4096)]
    public string InvitationToken { get; set; } = string.Empty;

    [Required, MinLength(12), MaxLength(128)]
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class PropertyManagementLoginResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string JwtToken { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; }
    public PropertyManagementProfileData Data { get; set; } = new();
}

public class PropertyManagementAccountDTO
{
    public int ManagementAccountId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public string AccountStatus { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>Account mutation response including email invitation delivery status.</summary>
public sealed class PropertyManagementAccountMutationDTO : PropertyManagementAccountDTO
{
    public string? InvitationEmailStatus { get; set; }
}

public sealed class PropertyManagementAccountListResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<PropertyManagementAccountDTO> Data { get; set; } = new();
}

public sealed class PropertyManagementProfileData
{
    public int ManagementAccountId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; }
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public string PropertyAddress { get; set; } = string.Empty;
}

public sealed class PropertyManagementProfileResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public PropertyManagementProfileData Data { get; set; } = new();
}

public sealed class PropertyManagementBookingDTO
{
    public int BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public int ParkingSpotId { get; set; }
    public string? ParkingLabel { get; set; }
    public string VehicleNumberPlate { get; set; } = string.Empty;
    public string BookingStatus { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public DateTime? ActualExitAt { get; set; }
}

public sealed class PropertyManagementBookingListResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<PropertyManagementBookingDTO> Data { get; set; } = new();
}

public sealed class PropertyManagementAccessEventDTO
{
    public int IoTStatusLogId { get; set; }
    public int BookingId { get; set; }
    public int ParkingSpotId { get; set; }
    public string? ParkingLabel { get; set; }
    public string VehicleNumberPlate { get; set; } = string.Empty;
    public DateTime EnteredAtUtc { get; set; }
    public bool FirstEntryForBooking { get; set; }
}

public sealed class PropertyManagementAccessEventListResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<PropertyManagementAccessEventDTO> Data { get; set; } = new();
}

public sealed class PropertyManagementPlateCheckRequest
{
    [Required]
    public string ImageBase64 { get; set; } = string.Empty;

    [Required]
    public string MimeType { get; set; } = "image/jpeg";

    public int? ParkingSpotId { get; set; }
}

public sealed class PropertyManagementPlateCheckData
{
    public bool Allowed { get; set; }
    public string DecisionCode { get; set; } = string.Empty;
    public int PropertyId { get; set; }
    public int? BookingId { get; set; }
    public int? ParkingSpotId { get; set; }
    public string DetectedPlate { get; set; } = string.Empty;
    public string FormattedPlate { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public double MinimumConfidence { get; set; }
}

public sealed class PropertyManagementPlateCheckResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public PropertyManagementPlateCheckData Data { get; set; } = new();
}

public sealed class PropertyManagementPlateCheckLogDTO
{
    public int AccessLogId { get; set; }
    public DateTime CheckedAtUtc { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public int? BookingId { get; set; }
}

public sealed class PropertyManagementPlateCheckLogListResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<PropertyManagementPlateCheckLogDTO> Data { get; set; } = new();
}
