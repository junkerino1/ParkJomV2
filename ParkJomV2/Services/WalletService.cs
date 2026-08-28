using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.Models;

namespace ParkJomV2.Services;

/// <summary>
/// Applies wallet balance movements (deduct / hold) and the platform wallet's atomic
/// server-side increment. Mutations are NOT saved here, so callers persist them within
/// their own atomic database transaction (e.g. a booking confirmation).
/// </summary>
public class WalletService
{
    private readonly ApplicationDbContext _context;

    public WalletService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>Deducts an amount from a wallet's available balance.</summary>
    public void Deduct(Wallet wallet, decimal amount, DateTime now)
    {
        wallet.Balance -= amount;
        wallet.UpdatedAt = now;
    }

    /// <summary>Adds an amount to a wallet's on-hold balance (e.g. owner payout held until settlement).</summary>
    public void Hold(Wallet wallet, decimal amount, DateTime now)
    {
        wallet.OnHold += amount;
        wallet.UpdatedAt = now;
    }

    /// <summary>
    /// Settles a held owner payout into the owner's available balance when a booking completes.
    /// Moves money from OnHold to Balance (+ Balance, − OnHold).
    /// Caller must ensure <paramref name="amount"/> does not exceed the wallet's current OnHold.
    /// </summary>
    public void SettleOwnerPayout(Wallet wallet, decimal amount, DateTime now)
    {
        wallet.Balance += amount;
        wallet.OnHold -= amount;
        wallet.UpdatedAt = now;
    }

    /// <summary>
    /// Atomically increments the platform wallet's balance and total revenue on the server.
    /// A single UPDATE avoids lost updates under concurrent bookings and only briefly locks the row.
    /// </summary>
    public Task IncrementPlatformWalletAsync(PlatformWallet platformWallet, decimal amount, DateTime now, CancellationToken cancellationToken)
    {
        return _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE [PlatformWallets]
            SET [Balance] = [Balance] + {amount},
                [TotalRevenue] = [TotalRevenue] + {amount},
                [UpdatedAt] = {now}
            WHERE [PlatformWalletId] = {platformWallet.PlatformWalletId}
            """,
            cancellationToken);
    }
}
