using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class AccessLog
{
    [Key]
    public int AccessLogId { get; set; }

    [Required]
    public int BookingId { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public int IoTDeviceId { get; set; }


    [StringLength(1000)]
    public string Actions { get; set; }

    public DateTime AccessedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation Properties

    [ForeignKey(nameof(BookingId))]
    public Booking Booking { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [ForeignKey(nameof(IoTDeviceId))]
    public IoTDevice IoTDevice { get; set; } = null!;
}