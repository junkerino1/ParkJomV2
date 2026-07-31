using Microsoft.AspNetCore.Components.Forms;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public class CloudinaryUploadResponse
{
    public string PublicId { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string SecureUrl { get; set; } = string.Empty;
    public string? OriginalFilename { get; set; }
}

public class ParkingRegistrationResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? ParkingSpotId { get; set; }
    public int? VerificationRequestId { get; set; }
}

public class ParkingRegistrationRequest
{
    [Required(ErrorMessage = "Property name is required")]
    [StringLength(100)]
    public string PropertyName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Property type is required")]
    public PropertyType PropertyType { get; set; }

    [Required]
    public int osmId { get; set; }

    [Required(ErrorMessage = "Address is required")]
    [StringLength(255)]
    public string Address { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Nearest station is required")]
    public string NearestStationName { get; set; } = string.Empty;

    // ── Parking spot fields ──
    [Required(ErrorMessage = "Bay number is required")]
    [StringLength(50)]
    public string BayNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Level is required")]
    [StringLength(50)]
    public string Level { get; set; } = string.Empty;

    [Required(ErrorMessage = "Document type is required")]
    public VerificationDocumentType DocumentType { get; set; }

    [Required(ErrorMessage = "Document is required")]
    public IFormFile Document { get; set; } = null!;
}

public class VerificationRequestDTO
{
    public int VerificationRequestId { get; set; }
    public int ParkingSpotId { get; set; }
    public string? ParkingLabel { get; set; }
    public int? PropertyId { get; set; }
    public string? PropertyName { get; set; }
    public int SubmittedByUserId { get; set; }
    public string? SubmittedByEmail { get; set; }
    public string? SubmittedByName { get; set; }
    public string? VerificationStatus { get; set; }
    public DateTime SubmittedAt { get; set; }
    public List<VerificationDocumentDTO>? Documents { get; set; }
}

public class VerificationRequestListDTO
{
    public int VerificationRequestId { get; set; }
    public int ParkingSpotId { get; set; }
    public string? ParkingLabel { get; set; }
    public int? PropertyId { get; set; }
    public string? PropertyName { get; set; }
    public int SubmittedByUserId { get; set; }
    public string? SubmittedByEmail { get; set; }
    public string? SubmittedByName { get; set; }
    public string? VerificationStatus { get; set; }
    public DateTime SubmittedAt { get; set; }
}

public class VerificationDocumentDTO
{
    public int VerificationDocumentId { get; set; }
    public VerificationDocumentType DocumentType { get; set; }
    public int MediaFileId { get; set; }
    public string? ResourceType { get; set; }
    public string? Format { get; set; }
    public string? OriginalFileName { get; set; }
    public DateTime UploadedAt { get; set; }
}

public class ErrorResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ApprovalRequest
{
    [Required(ErrorMessage = "IsApproved is required")]
    public bool IsApproved { get; set; }
}

public class DecisionRequest
{
    [Required(ErrorMessage = "Decision is required")]
    public string Decision { get; set; } = string.Empty; // "approved" or "rejected"

    [StringLength(500)]
    public string? ReviewNotes { get; set; }
}

public class ApprovalResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int VerificationRequestId { get; set; }
    public int ParkingSpotId { get; set; }
    public string? VerificationStatus { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class VerificationRequestListResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<VerificationRequestListDTO> Data { get; set; } = new();
}

public class VerificationRequestDetailResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public VerificationRequestDTO? Data { get; set; }
}

public class ConfigParkingRequest
{
    public int parkingSpotId { get; set; }

    public List<IFormFile> ParkingImage { get; set; } = new();

    public DayType DayType { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly EffectiveUntil { get; set; }

    public decimal? DailyRate { get; set; }

    public decimal? MonthlyPrice { get; set; }
}

public class ParkingSpotImageDTO
{
    public int ParkingSpotId { get; set; }
    public int MediaFileId { get; set; }
    public string? ResourceType { get; set; }
    public string? Format { get; set; }
    public string? OriginalFileName { get; set; }
    public DateTime UploadedAt { get; set; }
}

public class ConfigParkingResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ParkingSpotId { get; set; }
}

public class DisplayParkingSpotDTO
{
    public int ParkingSpotId { get; set; }
    public int PropertyId { get; set; }
    public int OwnerId { get; set; }
    public string? ParkingLabel { get; set; }
    public string AvailabilityStatus { get; set; } = string.Empty;
    public string VerificationStatus { get; set; } = string.Empty;
    public decimal? MonthlyRate { get; set; }
    public decimal? DailyRate { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DisplayMyParkingResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<DisplayParkingSpotDTO> Data { get; set; } = new();
}