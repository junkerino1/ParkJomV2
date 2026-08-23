using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class AccessLog
{
    [Key]
    public int AccessLogId { get; set; }

    public int? BookingId { get; set; }

    public int? UserId { get; set; }

    public int? IoTDeviceId { get; set; }


    [StringLength(1000)]
    public string Actions { get; set; } = string.Empty;

    public DateTime AccessedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation Properties

    [ForeignKey(nameof(BookingId))]
    public Booking? Booking { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey(nameof(IoTDeviceId))]
    public IoTDevice? IoTDevice { get; set; }
}