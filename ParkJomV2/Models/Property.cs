using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using ParkJomV2.Models.Enums;
namespace ParkJomV2.Models;

public class Property
{
    [Key]
    public int PropertyId { get; set; }

    [Required]
    [StringLength(100)]
    public string PropertyName { get; set; } = string.Empty;

    [Required]
    public PropertyType PropertyType { get; set; }

    [Required]
    [StringLength(255)]
    public string Address { get; set; } = string.Empty;

    [Column(TypeName = "decimal(9,6)")]
    public decimal Latitude { get; set; }

    [Column(TypeName = "decimal(9,6)")]
    public decimal Longitude { get; set; }

    [Required]
    public int NearestStationId { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal DistanceToStation { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal TimeToStation { get; set; }

    public long? OsmId { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation Properties

    [ForeignKey(nameof(NearestStationId))]
    public Station Station { get; set; } = null!;

    public ICollection<ParkingSpot> ParkingSpots { get; set; } = new List<ParkingSpot>();
}