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

    [StringLength(1000)]
    public string? Message { get; set; }

    public DateTime LoggedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation Properties

    [ForeignKey(nameof(IoTDeviceId))]
    public IoTDevice IoTDevice { get; set; } = null!;
}