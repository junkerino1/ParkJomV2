using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class PublicParkingLot
{
    [Key]
    public int PublicParkingLotId { get; set; }

    [Required]
    public int OwnerId { get; set; }

    public int? PropertyId { get; set; }

    [Required]
    [StringLength(120)]
    public string LotName { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    public string Address { get; set; } = string.Empty;

    [Column(TypeName = "decimal(9,6)")]
    public decimal Latitude { get; set; }

    [Column(TypeName = "decimal(9,6)")]
    public decimal Longitude { get; set; }

    [Required]
    public int TotalCapacity { get; set; }

    [Required]
    public PublicParkingOperatingType OperatingType { get; set; } = PublicParkingOperatingType.Unlimited24Hours;

    [StringLength(10)]
    public string? OpeningTime { get; set; } = "07:00";

    [StringLength(10)]
    public string? ClosingTime { get; set; } = "23:00";

    [Required]
    [Column(TypeName = "decimal(10,2)")]
    public decimal HourlyRate { get; set; } = 3.00m;

    [Column(TypeName = "decimal(10,2)")]
    public decimal? DailyCapRate { get; set; } = 20.00m;

    [Column(TypeName = "decimal(10,2)")]
    public decimal? FlatRatePerEntry { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? OvernightSurcharge { get; set; } = 30.00m;

    public int InitialGraceMinutes { get; set; } = 15;

    public int ExitGraceMinutes { get; set; } = 15;

    public bool IsActive { get; set; } = true;

    [StringLength(1000)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    [JsonIgnore]
    [ForeignKey(nameof(OwnerId))]
    public User Owner { get; set; } = null!;

    [JsonIgnore]
    [ForeignKey(nameof(PropertyId))]
    public Property? Property { get; set; }

    public ICollection<PublicParkingSession> Sessions { get; set; } = new List<PublicParkingSession>();
}
