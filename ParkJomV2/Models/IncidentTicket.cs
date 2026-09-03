using System.ComponentModel.DataAnnotations.Schema;

namespace ParkJomV2.Models;

public class IncidentTicket
{
    public int IncidentId { get; set; }

    public int TicketId { get; set; }

    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    [ForeignKey(nameof(IncidentId))]
    public OperationalIncident Incident { get; set; } = null!;

    [ForeignKey(nameof(TicketId))]
    public SupportTicket Ticket { get; set; } = null!;
}
