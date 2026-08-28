using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Constants;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class Booking
{
    [Key]
    public int BookingId { get; set; }

    [Required]
    [StringLength(50)]
    public string BookingReference { get; set; } = string.Empty;

    [Required]
    public int RenterId { get; set; }

    [Required]
    public int ParkingSpotId { get; set; }

    [Required]
    public int VehicleId { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    [Required]
    public BookingStatus BookingStatus { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal TotalAmount { get; set; }

    public int BookedDays { get; set; }

    [Required]
    public BookingRateType RateType { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal RatePerDaySnapshot { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal RentalSubtotal { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal PlatformCommissionRate { get; set; } = PlatformConstants.CommissionRate;

    [Column(TypeName = "decimal(10,2)")]
    public decimal PlatformCommissionAmount { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal OwnerPayoutAmount { get; set; }

    public Guid? BookingQuoteId { get; set; }

    [StringLength(100)]
    public string? IdempotencyKey { get; set; }

    [StringLength(500)]
    public string? CancellationReason { get; set; }

    public DateTime? CancelledAt { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal RefundAmount { get; set; }

    public DateTime? CheckedInAt { get; set; }

    public DateTime? ActualExitAt { get; set; }

    public int OverstayHours { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal OverstayPenaltyAmount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation Properties

    [ForeignKey(nameof(RenterId))]
    public User Renter { get; set; } = null!;

    [ForeignKey(nameof(ParkingSpotId))]
    public ParkingSpot ParkingSpot { get; set; } = null!;

    [ForeignKey(nameof(VehicleId))]
    public Vehicle Vehicle { get; set; } = null!;

    [ForeignKey(nameof(BookingQuoteId))]
    public BookingQuote? BookingQuote { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

    public ICollection<AccessLog> AccessLogs { get; set; } = new List<AccessLog>();

    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}
