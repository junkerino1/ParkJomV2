using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkJomV2.Models;

public class PushSubscription
{
    [Key]
    public int PushSubscriptionId { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    [StringLength(1024)]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    public string P256dh { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    public string Auth { get; set; } = string.Empty;

    [StringLength(255)]
    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedAt { get; set; }

    // Navigation Properties
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
