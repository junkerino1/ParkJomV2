using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.Services.Notifications.Workers;
using ParkJomV2.Services.Support.Workers;

namespace ParkJomV2.Controllers;

/// <summary>One-shot job endpoints called by the separately hosted VM cron scheduler.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/internal/cron")]
public sealed class InternalCronController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly BookingReminderJob _bookingReminderJob;
    private readonly OverstayMonitoringJob _overstayMonitoringJob;
    private readonly NotificationCleanupJob _notificationCleanupJob;
    private readonly SupportSlaJob _supportSlaJob;
    private readonly IncidentEscalationJob _incidentEscalationJob;
    private readonly NotificationDispatchWorker _notificationDispatchWorker;
    private readonly CronMaintenanceJob _cronMaintenanceJob;

    public InternalCronController(
        IConfiguration configuration,
        BookingReminderJob bookingReminderJob,
        OverstayMonitoringJob overstayMonitoringJob,
        NotificationCleanupJob notificationCleanupJob,
        SupportSlaJob supportSlaJob,
        IncidentEscalationJob incidentEscalationJob,
        NotificationDispatchWorker notificationDispatchWorker,
        CronMaintenanceJob cronMaintenanceJob)
    {
        _configuration = configuration;
        _bookingReminderJob = bookingReminderJob;
        _overstayMonitoringJob = overstayMonitoringJob;
        _notificationCleanupJob = notificationCleanupJob;
        _supportSlaJob = supportSlaJob;
        _incidentEscalationJob = incidentEscalationJob;
        _notificationDispatchWorker = notificationDispatchWorker;
        _cronMaintenanceJob = cronMaintenanceJob;
    }

    /// <summary>Runs one pass of start and end booking reminders.</summary>
    /// <remarks>Suggested schedule: every minute. The VM must send the configured shared secret in X-Cron-Secret.</remarks>
    /// <param name="cronSecret">The VM cron shared secret from the X-Cron-Secret header.</param>
    /// <param name="cancellationToken">Cancels the job if the HTTP request is aborted.</param>
    /// <response code="200">The reminder pass completed. The response includes the job name and completion time.</response>
    /// <response code="401">The supplied cron secret is invalid.</response>
    /// <response code="503">Cron:SharedSecret is missing or shorter than 32 UTF-8 bytes.</response>
    [ProducesResponseType(typeof(CronJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [HttpPost("booking-reminders")]
    public async Task<IActionResult> RunBookingReminders(
        [FromHeader(Name = "X-Cron-Secret")] string? cronSecret,
        CancellationToken cancellationToken)
    {
        var authorizationError = ValidateCronSecret(cronSecret);
        if (authorizationError is not null) return authorizationError;
        await _bookingReminderJob.RunOnceAsync(cancellationToken);
        return JobCompleted("booking-reminders");
    }

    /// <summary>Checks fresh device telemetry and sends overdue parking warnings.</summary>
    /// <remarks>Suggested schedule: every minute. The VM must send the configured shared secret in X-Cron-Secret.</remarks>
    /// <param name="cronSecret">The VM cron shared secret from the X-Cron-Secret header.</param>
    /// <param name="cancellationToken">Cancels the job if the HTTP request is aborted.</param>
    /// <response code="200">The telemetry pass completed; processed is the number of warnings sent.</response>
    /// <response code="401">The supplied cron secret is invalid.</response>
    /// <response code="503">Cron:SharedSecret is missing or shorter than 32 UTF-8 bytes.</response>
    [ProducesResponseType(typeof(CronJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [HttpPost("overstay-monitoring")]
    public async Task<IActionResult> RunOverstayMonitoring(
        [FromHeader(Name = "X-Cron-Secret")] string? cronSecret,
        CancellationToken cancellationToken)
    {
        var authorizationError = ValidateCronSecret(cronSecret);
        if (authorizationError is not null) return authorizationError;
        var warningsSent = await _overstayMonitoringJob.RunOnceAsync(cancellationToken);
        return JobCompleted("overstay-monitoring", warningsSent);
    }

    /// <summary>Purges expired notifications and unused push subscriptions.</summary>
    /// <remarks>Suggested schedule: daily at 03:15 Asia/Singapore. The VM must send the configured shared secret in X-Cron-Secret.</remarks>
    /// <param name="cronSecret">The VM cron shared secret from the X-Cron-Secret header.</param>
    /// <param name="cancellationToken">Cancels the job if the HTTP request is aborted.</param>
    /// <response code="200">The cleanup pass completed; processed is the number of removed records.</response>
    /// <response code="401">The supplied cron secret is invalid.</response>
    /// <response code="503">Cron:SharedSecret is missing or shorter than 32 UTF-8 bytes.</response>
    [ProducesResponseType(typeof(CronJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [HttpPost("notification-cleanup")]
    public async Task<IActionResult> RunNotificationCleanup(
        [FromHeader(Name = "X-Cron-Secret")] string? cronSecret,
        CancellationToken cancellationToken)
    {
        var authorizationError = ValidateCronSecret(cronSecret);
        if (authorizationError is not null) return authorizationError;
        var removed = await _notificationCleanupJob.RunOnceAsync(cancellationToken);
        return JobCompleted("notification-cleanup", removed);
    }

    /// <summary>Checks active support tickets for SLA breaches.</summary>
    /// <remarks>Suggested schedule: every minute. The VM must send the configured shared secret in X-Cron-Secret.</remarks>
    /// <param name="cronSecret">The VM cron shared secret from the X-Cron-Secret header.</param>
    /// <param name="cancellationToken">Cancels the job if the HTTP request is aborted.</param>
    /// <response code="200">The SLA pass completed; processed is the number of breached tickets processed.</response>
    /// <response code="401">The supplied cron secret is invalid.</response>
    /// <response code="503">Cron:SharedSecret is missing or shorter than 32 UTF-8 bytes.</response>
    [ProducesResponseType(typeof(CronJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [HttpPost("support-sla")]
    public async Task<IActionResult> RunSupportSla(
        [FromHeader(Name = "X-Cron-Secret")] string? cronSecret,
        CancellationToken cancellationToken)
    {
        var authorizationError = ValidateCronSecret(cronSecret);
        if (authorizationError is not null) return authorizationError;
        var breachedTickets = await _supportSlaJob.RunOnceAsync(cancellationToken);
        return JobCompleted("support-sla", breachedTickets);
    }

    /// <summary>Escalates support incidents whose configured response deadline has passed.</summary>
    /// <remarks>Suggested schedule: every minute. The VM must send the configured shared secret in X-Cron-Secret.</remarks>
    /// <param name="cronSecret">The VM cron shared secret from the X-Cron-Secret header.</param>
    /// <param name="cancellationToken">Cancels the job if the HTTP request is aborted.</param>
    /// <response code="200">The escalation pass completed; processed is the number of incidents escalated.</response>
    /// <response code="401">The supplied cron secret is invalid.</response>
    /// <response code="503">Cron:SharedSecret is missing or shorter than 32 UTF-8 bytes.</response>
    [ProducesResponseType(typeof(CronJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [HttpPost("incident-escalation")]
    public async Task<IActionResult> RunIncidentEscalation(
        [FromHeader(Name = "X-Cron-Secret")] string? cronSecret,
        CancellationToken cancellationToken)
    {
        var authorizationError = ValidateCronSecret(cronSecret);
        if (authorizationError is not null) return authorizationError;
        var escalated = await _incidentEscalationJob.RunOnceAsync(cancellationToken);
        return JobCompleted("incident-escalation", escalated);
    }

    /// <summary>Retries notification deliveries that are pending and due.</summary>
    /// <remarks>Suggested schedule: every minute. The VM must send the configured shared secret in X-Cron-Secret.</remarks>
    /// <param name="cronSecret">The VM cron shared secret from the X-Cron-Secret header.</param>
    /// <param name="cancellationToken">Cancels the job if the HTTP request is aborted.</param>
    /// <response code="200">The retry pass completed; processed is the number of deliveries processed.</response>
    /// <response code="401">The supplied cron secret is invalid.</response>
    /// <response code="503">Cron:SharedSecret is missing or shorter than 32 UTF-8 bytes.</response>
    [ProducesResponseType(typeof(CronJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [HttpPost("notification-retry-sweep")]
    public async Task<IActionResult> RunNotificationRetrySweep(
        [FromHeader(Name = "X-Cron-Secret")] string? cronSecret,
        CancellationToken cancellationToken)
    {
        var authorizationError = ValidateCronSecret(cronSecret);
        if (authorizationError is not null) return authorizationError;
        var processed = await _notificationDispatchWorker.RunRetrySweepOnceAsync(cancellationToken);
        return JobCompleted("notification-retry-sweep", processed);
    }

    /// <summary>Deletes access logs older than three days.</summary>
    /// <remarks>Suggested schedule: daily at 03:20 Asia/Singapore. Deletes records by AccessedAt. Requires X-Cron-Secret.</remarks>
    /// <param name="cronSecret">The VM cron shared secret from the X-Cron-Secret header.</param>
    /// <param name="cancellationToken">Cancels the job if the HTTP request is aborted.</param>
    /// <response code="200">Cleanup completed; processed is the number of access log rows deleted.</response>
    /// <response code="401">The supplied cron secret is invalid.</response>
    /// <response code="503">Cron:SharedSecret is missing or shorter than 32 UTF-8 bytes.</response>
    [ProducesResponseType(typeof(CronJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [HttpPost("access-log-cleanup")]
    public async Task<IActionResult> RunAccessLogCleanup(
        [FromHeader(Name = "X-Cron-Secret")] string? cronSecret,
        CancellationToken cancellationToken)
    {
        var authorizationError = ValidateCronSecret(cronSecret);
        if (authorizationError is not null) return authorizationError;
        var removed = await _cronMaintenanceJob.DeleteOldAccessLogsAsync(cancellationToken);
        return JobCompleted("access-log-cleanup", removed);
    }

    /// <summary>Deletes IoT status logs older than three days.</summary>
    /// <remarks>Suggested schedule: daily at 03:20 Asia/Singapore. Deletes records by LoggedAt. Requires X-Cron-Secret.</remarks>
    /// <param name="cronSecret">The VM cron shared secret from the X-Cron-Secret header.</param>
    /// <param name="cancellationToken">Cancels the job if the HTTP request is aborted.</param>
    /// <response code="200">Cleanup completed; processed is the number of IoT status log rows deleted.</response>
    /// <response code="401">The supplied cron secret is invalid.</response>
    /// <response code="503">Cron:SharedSecret is missing or shorter than 32 UTF-8 bytes.</response>
    [ProducesResponseType(typeof(CronJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [HttpPost("iot-status-log-cleanup")]
    public async Task<IActionResult> RunIoTStatusLogCleanup(
        [FromHeader(Name = "X-Cron-Secret")] string? cronSecret,
        CancellationToken cancellationToken)
    {
        var authorizationError = ValidateCronSecret(cronSecret);
        if (authorizationError is not null) return authorizationError;
        var removed = await _cronMaintenanceJob.DeleteOldIoTStatusLogsAsync(cancellationToken);
        return JobCompleted("iot-status-log-cleanup", removed);
    }

    /// <summary>Fails pending wallet top-ups older than ten minutes after safely closing their Stripe checkout sessions.</summary>
    /// <remarks>Suggested schedule: every five minutes. Stripe sessions that are paid or complete remain eligible for webhook crediting. Open sessions are expired before the local status changes. Requires X-Cron-Secret.</remarks>
    /// <param name="cronSecret">The VM cron shared secret from the X-Cron-Secret header.</param>
    /// <param name="cancellationToken">Cancels the job if the HTTP request is aborted.</param>
    /// <response code="200">Timeout pass completed; processed is the number of payments marked Failed.</response>
    /// <response code="401">The supplied cron secret is invalid.</response>
    /// <response code="503">Cron:SharedSecret is missing or shorter than 32 UTF-8 bytes.</response>
    [ProducesResponseType(typeof(CronJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [HttpPost("pending-payment-failure")]
    public async Task<IActionResult> RunPendingPaymentFailure(
        [FromHeader(Name = "X-Cron-Secret")] string? cronSecret,
        CancellationToken cancellationToken)
    {
        var authorizationError = ValidateCronSecret(cronSecret);
        if (authorizationError is not null) return authorizationError;
        var failed = await _cronMaintenanceJob.FailStalePendingPaymentsAsync(cancellationToken);
        return JobCompleted("pending-payment-failure", failed);
    }

    /// <summary>Marks checked-in active bookings completed after their booked period has ended.</summary>
    /// <remarks>Suggested schedule: daily at 08:10 Asia/Singapore. Booking dates are stored as date-only midnight values and compared with UTC. This updates BookingStatus only; ActualExitAt stays unset until the ESP32 confirms a physical exit. Requires X-Cron-Secret.</remarks>
    /// <param name="cronSecret">The VM cron shared secret from the X-Cron-Secret header.</param>
    /// <param name="cancellationToken">Cancels the job if the HTTP request is aborted.</param>
    /// <response code="200">Completion pass completed; processed is the number of bookings marked Completed.</response>
    /// <response code="401">The supplied cron secret is invalid.</response>
    /// <response code="503">Cron:SharedSecret is missing or shorter than 32 UTF-8 bytes.</response>
    [ProducesResponseType(typeof(CronJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CronErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [HttpPost("expired-booking-completion")]
    public async Task<IActionResult> RunExpiredBookingCompletion(
        [FromHeader(Name = "X-Cron-Secret")] string? cronSecret,
        CancellationToken cancellationToken)
    {
        var authorizationError = ValidateCronSecret(cronSecret);
        if (authorizationError is not null) return authorizationError;
        var completed = await _cronMaintenanceJob.CompleteExpiredBookingsAsync(cancellationToken);
        return JobCompleted("expired-booking-completion", completed);
    }

    private IActionResult? ValidateCronSecret(string? suppliedSecret)
    {
        var configuredSecret = _configuration["Cron:SharedSecret"];
        if (string.IsNullOrWhiteSpace(configuredSecret) || Encoding.UTF8.GetByteCount(configuredSecret) < 32)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new CronErrorResponse(
                false,
                "CRON_SECRET_NOT_CONFIGURED",
                "Cron job endpoints require Cron:SharedSecret to be configured with at least 32 UTF-8 bytes."));
        }

        var expectedBytes = Encoding.UTF8.GetBytes(configuredSecret);
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedSecret ?? string.Empty);
        if (suppliedBytes.Length == 0 || !CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes))
        {
            return Unauthorized(new CronErrorResponse(
                false,
                "INVALID_CRON_SECRET",
                "A valid X-Cron-Secret header is required."));
        }

        return null;
    }

    private IActionResult JobCompleted(string job, int? processed = null)
        => Ok(new CronJobResponse(true, job, processed, DateTime.UtcNow));
}

/// <summary>Successful result returned by a one-shot cron endpoint.</summary>
public sealed record CronJobResponse(bool Success, string Job, int? Processed, DateTime CompletedAtUtc);

/// <summary>Error returned when a cron secret is missing or invalid.</summary>
public sealed record CronErrorResponse(bool Success, string ErrorCode, string Message);
