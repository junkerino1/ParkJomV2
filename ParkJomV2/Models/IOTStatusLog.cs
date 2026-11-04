using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class IoTStatusLog
{
    [Key]
    public int IoTStatusLogId { get; set; }

    [Required]
    public int IoTDeviceId { get; set; }

    [Required]
    public DeviceStatus DeviceStatus { get; set; }

    // Store topic metadata separately so Message remains the untouched MQTT payload.
    [StringLength(255)]
    public string? Topic { get; set; }

    // Keep the MQTT payload intact; the database column is nvarchar(max).
    public string? Message { get; set; }

    public DateTime LoggedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation Properties

    [ForeignKey(nameof(IoTDeviceId))]
    public IoTDevice IoTDevice { get; set; } = null!;
}
