using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class SupportConversation
{
    [Key]
    public int ConversationId { get; set; }

    [Required]
    [StringLength(50)]
    public string ConversationReference { get; set; } = string.Empty;

    [Required]
    public int CustomerUserId { get; set; }

    [StringLength(50)]
    public string Channel { get; set; } = "LiveChat";

    [Required]
    public ConversationStatus Status { get; set; } = ConversationStatus.Active;

    public int? AssignedAdminUserId { get; set; }

    public int? CurrentBookingId { get; set; }

    public int? CurrentParkingSpotId { get; set; }

    public string? ContextSnapshotJson { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastMessageAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public string? ClosingReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    [ForeignKey(nameof(CustomerUserId))]
    public User CustomerUser { get; set; } = null!;

    [ForeignKey(nameof(AssignedAdminUserId))]
    public User? AssignedAdminUser { get; set; }

    [ForeignKey(nameof(CurrentBookingId))]
    public Booking? CurrentBooking { get; set; }

    [ForeignKey(nameof(CurrentParkingSpotId))]
    public ParkingSpot? CurrentParkingSpot { get; set; }

    public ICollection<SupportConversationMessage> Messages { get; set; } = new List<SupportConversationMessage>();

    public ICollection<SupportTicket> ConvertedTickets { get; set; } = new List<SupportTicket>();
}
