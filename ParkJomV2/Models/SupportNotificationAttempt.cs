using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class SupportNotificationAttempt
{
    [Key]
    public int NotificationAttemptId { get; set; }

    public int? IncidentId { get; set; }

    public int? TicketId { get; set; }

    [Required]
    public NotificationChannel Channel { get; set; } = NotificationChannel.Push;

    [Required]
    [StringLength(255)]
    public string Recipient { get; set; } = string.Empty;

    public int? RecipientUserId { get; set; }

    [StringLength(255)]
    public string? Subject { get; set; }

    [Required]
    public string Message { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Status { get; set; } = "Sent"; // Pending, Sent, Failed

    public int AttemptCount { get; set; } = 1;

    public string? ProviderResponse { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? SentAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    [ForeignKey(nameof(IncidentId))]
    public OperationalIncident? Incident { get; set; }

    [ForeignKey(nameof(TicketId))]
    public SupportTicket? Ticket { get; set; }

    [ForeignKey(nameof(RecipientUserId))]
    public User? RecipientUser { get; set; }
}
