using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkJomV2.Models;

public class PlatformWallet
{
    [Key]
    public int PlatformWalletId { get; set; }

    // Current available platform balance
    [Column(TypeName = "decimal(10,2)")]
    public decimal Balance { get; set; } = 0.00m;

    // Total platform revenue accumulated (retained 10% commission)
    [Column(TypeName = "decimal(10,2)")]
    public decimal TotalRevenue { get; set; } = 0.00m;

    // Total amount refunded/reversed from platform revenue
    [Column(TypeName = "decimal(10,2)")]
    public decimal TotalRefunded { get; set; } = 0.00m;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property for the platform commission ledger.
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
