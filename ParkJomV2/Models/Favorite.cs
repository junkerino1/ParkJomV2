using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class Favorite
{
    [Key]
    public int FavoriteId { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public int ParkingSpotId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation Properties

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [ForeignKey(nameof(ParkingSpotId))]
    public ParkingSpot ParkingSpot { get; set; } = null!;
}