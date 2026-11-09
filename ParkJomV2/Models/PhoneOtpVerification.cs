using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.Models;

/// <summary>Stores one user's phone OTP hash and SMS delivery state without retaining the OTP.</summary>
public sealed class PhoneOtpVerification
{
    [Key]
    public int PhoneOtpVerificationId { get; set; }

    public int UserId { get; set; }

    [Required, StringLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required, StringLength(64)]
    public string OtpHash { get; set; } = string.Empty;

    [StringLength(120)]
    public string? ProviderMessageId { get; set; }

    [Required, StringLength(20)]
    public string VerificationStatus { get; set; } = "Pending";

    [Required, StringLength(20)]
    public string SmsDeliveryStatus { get; set; } = "Pending";

    [StringLength(64)]
    public string? SmsErrorCode { get; set; }

    [StringLength(300)]
    public string? SmsErrorDescription { get; set; }

    [StringLength(50)]
    public string? MobileOperator { get; set; }

    public int AttemptCount { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public DateTime? SmsDeliveredAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
