using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class NotificationDelivery
{
    [Key]
    public long DeliveryId { get; set; }

    [Required]
    public long NotificationId { get; set; }

    [Required]
    public NotificationChannel Channel { get; set; } // InApp, WebPush, Push, Email, SMS, etc.

    [Required]
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;

    public int AttemptCount { get; set; } = 0;

    public DateTime? NextAttemptAt { get; set; }

    [StringLength(255)]
    public string? ProviderMessageId { get; set; }

    public string? LastError { get; set; }

    public DateTime? SentAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    [ForeignKey(nameof(NotificationId))]
    public Notification? Notification { get; set; }
}
