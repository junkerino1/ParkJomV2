using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkJomV2.Models;

public class SupportWorkflowRun
{
    [Key]
    public int WorkflowRunId { get; set; }

    [Required]
    [StringLength(50)]
    public string RunReference { get; set; } = string.Empty;

    [Required]
    public int CustomerUserId { get; set; }

    [Required]
    [StringLength(100)]
    public string WorkflowKey { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string WorkflowVersion { get; set; } = "1.0";

    [Required]
    public string AnswersJson { get; set; } = "{}";

    public string? ContextSnapshotJson { get; set; }

    [Required]
    [StringLength(50)]
    public string Outcome { get; set; } = "AutoResolved";

    [StringLength(20)]
    public string Priority { get; set; } = "P2";

    [StringLength(100)]
    public string? AssignedTeam { get; set; }

    public string? ChecksResultJson { get; set; }

    public int? TicketId { get; set; }

    public int? IncidentId { get; set; }

    public int? DisputeId { get; set; }

    [StringLength(100)]
    public string? ClientRequestId { get; set; }

    public string? CustomerMessage { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    [ForeignKey(nameof(CustomerUserId))]
    public User CustomerUser { get; set; } = null!;

    [ForeignKey(nameof(TicketId))]
    public SupportTicket? Ticket { get; set; }

    [ForeignKey(nameof(IncidentId))]
    public OperationalIncident? Incident { get; set; }

    [ForeignKey(nameof(DisputeId))]
    public DisputeInvestigation? Dispute { get; set; }
}
