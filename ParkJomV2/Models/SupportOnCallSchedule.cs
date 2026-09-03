using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkJomV2.Models;

public class SupportOnCallSchedule
{
    [Key]
    public int ScheduleId { get; set; }

    [Required]
    [StringLength(100)]
    public string ShiftName { get; set; } = "24/7 Operations Shift";

    public DateTime ShiftStart { get; set; } = DateTime.UtcNow;

    public DateTime ShiftEnd { get; set; } = DateTime.UtcNow.AddDays(7);

    public int? PrimaryResponderId { get; set; }

    public int? BackupResponderId { get; set; }

    public int? SupervisorId { get; set; }

    public int? OperationsManagerId { get; set; }

    [StringLength(255)]
    public string ActiveChannels { get; set; } = "Push,SMS,Phone,Email";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    [ForeignKey(nameof(PrimaryResponderId))]
    public User? PrimaryResponder { get; set; }

    [ForeignKey(nameof(BackupResponderId))]
    public User? BackupResponder { get; set; }

    [ForeignKey(nameof(SupervisorId))]
    public User? Supervisor { get; set; }

    [ForeignKey(nameof(OperationsManagerId))]
    public User? OperationsManager { get; set; }
}

public class SupportOnCallPolicy
{
    [Key]
    public int PolicyId { get; set; }

    public int P0BackupDelayMinutes { get; set; } = 2;

    public int P0SupervisorDelayMinutes { get; set; } = 5;

    public int P0ManagerDelayMinutes { get; set; } = 15;

    public int P1BackupDelayMinutes { get; set; } = 5;

    public int P1SupervisorDelayMinutes { get; set; } = 15;

    public int P1ManagerDelayMinutes { get; set; } = 30;

    [StringLength(255)]
    public string NotificationChannels { get; set; } = "Push,SMS,Phone,Email";

    public bool AutoEscalateEnabled { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
