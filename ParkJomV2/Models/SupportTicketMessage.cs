using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkJomV2.Models;

public class SupportTicketMessage
{
    [Key]
    public int MessageId { get; set; }

    [Required]
    public int TicketId { get; set; }

    public int? SenderUserId { get; set; }

    [Required]
    [StringLength(50)]
    public string SenderRole { get; set; } = "Customer"; // Customer, Admin, System

    [Required]
    public string Body { get; set; } = string.Empty;

    public bool IsInternal { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    [ForeignKey(nameof(TicketId))]
    public SupportTicket Ticket { get; set; } = null!;

    [ForeignKey(nameof(SenderUserId))]
    public User? SenderUser { get; set; }

    public ICollection<SupportAttachment> Attachments { get; set; } = new List<SupportAttachment>();
}
