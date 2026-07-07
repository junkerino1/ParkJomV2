using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema; 

namespace ParkJomV2.Models;

public class Vehicle
{
    [Key]
    public int VehicleId { get; set; }

    public int UserId { get; set; }

    [Required]
    [StringLength(20)]
    public string NumberPlate { get; set; } = string.Empty;

    [StringLength(50)]
    public string? VehicleBrand { get; set; }

    [StringLength(50)]
    public string? VehicleModel { get; set; }

    [StringLength(30)]
    public string? VehicleColor { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation

    public User User { get; set; } = null!;

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}