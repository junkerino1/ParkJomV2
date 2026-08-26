using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

/// <summary>
/// A server-calculated, short-lived price quote. Confirmation must still
/// revalidate availability before creating the linked booking.
/// </summary>
public class BookingQuote
{
    [Key]
    public Guid BookingQuoteId { get; set; } = Guid.NewGuid();

    [Required]
    public int RenterId { get; set; }

    [Required]
    public int ParkingSpotId { get; set; }

    [Required]
    public int VehicleId { get; set; }

    // EndDate is exclusive internally: 1–20 January ends at 21 January 00:00.
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int BookedDays { get; set; }

    [Required]
    public BookingRateType RateType { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal RatePerDay { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal RentalSubtotal { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal PlatformCommissionRate { get; set; } = 10m;

    [Column(TypeName = "decimal(10,2)")]
    public decimal PlatformCommissionAmount { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal OwnerPayoutAmount { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal RenterTotal { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RedeemedAt { get; set; }

    [ForeignKey(nameof(RenterId))]
    public User Renter { get; set; } = null!;

    [ForeignKey(nameof(ParkingSpotId))]
    public ParkingSpot ParkingSpot { get; set; } = null!;

    [ForeignKey(nameof(VehicleId))]
    public Vehicle Vehicle { get; set; } = null!;

    public Booking? Booking { get; set; }
}
