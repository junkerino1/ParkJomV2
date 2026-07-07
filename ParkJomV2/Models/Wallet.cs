using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class Wallet
{
    [Key]
    public int WalletId { get; set; }

    [Required]
    public int UserId { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal Balance { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal OnHold { get; set; }

    [Required]
    public WalletStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation Properties

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}