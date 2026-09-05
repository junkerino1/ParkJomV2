using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkJomV2.Models;

public class SupportAttachment
{
    [Key]
    public int AttachmentId { get; set; }

    public int? MediaFileId { get; set; }

    public int? TicketId { get; set; }

    public int? TicketMessageId { get; set; }

    public int? ConversationId { get; set; }

    public int? ConversationMessageId { get; set; }

    [Required]
    [StringLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    public string FileUrl { get; set; } = string.Empty;

    [StringLength(100)]
    public string ContentType { get; set; } = "application/octet-stream";

    public long FileSize { get; set; }

    public int UploadedByUserId { get; set; }

    public bool IsPrivate { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    [ForeignKey(nameof(MediaFileId))]
    public MediaFile? MediaFile { get; set; }

    [ForeignKey(nameof(TicketId))]
    public SupportTicket? Ticket { get; set; }

    [ForeignKey(nameof(TicketMessageId))]
    public SupportTicketMessage? TicketMessage { get; set; }

    [ForeignKey(nameof(ConversationId))]
    public SupportConversation? Conversation { get; set; }

    [ForeignKey(nameof(ConversationMessageId))]
    public SupportConversationMessage? ConversationMessage { get; set; }

    [ForeignKey(nameof(UploadedByUserId))]
    public User UploadedByUser { get; set; } = null!;
}
