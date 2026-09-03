using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class OperationalIncident
{
    [Key]
    public int IncidentId { get; set; }

    [Required]
    [StringLength(50)]
    public string IncidentReference { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string IncidentType { get; set; } = "GateFailure";

    [Required]
    public IncidentPriority Priority { get; set; } = IncidentPriority.P1;

    [Required]
    public IncidentStatus Status { get; set; } = IncidentStatus.Open;

    [Required]
    [StringLength(255)]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int? PropertyId { get; set; }

    public int? ParkingSpotId { get; set; }

    public int? IoTDeviceId { get; set; }

    [Required]
    [StringLength(50)]
    public string Source { get; set; } = "QuickHelp"; // QuickHelp, IoTMonitoring, TicketCorrelation, Admin

    [StringLength(100)]
    public string AssignedTeam { get; set; } = "ParkingOperations";

    public int? AssignedUserId { get; set; }

    public int AffectedCustomerCount { get; set; } = 1;

    public DateTime? AcknowledgedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public int EscalationLevel { get; set; } = 0; // 0=Primary, 1=Backup, 2=Supervisor, 3=Manager

    public DateTime? NextEscalationAt { get; set; }

    [StringLength(100)]
    public string? CorrelationKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    [ForeignKey(nameof(PropertyId))]
    public Property? Property { get; set; }

    [ForeignKey(nameof(ParkingSpotId))]
    public ParkingSpot? ParkingSpot { get; set; }

    [ForeignKey(nameof(IoTDeviceId))]
    public IoTDevice? IoTDevice { get; set; }

    [ForeignKey(nameof(AssignedUserId))]
    public User? AssignedUser { get; set; }

    public ICollection<IncidentTicket> IncidentTickets { get; set; } = new List<IncidentTicket>();

    public ICollection<SupportNotificationAttempt> NotificationAttempts { get; set; } = new List<SupportNotificationAttempt>();
}
