using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class IoTDevice
{
    [Key]
    public int IoTDeviceId { get; set; }

    [Required]
    public int ParkingSpotId { get; set; }

    [Required]
    [StringLength(100)]
    public string Esp32Serial { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string FirmwareVersion { get; set; } = string.Empty;

    [Required]
    public DeviceStatus DeviceStatus { get; set; }

    public bool IsAssigned { get; set; }

    public DateTime? LastHeartbeatAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation Properties

    [ForeignKey(nameof(ParkingSpotId))]
    public ParkingSpot ParkingSpot { get; set; } = null!;

}