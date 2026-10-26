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

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int? UpdatedByUserId { get; set; }

    [ForeignKey(nameof(UpdatedByUserId))]
    public User? UpdatedByUser { get; set; }
}
