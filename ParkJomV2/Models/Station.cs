using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;
namespace ParkJomV2.Models;

public class Station
{

    [Key]
    public int StationId { get; set; }
    [Required]
    [StringLength(100)]
    public string StationName { get; set; } = string.Empty;

    [Column(TypeName = "decimal(9,6)")]
    public decimal Latitude { get; set; }
    [Column(TypeName = "decimal(9,6)")]
    public decimal Longitude { get; set; }
    [StringLength(255)]
    // Navigation Properties
    public ICollection<Property> Properties { get; set; } = new List<Property>();

}

