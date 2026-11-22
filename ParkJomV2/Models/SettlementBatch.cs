using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkJomV2.Models;

public class SettlementBatch
{
    [Key]
    public int SettlementBatchId { get; set; }

    [Required]
    [StringLength(50)]
    public string BatchReference { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string Rail { get; set; } = "IBG";

    [StringLength(100)]
    public string ScheduleWindow { get; set; } = string.Empty;

    [Required]
    [StringLength(30)]
    public string Status { get; set; } = "Generated";

    public int ItemCount { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal TotalAmount { get; set; }

    [StringLength(10)]
    public string Currency { get; set; } = "MYR";

    [StringLength(30)]
    public string BnmFormatVersion { get; set; } = "BNM-IBG-v2.1";

    [StringLength(64)]
    public string FileChecksum { get; set; } = string.Empty;

    [StringLength(100)]
    public string? BankReference { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? SubmittedAt { get; set; }

    public DateTime? ClearedAt { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [StringLength(500)]
    public string? Notes { get; set; }

    [ForeignKey(nameof(CreatedByUserId))]
    public User? CreatedByUser { get; set; }

    public ICollection<SettlementBatchItem> Items { get; set; } = new List<SettlementBatchItem>();
}
