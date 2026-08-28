using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class ParkingSpot
{
    [Key]
    public int ParkingSpotId { get; set; }

    [Required]
    public int PropertyId { get; set; }

    [Required]
    public int OwnerId { get; set; }

    [StringLength(50)]
    public string? ParkingLabel { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required]
    public AvailabilityStatus AvailabilityStatus { get; set; }

    [Column(TypeName = "decimal(6,2)")]
    public decimal? MonthlyRate { get; set; }

    [Column(TypeName = "decimal(6,2)")]
    public decimal? DailyRate { get; set; }

    public bool IsPublished { get; set; }

    public bool IsConfigurationComplete { get; set; }

    public DateTime? ConfiguredAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation Properties

    [JsonIgnore]
    [ForeignKey(nameof(PropertyId))]
    public Property Property { get; set; } = null!;

    [JsonIgnore]
    [ForeignKey(nameof(OwnerId))]
    public User Owner { get; set; } = null!;

    [JsonIgnore]
    public IoTDevice? IoTDevice { get; set; }

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public ICollection<BookingQuote> BookingQuotes { get; set; } = new List<BookingQuote>();

    public ICollection<ParkingSpotImage> ParkingSpotImages { get; set; } = new List<ParkingSpotImage>();

    public ICollection<ParkingVerificationRequest> VerificationRequests { get; set; } = new List<ParkingVerificationRequest>();

    public ICollection<Review> Reviews { get; set; } = new List<Review>();

    public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();

    public ICollection<Availability> ParkingAvailabilities { get; set; } = new List<Availability>();
}
