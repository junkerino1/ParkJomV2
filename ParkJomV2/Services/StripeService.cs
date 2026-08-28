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
    private readonly TransactionService _transactionService;
    private readonly ILogger<StripeService> _logger;

    public StripeService(IConfiguration configuration, ApplicationDbContext context, TransactionService transactionService, ILogger<StripeService> logger)
    {
        _configuration = configuration;
        _context = context;
        _transactionService = transactionService;
        _logger = logger;
    }

    // stripe redirect browser back after payment, this is to create a checkout session for the user to pay
    public async Task<WalletTopUpResponse> CreateTopUpSessionAsync(int userId, StripeTopUpRequest request)
    {
        if (request.Amount < MinTopUpAmount || request.Amount > MaxTopUpAmount)
        {
            throw new InvalidOperationException($"Top up amount must be between RM{MinTopUpAmount:0.00} and RM{MaxTopUpAmount:0.00}.");
        }

        var returnTarget = string.IsNullOrWhiteSpace(request.ReturnTarget)
            ? "web"
            : request.ReturnTarget.Trim().ToLowerInvariant();
        if (returnTarget is not ("web" or "native"))
        {
            throw new InvalidOperationException("ReturnTarget must be either 'web' or 'native'.");
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

        // Stripe returns to HTTPS bridge endpoints. The bridge then redirects only
        // to the configured ParkJom web URL or registered native app scheme.
        var successUrl = $"{apiBaseUrl}/api/wallet/topup/success?returnTarget={returnTarget}&session_id={{CHECKOUT_SESSION_ID}}";
        var cancelUrl = $"{apiBaseUrl}/api/wallet/topup/cancel?returnTarget={returnTarget}";
        
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

    public async Task<WalletSummaryResponse> GetWalletAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var wallet = await _context.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

        if (wallet == null)
        {
            throw new KeyNotFoundException("Wallet not found for the current user.");
        }

        return new WalletSummaryResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Wallet retrieved successfully.",
            WalletId = wallet.WalletId,
            Balance = wallet.Balance,
            OnHold = wallet.OnHold,
            Status = wallet.Status.ToString(),
            UpdatedAt = wallet.UpdatedAt
        };
    }

    public async Task<WalletTopUpStatusResponse> GetTopUpStatusAsync(
        int userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        sessionId = sessionId.Trim();
        if (sessionId.Length == 0 || sessionId.Length > 100)
        {
            throw new InvalidOperationException("A valid Stripe checkout session ID is required.");
        }

        // Resolve through the signed-in user's payment before contacting Stripe so
        // one account can never inspect another account's Checkout Session.
        var payment = await _context.Payments
            .Include(p => p.Wallet)
            .FirstOrDefaultAsync(
                p => p.UserId == userId && p.StripeSessionId == sessionId,
                cancellationToken);

        if (payment == null)
        {
            throw new KeyNotFoundException("Wallet top-up session was not found.");
        }

        // Completed is authoritative in ParkJom because only the verified webhook
        // writes this state and credits the wallet.
        if (payment.Status == PaymentStatus.Completed)
        {
            return BuildTopUpStatusResponse(
                payment,
                state: "completed",
                checkoutStatus: "complete",
                stripePaymentStatus: "paid",
                message: "This top-up has been credited to your wallet.");
        }

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return BuildTopUpStatusResponse(
                payment,
                state: "cancelled",
                checkoutStatus: "closed",
                stripePaymentStatus: "unpaid",
                message: "This top-up was cancelled. Start a new top-up to continue.");
        }

        if (payment.Status == PaymentStatus.Failed)
        {
            return BuildTopUpStatusResponse(
                payment,
                state: "failed",
                checkoutStatus: "closed",
                stripePaymentStatus: "unpaid",
                message: "This top-up failed. Start a new top-up to try again.");
        }

        ConfigureStripe();

        var sessionService = new SessionService();
        var session = await sessionService.GetAsync(sessionId, cancellationToken: cancellationToken);
        var checkoutStatus = session.Status?.Trim().ToLowerInvariant() ?? "unknown";
        var stripePaymentStatus = session.PaymentStatus?.Trim().ToLowerInvariant() ?? "unknown";

        if (checkoutStatus == "expired" && payment.Status == PaymentStatus.Pending)
        {
            payment.Status = PaymentStatus.Cancelled;
            await _context.SaveChangesAsync(cancellationToken);
        }

        var isAwaitingWebhook = checkoutStatus == "complete"
            && stripePaymentStatus is "paid" or "no_payment_required";
        var canContinue = payment.Status == PaymentStatus.Pending
            && checkoutStatus == "open"
            && !string.IsNullOrWhiteSpace(session.Url);

        var (state, message) = (payment.Status, checkoutStatus, isAwaitingWebhook) switch
        {
            (_, _, true) => (
                "processing",
                "Payment was received and is waiting for the secure wallet webhook to finish."),
            (PaymentStatus.Pending, "open", _) => (
                "open",
                "Checkout is still open and can be continued."),
            (PaymentStatus.Cancelled, "expired", _) => (
                "expired",
                "This checkout session has expired. Start a new top-up to continue."),
            (PaymentStatus.Cancelled, _, _) => (
                "cancelled",
                "This top-up was cancelled. Start a new top-up to continue."),
            (PaymentStatus.Failed, _, _) => (
                "failed",
                "This top-up failed. Start a new top-up to try again."),
            _ => (
                "unavailable",
                "The current checkout cannot be continued. Start a new top-up to try again.")
        };

        return BuildTopUpStatusResponse(
            payment,
            state,
            checkoutStatus,
            stripePaymentStatus,
            message,
            canContinue,
            canContinue ? session.Url : null,
            session.ExpiresAt);
    }

    private void ConfigureStripe()
    {
        var secretKey = _configuration["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException("Stripe secret key is not configured.");
        }

        StripeConfiguration.ApiKey = secretKey;
    }

    private static WalletTopUpStatusResponse BuildTopUpStatusResponse(
        Payment payment,
        string state,
        string checkoutStatus,
        string stripePaymentStatus,
        string message,
        bool canContinue = false,
        string? checkoutUrl = null,
        DateTime? expiresAt = null)
    {
        return new WalletTopUpStatusResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = message,
            PaymentId = payment.PaymentId,
            SessionId = payment.StripeSessionId ?? string.Empty,
            Amount = payment.Amount,
            Currency = payment.Currency,
            State = state,
            PaymentStatus = payment.Status.ToString(),
            CheckoutStatus = checkoutStatus,
            StripePaymentStatus = stripePaymentStatus,
            IsCredited = payment.Status == PaymentStatus.Completed,
            CanContinue = canContinue,
            CheckoutUrl = checkoutUrl,
            ExpiresAt = expiresAt,
            WalletBalance = payment.Wallet.Balance,
            WalletUpdatedAt = payment.Wallet.UpdatedAt
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

        _transactionService.Create(
            payment.WalletId,
            null,
            booking: null,
            TransactionType.TopUp,
            payment.Amount,
            ParkJomV2.Models.Enums.PaymentMethod.Stripe,
            session.PaymentIntentId,
            DateTime.UtcNow);

        await _context.SaveChangesAsync();
        await dbTransaction.CommitAsync();
    }
}
