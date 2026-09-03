using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class SupportTicket
{
    [Key]
    public int TicketId { get; set; }

    [Required]
    [StringLength(50)]
    public string TicketReference { get; set; } = string.Empty;

    [Required]
    public SupportTicketType TicketType { get; set; } = SupportTicketType.Preset;

    [Required]
    public SupportSource Source { get; set; } = SupportSource.QuickHelp;

    [Required]
    public SupportCategory Category { get; set; } = SupportCategory.General;

    [Required]
    public SupportTicketPriority Priority { get; set; } = SupportTicketPriority.P2;

    [Required]
    public int CustomerUserId { get; set; }

    [Required]
    public int CreatedByUserId { get; set; }

    public int? AssignedAdminUserId { get; set; }

    [StringLength(100)]
    public string? AssignedTeam { get; set; } = "CustomerSupport";

    public int? ConversationId { get; set; }

    public int? WorkflowRunId { get; set; }

    public int? BookingId { get; set; }

    public int? ParkingSpotId { get; set; }

    public int? VehicleId { get; set; }

    public int? OperationalIncidentId { get; set; }

    public int? DisputeInvestigationId { get; set; }

    [Required]
    [StringLength(255)]
    public string Subject { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [Required]
    public SupportTicketStatus Status { get; set; } = SupportTicketStatus.New;

    public DateTime? AcceptedAt { get; set; }

    public DateTime? FirstResponseAt { get; set; }

    public DateTime? FirstResponseDueAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime? ResolutionDueAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    [StringLength(100)]
    public string? ResolutionCode { get; set; }

    [StringLength(500)]
    public string? InternalSummary { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    [ForeignKey(nameof(CustomerUserId))]
    public User CustomerUser { get; set; } = null!;

    [ForeignKey(nameof(CreatedByUserId))]
    public User CreatedByUser { get; set; } = null!;

    [ForeignKey(nameof(AssignedAdminUserId))]
    public User? AssignedAdminUser { get; set; }

    [ForeignKey(nameof(ConversationId))]
    public SupportConversation? Conversation { get; set; }

    public SupportWorkflowRun? WorkflowRun { get; set; }

    [ForeignKey(nameof(BookingId))]
    public Booking? Booking { get; set; }

    [ForeignKey(nameof(ParkingSpotId))]
    public ParkingSpot? ParkingSpot { get; set; }

    [ForeignKey(nameof(VehicleId))]
    public Vehicle? Vehicle { get; set; }

    [ForeignKey(nameof(OperationalIncidentId))]
    public OperationalIncident? OperationalIncident { get; set; }

    public DisputeInvestigation? DisputeInvestigation { get; set; }

    public ICollection<SupportTicketMessage> Messages { get; set; } = new List<SupportTicketMessage>();

    public ICollection<SupportAttachment> Attachments { get; set; } = new List<SupportAttachment>();

    public ICollection<IncidentTicket> IncidentTickets { get; set; } = new List<IncidentTicket>();
}
