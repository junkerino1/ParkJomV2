using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkJomV2.Models;

public class SupportAuditEvent
{
    [Key]
    public int AuditEventId { get; set; }

    [Required]
    [StringLength(50)]
    public string ObjectType { get; set; } = string.Empty; // Conversation, Ticket, Incident, Dispute, WorkflowRun, OnCall

    public int ObjectId { get; set; }

    [Required]
    [StringLength(100)]
    public string ObjectReference { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Action { get; set; } = string.Empty; // Created, StatusChanged, Assigned, MessageAdded, OverrideExecuted, DecisionMade, etc.

    public int? ActorUserId { get; set; }

    [Required]
    [StringLength(50)]
    public string ActorRole { get; set; } = "System"; // Customer, Admin, System

    [StringLength(100)]
    public string? PreviousState { get; set; }

    [StringLength(100)]
    public string? NewState { get; set; }

    public string? Detail { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    [ForeignKey(nameof(ActorUserId))]
    public User? ActorUser { get; set; }
}
