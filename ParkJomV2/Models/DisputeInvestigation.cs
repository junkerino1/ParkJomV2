using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class DisputeInvestigation
{
    [Key]
    public int DisputeId { get; set; }

    [Required]
    [StringLength(50)]
    public string DisputeReference { get; set; } = string.Empty;

    [Required]
    public DisputeType DisputeType { get; set; } = DisputeType.Refund;

    [Required]
    public DisputeStatus Status { get; set; } = DisputeStatus.Opened;

    [Required]
    public int CustomerUserId { get; set; }

    public int? TicketId { get; set; }

    public int? BookingId { get; set; }

    public int? PaymentId { get; set; }

    public int? TransactionId { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal Amount { get; set; }

    [StringLength(10)]
    public string Currency { get; set; } = "MYR";

    [Required]
    public string Reason { get; set; } = string.Empty;

    [StringLength(100)]
    public string AssignedTeam { get; set; } = "Payments";

    public int? AssignedUserId { get; set; }

    [StringLength(50)]
    public string? Decision { get; set; } // ApproveReversal, Decline, NeedMoreInfo

    public string? DecisionReason { get; set; }

    public int? DecidedByUserId { get; set; }

    public DateTime? DecidedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    [ForeignKey(nameof(CustomerUserId))]
    public User CustomerUser { get; set; } = null!;

    [ForeignKey(nameof(TicketId))]
    public SupportTicket? Ticket { get; set; }

    [ForeignKey(nameof(BookingId))]
    public Booking? Booking { get; set; }

    [ForeignKey(nameof(PaymentId))]
    public Payment? Payment { get; set; }

    [ForeignKey(nameof(TransactionId))]
    public Transaction? Transaction { get; set; }

    [ForeignKey(nameof(AssignedUserId))]
    public User? AssignedUser { get; set; }

    [ForeignKey(nameof(DecidedByUserId))]
    public User? DecidedByUser { get; set; }

    public ICollection<DisputeEvidence> Evidences { get; set; } = new List<DisputeEvidence>();
}
