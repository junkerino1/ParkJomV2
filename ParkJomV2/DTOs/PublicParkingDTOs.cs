using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.DTOs;

public class CreatePublicParkingLotRequest
{
    [Required]
    [StringLength(120)]
    public string LotName { get; set; } = string.Empty;

    public int? PropertyId { get; set; }

    [Required]
    [StringLength(255)]
    public string Address { get; set; } = string.Empty;

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    [Range(1, 1000)]
    public int TotalCapacity { get; set; }

    public PublicParkingOperatingType OperatingType { get; set; } = PublicParkingOperatingType.Unlimited24Hours;

    public string? OpeningTime { get; set; } = "07:00";

    public string? ClosingTime { get; set; } = "23:00";

    [Range(0.50, 100.00)]
    public decimal HourlyRate { get; set; } = 3.00m;

    public decimal? DailyCapRate { get; set; } = 20.00m;

    public decimal? FlatRatePerEntry { get; set; }

    public decimal? OvernightSurcharge { get; set; } = 30.00m;

    [Range(0, 60)]
    public int InitialGraceMinutes { get; set; } = 15;

    [Range(5, 60)]
    public int ExitGraceMinutes { get; set; } = 15;

    public string? Description { get; set; }

    [StringLength(500)]
    public string? VerificationDocumentUrl { get; set; }

    [StringLength(100)]
    public string? VerificationDocumentType { get; set; }

    [StringLength(500)]
    public string? FacilityPhotoUrl { get; set; }

    [StringLength(500)]
    public string? EntrancePhotoUrl { get; set; }

    [StringLength(100)]
    public string? NearestStationName { get; set; }

    public int? DistanceToStation { get; set; }
}

public class UpdatePublicParkingLotRequest
{
    [StringLength(120)]
    public string? LotName { get; set; }

    [StringLength(255)]
    public string? Address { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    [Range(1, 5000)]
    public int? TotalCapacity { get; set; }

    public PublicParkingOperatingType? OperatingType { get; set; }

    public string? OpeningTime { get; set; }

    public string? ClosingTime { get; set; }

    [Range(0.00, 500.00)]
    public decimal? HourlyRate { get; set; }

    public decimal? DailyCapRate { get; set; }

    public decimal? FlatRatePerEntry { get; set; }

    public decimal? OvernightSurcharge { get; set; }

    [Range(0, 120)]
    public int? InitialGraceMinutes { get; set; }

    [Range(0, 120)]
    public int? ExitGraceMinutes { get; set; }

    public bool? IsActive { get; set; }

    public string? Description { get; set; }

    [StringLength(500)]
    public string? VerificationDocumentUrl { get; set; }

    [StringLength(100)]
    public string? VerificationDocumentType { get; set; }

    [StringLength(500)]
    public string? FacilityPhotoUrl { get; set; }

    [StringLength(500)]
    public string? EntrancePhotoUrl { get; set; }

    [StringLength(100)]
    public string? IotDeviceUuid { get; set; }

    public bool? IsIotConnected { get; set; }

    [StringLength(100)]
    public string? NearestStationName { get; set; }

    public int? DistanceToStation { get; set; }
}

public class ReviewPublicParkingLotRequest
{
    [Required]
    public VerificationStatus Status { get; set; } // 2 = Approved, 3 = Rejected

    public string? ReviewNotes { get; set; }

    public string? IotDeviceUuid { get; set; }
}

public class PublicParkingLotDto
{
    public int PublicParkingLotId { get; set; }
    public int OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public int? PropertyId { get; set; }
    public string LotName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public int TotalCapacity { get; set; }
    public int OccupiedSpaces { get; set; }
    public int AvailableSpaces { get; set; }
    public bool IsFull => AvailableSpaces <= 0;
    public PublicParkingOperatingType OperatingType { get; set; }
    public string? OpeningTime { get; set; }
    public string? ClosingTime { get; set; }
    public bool IsCurrentlyOpen { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal? DailyCapRate { get; set; }
    public decimal? FlatRatePerEntry { get; set; }
    public decimal? OvernightSurcharge { get; set; }
    public int InitialGraceMinutes { get; set; }
    public int ExitGraceMinutes { get; set; }
    public bool IsActive { get; set; }
    public VerificationStatus VerificationStatus { get; set; }
    public string? VerificationDocumentUrl { get; set; }
    public string? VerificationDocumentType { get; set; }
    public string? FacilityPhotoUrl { get; set; }
    public string? EntrancePhotoUrl { get; set; }
    public string? IotDeviceUuid { get; set; }
    public bool IsIotConnected { get; set; }
    public string? NearestStationName { get; set; }
    public int? DistanceToStation { get; set; }
    public string? AdminReviewNotes { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PublicParkingEntryRequest
{
    [Required]
    public int PublicParkingLotId { get; set; }

    [Required]
    [StringLength(20)]
    public string CarPlateNumber { get; set; } = string.Empty;
}

public class PublicParkingExitRequest
{
    [Required]
    public int PublicParkingLotId { get; set; }

    [Required]
    [StringLength(20)]
    public string CarPlateNumber { get; set; } = string.Empty;
}

public class PublicParkingSessionDto
{
    public int PublicParkingSessionId { get; set; }
    public string SessionCode { get; set; } = string.Empty;
    public int PublicParkingLotId { get; set; }
    public string LotName { get; set; } = string.Empty;
    public string LotAddress { get; set; } = string.Empty;
    public string CarPlateNumber { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public DateTime EntryTimeUtc { get; set; }
    public DateTime? ExitTimeUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public int DurationMinutes { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal OvernightSurchargeAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public PublicParkingSessionStatus Status { get; set; }
    public string? PaymentMethod { get; set; }
    public int GraceMinutesRemaining { get; set; }
    public bool CanExitFree { get; set; }
    public bool IsPaid => Status == PublicParkingSessionStatus.Paid || Status == PublicParkingSessionStatus.Exited;
}

public class PublicParkingCheckoutWalletRequest
{
    [Required]
    public string SessionCode { get; set; } = string.Empty;
}

public class PublicParkingCheckoutStripeRequest
{
    [Required]
    public string SessionCode { get; set; } = string.Empty;

    public string ReturnTarget { get; set; } = "web"; // "web" or "native"
}

public class PublicParkingCheckoutStripeResponse
{
    public string CheckoutUrl { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
}

public class PublicParkingFileUploadRequest
{
    [Required]
    public IFormFile File { get; set; } = null!;
}

public class PublicParkingUploadResponse
{
    public string Url { get; set; } = string.Empty;
}
