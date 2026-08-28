using ParkJomV2.Data;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Services;

/// <summary>
/// Creates <see cref="Transaction"/> ledger rows for wallet movements (user wallets and the
/// platform wallet). Transactions are added to the context but NOT saved here, so callers can
/// persist them together in their own atomic database transaction (e.g. a booking confirmation).
/// </summary>
public class TransactionService
{
    private readonly ApplicationDbContext _context;

    public TransactionService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Creates a transaction for one wallet movement and registers it on the context.
    /// Pass <paramref name="walletId"/> for a user wallet, or <paramref name="platformWalletId"/>
    /// for the platform wallet (exactly one should be set). <paramref name="booking"/> is optional
    /// (pass null for wallet top-ups), and <paramref name="paymentMethod"/> records how the funds moved.
    /// </summary>
    public Transaction Create(
        int? walletId,
        int? platformWalletId,
        Booking? booking,
        TransactionType type,
        decimal amount,
        PaymentMethod paymentMethod,
        string reference,
        DateTime now)
    {
        var transaction = new Transaction
        {
            WalletId = walletId,
            PlatformWalletId = platformWalletId,
            Booking = booking,
            TransactionType = type,
            Amount = amount,
            PaymentMethod = paymentMethod,
            TransactionStatus = TransactionStatus.Completed,
            ReferenceNumber = reference,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.Transactions.Add(transaction);
        return transaction;
    }
}
