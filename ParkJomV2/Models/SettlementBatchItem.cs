using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkJomV2.Models;

public class SettlementBatchItem
{
    [Key]
    public int SettlementBatchItemId { get; set; }

    [Required]
    public int SettlementBatchId { get; set; }

    [Required]
    public int TransactionId { get; set; }

    [Required]
    public int OwnerUserId { get; set; }

    [Required]
    [StringLength(100)]
    public string BankName { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string BankCode { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string AccountNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string AccountHolderName { get; set; } = string.Empty;

    [Column(TypeName = "decimal(10,2)")]
    public decimal Amount { get; set; }

    [Required]
    [StringLength(30)]
    public string Status { get; set; } = "Queued";

    [StringLength(100)]
    public string? ClearingReference { get; set; }

    [StringLength(300)]
    public string? FailureReason { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(SettlementBatchId))]
    public SettlementBatch SettlementBatch { get; set; } = null!;

    [ForeignKey(nameof(TransactionId))]
    public Transaction Transaction { get; set; } = null!;

    [ForeignKey(nameof(OwnerUserId))]
    public User OwnerUser { get; set; } = null!;
}
