using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.Models.Enums;
using Stripe;
using Stripe.Checkout;

namespace ParkJomV2.Services.Notifications.Workers;

/// <summary>One-shot retention and lifecycle maintenance invoked by the VM cron scheduler.</summary>
public sealed class CronMaintenanceJob
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CronMaintenanceJob> _logger;

    public CronMaintenanceJob(
        ApplicationDbContext db,
        IConfiguration configuration,
        ILogger<CronMaintenanceJob> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<int> DeleteOldAccessLogsAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-3);
        var removed = await _db.AccessLogs
            .Where(log => log.AccessedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        _logger.LogInformation("Deleted {Count} access logs older than {CutoffUtc}.", removed, cutoff);
        return removed;
    }

    public async Task<int> DeleteOldIoTStatusLogsAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-3);
        var removed = await _db.IoTStatusLogs
            .Where(log => log.LoggedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        _logger.LogInformation("Deleted {Count} IoT status logs older than {CutoffUtc}.", removed, cutoff);
        return removed;
    }

    /// <summary>
    /// Closes pending wallet top-ups created more than ten minutes ago. Open Stripe
    /// sessions are expired first; paid/completed sessions are left for webhook processing.
    /// </summary>
    public async Task<int> FailStalePendingPaymentsAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        var stalePayments = await _db.Payments
            .Where(payment => payment.Status == PaymentStatus.Pending && payment.CreatedAt < cutoff)
            .OrderBy(payment => payment.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (stalePayments.Count == 0) return 0;

        var apiKey = _configuration["Stripe:SecretKey"];
        if (stalePayments.Any(payment => !string.IsNullOrWhiteSpace(payment.StripeSessionId)) &&
            string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Stripe:SecretKey is required to safely expire stale pending top-ups.");
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
            StripeConfiguration.ApiKey = apiKey;

        var sessionService = new SessionService();
        var failed = 0;
        foreach (var payment in stalePayments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!string.IsNullOrWhiteSpace(payment.StripeSessionId))
                {
                    var session = await sessionService.GetAsync(
                        payment.StripeSessionId,
                        cancellationToken: cancellationToken);

                    // Keep a completed or paid checkout pending until its verified webhook
                    // credits the wallet. Never fail a payment based only on its age.
                    if (string.Equals(session.Status, "complete", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(session.PaymentStatus, "no_payment_required", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.Equals(session.Status, "open", StringComparison.OrdinalIgnoreCase))
                    {
                        session = await sessionService.ExpireAsync(
                            payment.StripeSessionId,
                            cancellationToken: cancellationToken);
                    }

                    if (!string.Equals(session.Status, "expired", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                payment.Status = PaymentStatus.Failed;
                failed++;
            }
            catch (StripeException ex)
            {
                // Stripe may be unavailable or the session may be in a race with payment.
                // Leave the row pending and retry on the next VM invocation.
                _logger.LogWarning(ex,
                    "Could not safely close stale pending PaymentId {PaymentId}; it will be retried.",
                    payment.PaymentId);
            }
        }

        if (failed > 0)
            await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Marked {Count} stale pending top-up payments as failed.", failed);
        return failed;
    }

    /// <summary>
    /// Marks checked-in active bookings completed when their booked period has ended.
    /// ActualExitAt remains unset because this job has no physical exit confirmation.
    /// </summary>
    public async Task<int> CompleteExpiredBookingsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var expiredBookings = await _db.Bookings
            .Where(booking => booking.BookingStatus == BookingStatus.Active &&
                              booking.EndDate <= now &&
                              booking.ActualExitAt == null)
            .ToListAsync(cancellationToken);

        foreach (var booking in expiredBookings)
        {
            booking.BookingStatus = BookingStatus.Completed;
            booking.UpdatedAt = now;
        }

        if (expiredBookings.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Marked {Count} expired active bookings as completed.", expiredBookings.Count);
        return expiredBookings.Count;
    }
}
