using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkJomV2.Models;

public class Review
{
    [Key]
    public int ReviewId { get; set; }

    [Required]
    public int BookingId { get; set; }

    [Required]
    public int ParkingSpotId { get; set; }

    [Required]
    public int ReviewerId { get; set; }

    [Range(1, 5)]
    public int Rating { get; set; }

    [StringLength(1000)]
    public string? Comment { get; set; }

    [StringLength(1000)]
    public string? OwnerReply { get; set; }

    public DateTime? OwnerReplyAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation Properties

    [ForeignKey(nameof(BookingId))]
    public Booking Booking { get; set; } = null!;

    [ForeignKey(nameof(ParkingSpotId))]
    public ParkingSpot ParkingSpot { get; set; } = null!;

    [ForeignKey(nameof(ReviewerId))]
    public User Reviewer { get; set; } = null!;
}