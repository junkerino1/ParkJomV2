using ParkJomV2.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public class ParkingDTO
{
    [Required]
    public int PropertyId { get; set; }

    [Required]
    [StringLength(50)]
    public string BayNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Level { get; set; } = string.Empty;

    [Required]
    public VerificationDocumentType DocumentType { get; set; }

    [Required]
    public IFormFile Document { get; set; } = null!;
}

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
    public VerificationStatus VerificationStatus { get; set; }
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
    public VerificationStatus VerificationStatus { get; set; }
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

public class ApprovalResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int VerificationRequestId { get; set; }
    public int ParkingSpotId { get; set; }
    public VerificationStatus VerificationStatus { get; set; }
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