using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class PublicParkingSession
{
    [Key]
    public int PublicParkingSessionId { get; set; }

    [Required]
    [StringLength(50)]
    public string SessionCode { get; set; } = string.Empty;

    [Required]
    public int PublicParkingLotId { get; set; }

    [Required]
    [StringLength(20)]
    public string CarPlateNumber { get; set; } = string.Empty;

    public int? UserId { get; set; }

    [Required]
    public DateTime EntryTimeUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ExitTimeUtc { get; set; }

    public DateTime? PaidAtUtc { get; set; }

    public int DurationMinutes { get; set; } = 0;

    [Column(TypeName = "decimal(10,2)")]
    public decimal HourlyRateSnapshot { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal TotalAmount { get; set; } = 0m;

    [Required]
    public PublicParkingSessionStatus Status { get; set; } = PublicParkingSessionStatus.Parked;

    [StringLength(30)]
    public string? PaymentMethod { get; set; }

    [StringLength(100)]
    public string? StripeSessionId { get; set; }

    [StringLength(100)]
    public string? StripePaymentIntentId { get; set; }

    public int? WalletTransactionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    [JsonIgnore]
    [ForeignKey(nameof(PublicParkingLotId))]
    public PublicParkingLot ParkingLot { get; set; } = null!;

    [JsonIgnore]
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [JsonIgnore]
    [ForeignKey(nameof(WalletTransactionId))]
    public Transaction? WalletTransaction { get; set; }
}
