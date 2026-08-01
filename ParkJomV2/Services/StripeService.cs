using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using Stripe;
using Stripe.Checkout;

namespace ParkJomV2.Services;

public class StripeService
{
    private const decimal MinTopUpAmount = 10m;
    private const decimal MaxTopUpAmount = 5000m;

    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<StripeService> _logger;

    public StripeService(IConfiguration configuration, ApplicationDbContext context, ILogger<StripeService> logger)
    {
        _configuration = configuration;
        _context = context;
        _logger = logger;
    }

    public async Task<WalletTopUpResponse> CreateTopUpSessionAsync(int userId, StripeTopUpRequest request)
    {
        if (request.Amount < MinTopUpAmount || request.Amount > MaxTopUpAmount)
        {
            throw new InvalidOperationException($"Top up amount must be between RM{MinTopUpAmount:0.00} and RM{MaxTopUpAmount:0.00}.");
        }

        var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
        if (wallet == null)
        {
            throw new InvalidOperationException("Wallet not found for the current user.");
        }

        var currency = _configuration["Stripe:Currency"] ?? "myr";
        var secretKey = _configuration["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException("Stripe secret key is not configured.");
        }

        StripeConfiguration.ApiKey = secretKey;

        var payment = new Payment
        {
            UserId = userId,
            WalletId = wallet.WalletId,
            Amount = request.Amount,
            Currency = currency.ToUpperInvariant(),
            Status = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();

        var apiBaseUrl = _configuration["ApiBaseUrl"]?.TrimEnd('/')
            ?? throw new InvalidOperationException("ApiBaseUrl is not configured.");

        // var localhost = _configuration["localhost"]?.TrimEnd('/')
        //     ?? throw new InvalidOperationException("localhost is not configured.");

        var successUrl = $"{apiBaseUrl}/api/wallet/topup/success?session_id={{CHECKOUT_SESSION_ID}}";
        var cancelUrl = $"{apiBaseUrl}/api/wallet/topup/cancel";

        // var successUrl = $"{localhost}/api/wallet/topup/success?session_id={{CHECKOUT_SESSION_ID}}";
        // var cancelUrl = $"{localhost}/api/wallet/topup/cancel";
        
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? $"Wallet Top Up - RM{request.Amount:0.00}"
            : request.Description.Trim();

        var sessionService = new SessionService();
        var sessionOptions = new SessionCreateOptions
        {
            Mode = "payment",
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions>
            {
                new()
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = currency,
                        UnitAmount = (long)Math.Round(request.Amount * 100m, MidpointRounding.AwayFromZero),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = "ParkJom Wallet Top Up",
                            Description = description
                        }
                    }
                }
            },
            Metadata = new Dictionary<string, string>
            {
                ["PaymentId"] = payment.PaymentId.ToString(),
                ["UserId"] = userId.ToString(),
                ["WalletId"] = wallet.WalletId.ToString()
            }
        };

        var session = sessionService.Create(sessionOptions);

        payment.StripeSessionId = session.Id;
        payment.StripePaymentIntentId = session.PaymentIntentId;
        await _context.SaveChangesAsync();

        return new WalletTopUpResponse
        {
            PaymentId = payment.PaymentId,
            SessionId = session.Id,
            CheckoutUrl = session.Url ?? string.Empty
        };
    }

    public async Task ProcessWebhookAsync(Event stripeEvent)
    {
        if (!string.Equals(stripeEvent.Type, "checkout.session.completed", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var session = stripeEvent.Data.Object as Session;
        if (session == null)
        {
            return;
        }

        Payment? payment = null;
        if (session.Metadata != null &&
            session.Metadata.TryGetValue("PaymentId", out var paymentIdText) &&
            int.TryParse(paymentIdText, out var paymentId))
        {
            payment = await _context.Payments
                .Include(p => p.Wallet)
                .FirstOrDefaultAsync(p => p.PaymentId == paymentId);
        }

        payment ??= await _context.Payments
            .Include(p => p.Wallet)
            .FirstOrDefaultAsync(p => p.StripeSessionId == session.Id);

        if (payment == null)
        {
            _logger.LogWarning("Stripe webhook received for unknown payment. SessionId={SessionId}", session.Id);
            return;
        }

        if (payment.Status == PaymentStatus.Completed)
        {
            return;
        }

        await using var dbTransaction = await _context.Database.BeginTransactionAsync();

        payment = await _context.Payments
            .Include(p => p.Wallet)
            .FirstOrDefaultAsync(p => p.PaymentId == payment.PaymentId);

        if (payment == null || payment.Status == PaymentStatus.Completed)
        {
            await dbTransaction.RollbackAsync();
            return;
        }

        payment.Status = PaymentStatus.Completed;
        payment.CompletedAt = DateTime.UtcNow;
        payment.StripeSessionId = session.Id;
        payment.StripePaymentIntentId = session.PaymentIntentId;

        payment.Wallet.Balance += payment.Amount;
        payment.Wallet.UpdatedAt = DateTime.UtcNow;

        _context.Transactions.Add(new Transaction
        {
            WalletId = payment.WalletId,
            TransactionType = TransactionType.TopUp,
            Amount = payment.Amount,
            PaymentMethod = ParkJomV2.Models.Enums.PaymentMethod.CreditCard,
            TransactionStatus = TransactionStatus.Completed,
            ReferenceNumber = $"TOPUP-{payment.PaymentId}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        await dbTransaction.CommitAsync();
    }
}