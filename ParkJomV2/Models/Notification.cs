using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class Notification
{
    [Key]
    public long NotificationId { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    [StringLength(100)]
    public string Type { get; set; } = string.Empty; // e.g. "booking.confirmed"

    [Required]
    public NotificationCategory Category { get; set; }

    [Required]
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Body { get; set; } = string.Empty;

    [StringLength(500)]
    public string? ActionUrl { get; set; }

    [StringLength(50)]
    public string? EntityType { get; set; } // e.g. "Booking"

    [StringLength(100)]
    public string? EntityId { get; set; }

    public string? DataJson { get; set; }

    [StringLength(150)]
    public string? DedupeKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ReadAt { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    // Navigation Properties
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public ICollection<NotificationDelivery> Deliveries { get; set; } = new List<NotificationDelivery>();
}
