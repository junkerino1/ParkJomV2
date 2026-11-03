using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class NotificationPreference
{
    [Key]
    public int PreferenceId { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public NotificationCategory Category { get; set; }

    [Required]
    public NotificationChannel Channel { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
