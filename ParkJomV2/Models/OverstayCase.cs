using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkJomV2.Models;

public class OverstayCase
{
    [Key]
    public int OverstayCaseId { get; set; }

    [Required]
    [StringLength(50)]
    public string CaseReference { get; set; } = string.Empty;

    [Required]
    public int BookingId { get; set; }

    [Required]
    public int RenterId { get; set; }

    [Required]
    public int ParkingSpotId { get; set; }

    public int? IoTDeviceId { get; set; }

    public DateOnly WindowDate { get; set; }

    public DateTime ScheduledEndAtUtc { get; set; }

    public DateTime GraceEndsAtUtc { get; set; }

    public DateTime DetectedAtUtc { get; set; }

    public DateTime? DepartedAtUtc { get; set; }

    public int OverstayMinutes { get; set; }

    public int BillableHours { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal BaseHourlyRate { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal PenaltyMultiplier { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal HourlyPenaltyTariff { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal CalculatedPenaltyAmount { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal DebitedAmount { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal WaivedAmount { get; set; }

    [Required]
    [StringLength(30)]
    public string Status { get; set; } = "Detected";

    [Required]
    [StringLength(30)]
    public string PaymentStatus { get; set; } = "Unbilled";

    public DateTime? PushWarningSentAtUtc { get; set; }

    public DateTime? SmsWarningSentAtUtc { get; set; }

    [StringLength(100)]
    public string? SmsProviderRef { get; set; }

    [StringLength(100)]
    public string? SmsDeliveryStatus { get; set; }

    public DateTime? LedgerDebitedAtUtc { get; set; }

    public int? PenaltyTransactionId { get; set; }

    public bool HardwareLockActive { get; set; }

    public DateTime? HardwareLockEngagedAtUtc { get; set; }

    public DateTime? HardwareLockReleasedAtUtc { get; set; }

    public int? HardwareLockReleasedByUserId { get; set; }

    public int? DisputeInvestigationId { get; set; }

    [StringLength(1000)]
    public string? ResolutionNotes { get; set; }

    public int? ResolvedByUserId { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(BookingId))]
    public Booking Booking { get; set; } = null!;

    [ForeignKey(nameof(RenterId))]
    public User Renter { get; set; } = null!;

    [ForeignKey(nameof(ParkingSpotId))]
    public ParkingSpot ParkingSpot { get; set; } = null!;

    [ForeignKey(nameof(IoTDeviceId))]
    public IoTDevice? IoTDevice { get; set; }

    [ForeignKey(nameof(PenaltyTransactionId))]
    public Transaction? PenaltyTransaction { get; set; }

    [ForeignKey(nameof(HardwareLockReleasedByUserId))]
    public User? HardwareLockReleasedByUser { get; set; }

    [ForeignKey(nameof(DisputeInvestigationId))]
    public DisputeInvestigation? DisputeInvestigation { get; set; }

    [ForeignKey(nameof(ResolvedByUserId))]
    public User? ResolvedByUser { get; set; }
}
