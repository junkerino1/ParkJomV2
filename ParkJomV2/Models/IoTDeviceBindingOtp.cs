using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkJomV2.Models;

/// <summary>Stores only the current keyed hash and validity window for a device's binding OTP.</summary>
public sealed class IoTDeviceBindingOtp
{
    [Key]
    [ForeignKey(nameof(IoTDevice))]
    public int IoTDeviceId { get; set; }

    [Required]
    [StringLength(64)]
    public string OtpHash { get; set; } = string.Empty;

    public DateTime GeneratedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public IoTDevice IoTDevice { get; set; } = null!;
}
