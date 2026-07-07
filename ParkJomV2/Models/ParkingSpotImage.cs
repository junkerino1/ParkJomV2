using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkJomV2.Models;

public class ParkingSpotImage
{
    [Key]
    public int ParkingSpotImageId { get; set; }

    [Required]
    public int ParkingSpotId { get; set; }

    [Required]
    public int MediaFileId { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsPrimary { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation Properties

    [ForeignKey(nameof(ParkingSpotId))]
    public ParkingSpot ParkingSpot { get; set; } = null!;

    [ForeignKey(nameof(MediaFileId))]
    public MediaFile MediaFile { get; set; } = null!;
}