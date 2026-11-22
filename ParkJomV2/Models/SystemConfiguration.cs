using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkJomV2.Models;

public class SystemConfiguration
{
    [Key]
    public int Id { get; set; } = 1;

    [Column(TypeName = "decimal(5,2)")]
    public decimal CommissionRate { get; set; } = 10.00m;

    public int GracePeriodMinutes { get; set; } = 15;

    [Column(TypeName = "decimal(5,2)")]
    public decimal OverstayPenaltyMultiplier { get; set; } = 1.50m;

    [Column(TypeName = "decimal(10,2)")]
    public decimal OverstayMaxDailyPenalty { get; set; } = 30.00m;

    public bool HardwareExitLockEnabled { get; set; } = true;

    [Column(TypeName = "decimal(5,2)")]
    public decimal TaxRatePercent { get; set; } = 8.00m;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int? UpdatedByUserId { get; set; }

    [ForeignKey(nameof(UpdatedByUserId))]
    public User? UpdatedByUser { get; set; }
}
