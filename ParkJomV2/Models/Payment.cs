using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class Payment
{
    [Key]
    public int PaymentId { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public int WalletId { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal Amount { get; set; }

    [Required]
    [StringLength(10)]
    public string Currency { get; set; } = "MYR";

    [Required]
    public PaymentStatus Status { get; set; }

    [StringLength(100)]
    public string? StripeSessionId { get; set; }

    [StringLength(100)]
    public string? StripePaymentIntentId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    // Navigation Properties

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [ForeignKey(nameof(WalletId))]
    public Wallet Wallet { get; set; } = null!;
}
