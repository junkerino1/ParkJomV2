using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class Transaction
{
    [Key]
    public int TransactionId { get; set; }

    [Required]
    public int WalletId { get; set; }

    public int? BookingId { get; set; }

    [Required]
    public TransactionType TransactionType { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal Amount { get; set; }

    [Required]
    public PaymentMethod PaymentMethod { get; set; }

    [Required]
    public TransactionStatus TransactionStatus { get; set; }

    [Required]
    [StringLength(100)]
    public string ReferenceNumber { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation Properties

    [ForeignKey(nameof(WalletId))]
    public Wallet Wallet { get; set; } = null!;

    [ForeignKey(nameof(BookingId))]
    public Booking? Booking { get; set; }
}