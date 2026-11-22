using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services.Notifications;

namespace ParkJomV2.Services.Overstay;

public sealed class OverstayEnforcementService
{
    private static readonly TimeSpan MalaysiaOffset = TimeSpan.FromHours(8);

    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly INotificationService _notifications;
    private readonly Sms360Service _sms360Service;
    private readonly WalletService _walletService;
    private readonly AccessLogService _accessLogService;
    private readonly IMqttService _mqttService;
    private readonly OverstayWorkerStatusTracker _workerTracker;
    private readonly ILogger<OverstayEnforcementService> _logger;

    public OverstayEnforcementService(
        ApplicationDbContext db,
        IConfiguration configuration,
        INotificationService notifications,
        Sms360Service sms360Service,
        WalletService walletService,
        AccessLogService accessLogService,
        IMqttService mqttService,
        OverstayWorkerStatusTracker workerTracker,
        ILogger<OverstayEnforcementService> logger)
    {
        _db = db;
        _configuration = configuration;
        _notifications = notifications;
        _sms360Service = sms360Service;
        _walletService = walletService;
        _accessLogService = accessLogService;
        _mqttService = mqttService;
        _workerTracker = workerTracker;
        _logger = logger;
    }

    public static (decimal BaseHourlyRate, decimal HourlyPenaltyTariff, int BillableHours, decimal CalculatedPenalty) CalculateTariff(
        decimal dailyRateSnapshot,
        decimal windowHours,
        int overstayMinutes,
        decimal penaltyMultiplier,
        decimal maxDailyPenalty)
    {
        var safeHours = windowHours > 0m ? windowHours : 8m;
        var baseHourlyRate = dailyRateSnapshot > 0m
            ? Math.Round(dailyRateSnapshot / safeHours, 2, MidpointRounding.AwayFromZero)
            : 5.00m;
        var effectiveMultiplier = penaltyMultiplier >= 1.0m ? penaltyMultiplier : 1.50m;
        var hourlyPenaltyTariff = Math.Max(2.00m,
            Math.Round(baseHourlyRate * effectiveMultiplier, 2, MidpointRounding.AwayFromZero));
        var billableHours = Math.Max(1, (int)Math.Ceiling(Math.Max(1, overstayMinutes) / 60.0));
        var rawPenalty = billableHours * hourlyPenaltyTariff;
        var calculatedPenalty = maxDailyPenalty > 0m ? Math.Min(rawPenalty, maxDailyPenalty) : rawPenalty;
        return (baseHourlyRate, hourlyPenaltyTariff, billableHours, calculatedPenalty);
    }

    public async Task<OverstaySummaryDTO> GetSummaryAsync(CancellationToken ct = default)
    {
        var config = await GetPolicyAsync(ct);
        var cases = await _db.OverstayCases
            .AsNoTracking()
            .Select(c => new
            {
                c.Status,
                c.PaymentStatus,
                c.HardwareLockActive,
                c.CalculatedPenaltyAmount,
                c.DebitedAmount,
                c.WaivedAmount
            })
            .ToListAsync(ct);

        var openStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Detected", "WarningSent", "Penalized", "Disputed" };
        var closedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Waived", "Released", "Resolved" };

        var totalCalculated = cases.Sum(c => c.CalculatedPenaltyAmount);
        var totalCollected = cases.Sum(c => c.DebitedAmount);
        var totalWaived = cases.Sum(c => c.WaivedAmount);
        var totalOutstanding = cases.Sum(c => Math.Max(0m, c.CalculatedPenaltyAmount - c.DebitedAmount - c.WaivedAmount));

        return new OverstaySummaryDTO
        {
            GracePeriodMinutes = config.GracePeriodMinutes,
            PenaltyMultiplier = config.OverstayPenaltyMultiplier,
            MaxDailyPenalty = config.OverstayMaxDailyPenalty,
            HardwareExitLockEnabled = config.HardwareExitLockEnabled,
            ActiveOverstaysCount = cases.Count(c => openStatuses.Contains(c.Status)),
            WarningsSentCount = cases.Count(c => string.Equals(c.Status, "WarningSent", StringComparison.OrdinalIgnoreCase)),
            PaymentDueCount = cases.Count(c => string.Equals(c.PaymentStatus, "PaymentDue", StringComparison.OrdinalIgnoreCase)),
            DisputedCount = cases.Count(c => string.Equals(c.Status, "Disputed", StringComparison.OrdinalIgnoreCase)),
            HardwareLockedCount = cases.Count(c => c.HardwareLockActive),
            WaivedOrResolvedCount = cases.Count(c => closedStatuses.Contains(c.Status)),
            TotalCalculatedPenalties = totalCalculated,
            TotalCollectedPenalties = totalCollected,
            TotalOutstandingPenalties = totalOutstanding,
            TotalWaivedPenalties = totalWaived,
            WorkerStatus = _workerTracker.Snapshot(),
            GeneratedAtUtc = DateTime.UtcNow
        };
    }

    public async Task<OverstayCaseListResponse> GetCasesAsync(
        string? status,
        string? paymentStatus,
        bool? hardwareLocked,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.OverstayCases
            .AsNoTracking()
            .Include(c => c.Booking)
                .ThenInclude(b => b.Vehicle)
            .Include(c => c.Renter)
            .Include(c => c.ParkingSpot)
                .ThenInclude(s => s.Property)
            .Include(c => c.IoTDevice)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = status.Trim().ToLowerInvariant();
            query = normalized switch
            {
                "open" => query.Where(c => c.Status == "Detected" || c.Status == "WarningSent" || c.Status == "Penalized" || c.Status == "Disputed"),
                "closed" => query.Where(c => c.Status == "Waived" || c.Status == "Released" || c.Status == "Resolved"),
                _ => query.Where(c => c.Status == status.Trim())
            };
        }

        if (!string.IsNullOrWhiteSpace(paymentStatus) && !string.Equals(paymentStatus, "all", StringComparison.OrdinalIgnoreCase))
        {
            var ps = paymentStatus.Trim();
            query = query.Where(c => c.PaymentStatus == ps);
        }

        if (hardwareLocked.HasValue)
        {
            query = query.Where(c => c.HardwareLockActive == hardwareLocked.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c =>
                c.CaseReference.Contains(term) ||
                c.Booking.BookingReference.Contains(term) ||
                c.Booking.Vehicle.NumberPlate.Contains(term) ||
                c.Renter.Email.Contains(term) ||
                (c.Renter.FirstName != null && c.Renter.FirstName.Contains(term)) ||
                (c.Renter.LastName != null && c.Renter.LastName.Contains(term)) ||
                (c.ParkingSpot.ParkingLabel != null && c.ParkingSpot.ParkingLabel.Contains(term)) ||
                c.ParkingSpot.Property.PropertyName.Contains(term));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.HardwareLockActive)
            .ThenByDescending(c => c.DetectedAtUtc)
            .ThenByDescending(c => c.OverstayCaseId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new OverstayCaseListResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = items.Count > 0 ? "Overstay cases retrieved." : "No matching overstay cases found.",
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize),
            Data = items.Select(MapDto).ToList()
        };
    }

    public async Task<OverstayScanResultDTO> RunScanAsync(int? triggeredByAdminId = null, CancellationToken ct = default)
    {
        _workerTracker.RecordScanStarted();
        var nowUtc = DateTime.UtcNow;
        var config = await GetPolicyAsync(ct);

        try
        {
            var localNow = new DateTimeOffset(nowUtc, TimeSpan.Zero).ToOffset(MalaysiaOffset);
            var localDate = DateOnly.FromDateTime(localNow.DateTime);

            var activeBookings = await _db.Bookings
                .Include(b => b.Renter)
                .Include(b => b.Vehicle)
                .Include(b => b.ParkingSpot)
                    .ThenInclude(s => s.Property)
                .Include(b => b.ParkingSpot)
                    .ThenInclude(s => s.IoTDevice)
                .Include(b => b.ParkingSpot)
                    .ThenInclude(s => s.ParkingAvailabilities)
                .Where(b => b.BookingStatus == BookingStatus.Active && b.ActualExitAt == null)
                .ToListAsync(ct);

            var freshnessMinutes = Math.Clamp(_configuration.GetValue<int?>("IoT:TelemetryFreshnessMinutes") ?? 10, 5, 30);
            var freshSince = nowUtc.AddMinutes(-freshnessMinutes);
            var topicRoot = (_configuration["Mqtt:TopicRoot"] ?? "parkjom/devices").TrimEnd('/');

            var deviceSerials = activeBookings
                .Select(b => b.ParkingSpot.IoTDevice)
                .Where(d => d is { IsAssigned: true } && !string.IsNullOrWhiteSpace(d.Esp32Serial))
                .DistinctBy(d => d!.IoTDeviceId)
                .ToList();

            var deviceIds = deviceSerials.Select(d => d!.IoTDeviceId).ToList();
            var expectedTopics = deviceSerials.Select(d => $"{topicRoot}/{d!.Esp32Serial}/telemetry").ToList();

            var latestByDevice = new Dictionary<int, IoTStatusLog>();
            if (deviceIds.Count > 0)
            {
                var freshTelemetry = await _db.IoTStatusLogs
                    .AsNoTracking()
                    .Where(log => deviceIds.Contains(log.IoTDeviceId) &&
                                  log.LoggedAt >= freshSince &&
                                  log.Topic != null &&
                                  expectedTopics.Contains(log.Topic))
                    .OrderByDescending(log => log.LoggedAt)
                    .ToListAsync(ct);

                latestByDevice = freshTelemetry
                    .GroupBy(log => log.IoTDeviceId)
                    .ToDictionary(g => g.Key, g => g.First());
            }

            var casesDetectedOrUpdated = 0;
            var warningsDispatched = 0;
            var penaltiesDebited = 0;

            foreach (var booking in activeBookings)
            {
                ct.ThrowIfCancellationRequested();

                var bookingStartDate = DateOnly.FromDateTime(booking.StartDate);
                var bookingEndDateExclusive = DateOnly.FromDateTime(booking.EndDate);
                var endBoundaryLocal = new DateTimeOffset(bookingEndDateExclusive.ToDateTime(TimeOnly.MinValue), MalaysiaOffset);
                var endGraceEndsAtUtc = endBoundaryLocal.AddMinutes(config.GracePeriodMinutes).UtcDateTime;

                DateOnly windowDate;
                DateTime scheduledEndAtUtc;
                DateTime graceEndsAtUtc;
                decimal windowHours = 8m;

                // Path A: Entire booking period has expired (Booking.EndDate <= now + grace)
                if (nowUtc >= endGraceEndsAtUtc)
                {
                    windowDate = bookingEndDateExclusive.AddDays(-1);
                    if (windowDate < bookingStartDate) windowDate = bookingStartDate;

                    var lastDayRules = GetAvailabilityRulesForDate(booking.ParkingSpot.ParkingAvailabilities, windowDate).ToList();
                    if (lastDayRules.Count > 0 && !IsAllDayRental(lastDayRules))
                    {
                        var minStart = lastDayRules.Min(r => r.StartTime);
                        var maxEnd = lastDayRules.Max(r => r.EndTime);
                        windowHours = Math.Max(1m, (decimal)(maxEnd.ToTimeSpan() - minStart.ToTimeSpan()).TotalHours);
                        scheduledEndAtUtc = new DateTimeOffset(windowDate.ToDateTime(maxEnd), MalaysiaOffset).UtcDateTime;
                    }
                    else
                    {
                        windowHours = 24m;
                        scheduledEndAtUtc = endBoundaryLocal.UtcDateTime;
                    }

                    graceEndsAtUtc = scheduledEndAtUtc.AddMinutes(config.GracePeriodMinutes);
                    if (nowUtc < graceEndsAtUtc) continue;
                }
                else
                {
                    // Path B: Mid-booking daily availability window overstay (while still checked in)
                    if (localDate < bookingStartDate || localDate >= bookingEndDateExclusive || !booking.CheckedInAt.HasValue)
                        continue;

                    var rules = GetAvailabilityRulesForDate(booking.ParkingSpot.ParkingAvailabilities, localDate).ToList();
                    if (rules.Count == 0 || IsAllDayRental(rules)) continue;

                    var minStart = rules.Min(r => r.StartTime);
                    var maxEnd = rules.Max(r => r.EndTime);
                    windowHours = Math.Max(1m, (decimal)(maxEnd.ToTimeSpan() - minStart.ToTimeSpan()).TotalHours);
                    scheduledEndAtUtc = new DateTimeOffset(localDate.ToDateTime(maxEnd), MalaysiaOffset).UtcDateTime;
                    graceEndsAtUtc = scheduledEndAtUtc.AddMinutes(config.GracePeriodMinutes);
                    if (nowUtc < graceEndsAtUtc) continue;

                    // If telemetry exists and explicitly confirms the bay is locked/empty, do not flag a daily overstay.
                    var device = booking.ParkingSpot.IoTDevice;
                    if (device is not null && latestByDevice.TryGetValue(device.IoTDeviceId, out var telemetry) &&
                        telemetry.LoggedAt >= booking.CheckedInAt.Value &&
                        TryReadTelemetry(telemetry.Message, out var distanceCm, out var bollardPosition))
                    {
                        var vehicleClearlyGone = string.Equals(bollardPosition, "LOCKED", StringComparison.OrdinalIgnoreCase) &&
                                                 distanceCm.HasValue && distanceCm.Value > 50;
                        if (vehicleClearlyGone) continue;
                    }

                    windowDate = localDate;
                }

                var overstayMinutes = Math.Max(1, (int)Math.Ceiling((nowUtc - graceEndsAtUtc).TotalMinutes));
                var tariff = CalculateTariff(
                    booking.RatePerDaySnapshot,
                    windowHours,
                    overstayMinutes,
                    config.OverstayPenaltyMultiplier,
                    config.OverstayMaxDailyPenalty);

                var existing = await _db.OverstayCases
                    .FirstOrDefaultAsync(c => c.BookingId == booking.BookingId && c.WindowDate == windowDate, ct);

                if (existing is not null)
                {
                    if (existing.Status is "Waived" or "Released" or "Resolved")
                    {
                        continue;
                    }

                    existing.OverstayMinutes = overstayMinutes;
                    existing.BillableHours = tariff.BillableHours;
                    existing.BaseHourlyRate = tariff.BaseHourlyRate;
                    existing.PenaltyMultiplier = config.OverstayPenaltyMultiplier;
                    existing.HourlyPenaltyTariff = tariff.HourlyPenaltyTariff;
                    existing.CalculatedPenaltyAmount = tariff.CalculatedPenalty;
                    existing.IoTDeviceId ??= booking.ParkingSpot.IoTDevice?.IoTDeviceId;
                    existing.UpdatedAt = nowUtc;
                }
                else
                {
                    var nextSeq = (await _db.OverstayCases.MaxAsync(c => (int?)c.OverstayCaseId, ct) ?? 0) + 1;
                    existing = new OverstayCase
                    {
                        CaseReference = $"OVS-{localNow:yyyyMMdd}-{nextSeq:D4}",
                        BookingId = booking.BookingId,
                        RenterId = booking.RenterId,
                        ParkingSpotId = booking.ParkingSpotId,
                        IoTDeviceId = booking.ParkingSpot.IoTDevice?.IoTDeviceId,
                        WindowDate = windowDate,
                        ScheduledEndAtUtc = scheduledEndAtUtc,
                        GraceEndsAtUtc = graceEndsAtUtc,
                        DetectedAtUtc = nowUtc,
                        OverstayMinutes = overstayMinutes,
                        BillableHours = tariff.BillableHours,
                        BaseHourlyRate = tariff.BaseHourlyRate,
                        PenaltyMultiplier = config.OverstayPenaltyMultiplier,
                        HourlyPenaltyTariff = tariff.HourlyPenaltyTariff,
                        CalculatedPenaltyAmount = tariff.CalculatedPenalty,
                        DebitedAmount = 0m,
                        WaivedAmount = 0m,
                        Status = "Detected",
                        PaymentStatus = "Unbilled",
                        HardwareLockActive = false,
                        CreatedAt = nowUtc,
                        UpdatedAt = nowUtc
                    };
                    _db.OverstayCases.Add(existing);
                }

                await _db.SaveChangesAsync(ct);
                casesDetectedOrUpdated++;

                if (!existing.PushWarningSentAtUtc.HasValue)
                {
                    var warned = await DispatchWarningInternalAsync(existing, booking, config, nowUtc, forceResend: false, ct);
                    if (warned) warningsDispatched++;
                }

                var remainingUnbilled = existing.CalculatedPenaltyAmount - existing.DebitedAmount - existing.WaivedAmount;
                if (remainingUnbilled > 0m && existing.Status != "Disputed")
                {
                    var debited = await DebitUnbilledPenaltyInternalAsync(existing, booking, config, nowUtc, ct);
                    if (debited) penaltiesDebited++;
                }
            }

            // Auto-resolve cases whose booking exited cleanly and have no remaining due balance
            var openExitedCases = await _db.OverstayCases
                .Include(c => c.Booking)
                .Where(c => (c.Status == "Detected" || c.Status == "WarningSent" || c.Status == "Penalized") &&
                            c.Booking.ActualExitAt != null)
                .ToListAsync(ct);

            var autoResolved = 0;
            foreach (var c in openExitedCases)
            {
                var remainingDue = Math.Max(0m, c.CalculatedPenaltyAmount - c.DebitedAmount - c.WaivedAmount);
                if (remainingDue == 0m)
                {
                    c.DepartedAtUtc ??= c.Booking.ActualExitAt;
                    c.HardwareLockActive = false;
                    c.HardwareLockReleasedAtUtc ??= c.Booking.ActualExitAt;
                    c.Status = "Resolved";
                    c.ResolvedAtUtc = nowUtc;
                    c.ResolutionNotes ??= "Auto-resolved upon confirmed vehicle departure and settled penalty.";
                    c.UpdatedAt = nowUtc;
                    autoResolved++;
                }
            }

            if (autoResolved > 0)
            {
                await _db.SaveChangesAsync(ct);
            }

            _workerTracker.RecordScanSucceeded(casesDetectedOrUpdated, warningsDispatched, penaltiesDebited);

            if (triggeredByAdminId.HasValue)
            {
                await _accessLogService.LogAsync(triggeredByAdminId, "RunOverstayEnforcementScan", true,
                    $"Scanned={activeBookings.Count}; Cases={casesDetectedOrUpdated}; Warnings={warningsDispatched}; Debited={penaltiesDebited}; Resolved={autoResolved}");
            }

            return new OverstayScanResultDTO
            {
                ActiveBookingsScanned = activeBookings.Count,
                CasesDetectedOrUpdated = casesDetectedOrUpdated,
                WarningsDispatched = warningsDispatched,
                PenaltiesDebited = penaltiesDebited,
                CasesAutoResolved = autoResolved,
                ScannedAtUtc = nowUtc
            };
        }
        catch (Exception ex)
        {
            _workerTracker.RecordScanFailed(ex.Message);
            _logger.LogError(ex, "Overstay enforcement scan failed.");
            throw;
        }
    }

    public async Task<OverstayCaseDTO> SendWarningAsync(int caseId, int? adminUserId, bool forceResend, CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var config = await GetPolicyAsync(ct);
        var overstay = await LoadCaseWithDetailsAsync(caseId, ct)
            ?? throw new KeyNotFoundException($"Overstay case #{caseId} was not found.");

        await DispatchWarningInternalAsync(overstay, overstay.Booking, config, nowUtc, forceResend, ct);
        await _accessLogService.LogAsync(adminUserId, "AdminDispatchOverstayWarning", true,
            $"Case={overstay.CaseReference}; BookingId={overstay.BookingId}; SmsStatus={overstay.SmsDeliveryStatus ?? "n/a"}",
            overstay.BookingId, overstay.IoTDeviceId);

        return MapDto((await LoadCaseWithDetailsAsync(caseId, ct))!);
    }

    public async Task<OverstayCaseDTO> ChargePenaltyAsync(int caseId, int? adminUserId, CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var config = await GetPolicyAsync(ct);
        var overstay = await LoadCaseWithDetailsAsync(caseId, ct)
            ?? throw new KeyNotFoundException($"Overstay case #{caseId} was not found.");

        if (overstay.Status is "Waived" or "Resolved")
            throw new InvalidOperationException($"Case {overstay.CaseReference} is already {overstay.Status}.");

        var remaining = overstay.CalculatedPenaltyAmount - overstay.DebitedAmount - overstay.WaivedAmount;
        if (remaining <= 0m)
            throw new InvalidOperationException($"Case {overstay.CaseReference} has no unbilled penalty remaining.");

        await DebitUnbilledPenaltyInternalAsync(overstay, overstay.Booking, config, nowUtc, ct);
        await _accessLogService.LogAsync(adminUserId, "AdminChargeOverstayPenalty", true,
            $"Case={overstay.CaseReference}; PaymentStatus={overstay.PaymentStatus}; Debited=RM {overstay.DebitedAmount:F2}",
            overstay.BookingId, overstay.IoTDeviceId);

        return MapDto((await LoadCaseWithDetailsAsync(caseId, ct))!);
    }

    public async Task<OverstayCaseDTO> WaivePenaltyAsync(
        int caseId,
        OverstayWaiveRequest request,
        int? adminUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new InvalidOperationException("A reason is required to waive an overstay penalty.");

        var nowUtc = DateTime.UtcNow;
        var reason = request.Reason.Trim();
        int renterId;
        int bookingId;
        decimal effectiveWaiver;

        await using (var dbTx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var overstay = await _db.OverstayCases
                .Include(c => c.Booking)
                    .ThenInclude(b => b.ParkingSpot)
                .FirstOrDefaultAsync(c => c.OverstayCaseId == caseId, ct)
                ?? throw new KeyNotFoundException($"Overstay case #{caseId} was not found.");

            var maxWaivable = Math.Max(0m, overstay.CalculatedPenaltyAmount - overstay.WaivedAmount);
            if (maxWaivable <= 0m)
                throw new InvalidOperationException($"Case {overstay.CaseReference} has already been fully waived.");

            effectiveWaiver = request.WaiverAmount.HasValue
                ? Math.Min(maxWaivable, Math.Round(request.WaiverAmount.Value, 2, MidpointRounding.AwayFromZero))
                : maxWaivable;

            var uncollectedPortion = Math.Max(0m, overstay.CalculatedPenaltyAmount - overstay.DebitedAmount - overstay.WaivedAmount);
            var refundToRenter = Math.Max(0m, effectiveWaiver - uncollectedPortion);

            if (refundToRenter > 0m && overstay.DebitedAmount > 0m)
            {
                refundToRenter = Math.Min(refundToRenter, overstay.DebitedAmount);
                var renterWallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == overstay.RenterId, ct);
                if (renterWallet is not null)
                {
                    _walletService.Refund(renterWallet, refundToRenter, nowUtc);
                    _db.Transactions.Add(new Transaction
                    {
                        WalletId = renterWallet.WalletId,
                        BookingId = overstay.BookingId,
                        TransactionType = TransactionType.Refund,
                        Amount = refundToRenter,
                        PaymentMethod = PaymentMethod.Wallet,
                        TransactionStatus = TransactionStatus.Completed,
                        ReferenceNumber = $"WAIVE-{overstay.CaseReference}",
                        CreatedAt = nowUtc,
                        UpdatedAt = nowUtc
                    });
                }

                var ownerWallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == overstay.Booking.ParkingSpot.OwnerId, ct);
                if (ownerWallet is not null && ownerWallet.Balance >= refundToRenter)
                {
                    _walletService.Deduct(ownerWallet, refundToRenter, nowUtc);
                    _db.Transactions.Add(new Transaction
                    {
                        WalletId = ownerWallet.WalletId,
                        BookingId = overstay.BookingId,
                        TransactionType = TransactionType.Refund,
                        Amount = -refundToRenter,
                        PaymentMethod = PaymentMethod.Wallet,
                        TransactionStatus = TransactionStatus.Completed,
                        ReferenceNumber = $"WAIVE-OWN-{overstay.CaseReference}",
                        CreatedAt = nowUtc,
                        UpdatedAt = nowUtc
                    });
                }

                overstay.DebitedAmount -= refundToRenter;
                overstay.Booking.OverstayPenaltyAmount = Math.Max(0m, overstay.Booking.OverstayPenaltyAmount - refundToRenter);
                overstay.Booking.UpdatedAt = nowUtc;
            }

            overstay.WaivedAmount += effectiveWaiver;
            var newRemainingDue = Math.Max(0m, overstay.CalculatedPenaltyAmount - overstay.DebitedAmount - overstay.WaivedAmount);

            var pendingRef = $"OV-{overstay.BookingId}-{overstay.WindowDate:yyyyMMdd}";
            var pendingTx = await _db.Transactions
                .Where(t => t.BookingId == overstay.BookingId &&
                            t.TransactionType == TransactionType.OverstayPenalty &&
                            t.TransactionStatus == TransactionStatus.Pending &&
                            t.Amount < 0 &&
                            t.ReferenceNumber == pendingRef)
                .ToListAsync(ct);

            foreach (var ptx in pendingTx)
            {
                if (newRemainingDue == 0m)
                {
                    ptx.TransactionStatus = TransactionStatus.Refunded;
                    ptx.Amount = 0m;
                    ptx.UpdatedAt = nowUtc;
                }
                else
                {
                    ptx.Amount = -newRemainingDue;
                    ptx.UpdatedAt = nowUtc;
                }
            }

            if (newRemainingDue == 0m)
            {
                overstay.HardwareLockActive = false;
                overstay.HardwareLockReleasedAtUtc ??= nowUtc;
                overstay.HardwareLockReleasedByUserId ??= adminUserId;
                overstay.ResolvedByUserId = adminUserId;
                overstay.ResolvedAtUtc = nowUtc;
                overstay.PaymentStatus = refundToRenter > 0m
                    ? (overstay.DebitedAmount > 0m ? "Paid" : "Refunded")
                    : (overstay.DebitedAmount > 0m ? "Paid" : "Waived");
                overstay.Status = overstay.DebitedAmount == 0m ? "Waived" : "Resolved";
            }

            overstay.ResolutionNotes = string.IsNullOrWhiteSpace(overstay.ResolutionNotes)
                ? $"Waiver RM {effectiveWaiver:F2}: {reason}"
                : $"{overstay.ResolutionNotes} | Waiver RM {effectiveWaiver:F2}: {reason}";
            overstay.UpdatedAt = nowUtc;

            renterId = overstay.RenterId;
            bookingId = overstay.BookingId;

            await _db.SaveChangesAsync(ct);
            await dbTx.CommitAsync(ct);
        }

        await _accessLogService.LogAsync(adminUserId, "AdminWaiveOverstayPenalty", true,
            $"CaseId={caseId}; Waived=RM {effectiveWaiver:F2}; Reason={reason}", bookingId);

        try
        {
            await _notifications.PublishAsync(new NotificationPublishRequest(
                renterId,
                "booking.overstay_waived",
                new Dictionary<string, object?>
                {
                    ["bookingId"] = bookingId,
                    ["waivedAmount"] = $"RM {effectiveWaiver:F2}",
                    ["reason"] = reason
                },
                entityType: "Booking",
                entityId: bookingId.ToString(),
                dedupeKey: $"booking.overstay_waived:{caseId}:{nowUtc:yyyyMMddHHmmss}"), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish overstay waiver notification for case {CaseId}.", caseId);
        }

        return MapDto((await LoadCaseWithDetailsAsync(caseId, ct))!);
    }

    public async Task<OverstayCaseDTO> ReviewDisputeAsync(
        int caseId,
        OverstayDisputeReviewRequest request,
        int? adminUserId,
        CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var action = request.Action.Trim().ToLowerInvariant();
        var notes = request.Notes.Trim();

        var overstay = await LoadCaseWithDetailsAsync(caseId, ct)
            ?? throw new KeyNotFoundException($"Overstay case #{caseId} was not found.");

        if (!overstay.DisputeInvestigationId.HasValue)
        {
            var linkedDispute = await _db.DisputeInvestigations
                .AsNoTracking()
                .Where(d => d.BookingId == overstay.BookingId)
                .OrderByDescending(d => d.DisputeId)
                .Select(d => (int?)d.DisputeId)
                .FirstOrDefaultAsync(ct);
            overstay.DisputeInvestigationId = linkedDispute;
        }

        switch (action)
        {
            case "flag":
                overstay.Status = "Disputed";
                overstay.ResolutionNotes = $"Dispute opened: {notes}";
                overstay.UpdatedAt = nowUtc;
                await _db.SaveChangesAsync(ct);
                break;

            case "uphold":
                overstay.Status = overstay.PaymentStatus == "Paid" ? "Resolved" : "Penalized";
                overstay.ResolutionNotes = $"Dispute upheld: {notes}";
                overstay.UpdatedAt = nowUtc;
                await _db.SaveChangesAsync(ct);
                break;

            case "partial_waive":
            case "waive":
                await _db.SaveChangesAsync(ct);
                return await WaivePenaltyAsync(
                    caseId,
                    new OverstayWaiveRequest
                    {
                        WaiverAmount = action == "waive" ? null : request.WaiverAmount,
                        Reason = $"Dispute review ({action}): {notes}"
                    },
                    adminUserId,
                    ct);

            default:
                throw new InvalidOperationException("Action must be one of: flag, uphold, partial_waive, waive.");
        }

        await _accessLogService.LogAsync(adminUserId, "AdminReviewOverstayDispute", true,
            $"Case={overstay.CaseReference}; Action={action}; Notes={notes}",
            overstay.BookingId, overstay.IoTDeviceId);

        return MapDto((await LoadCaseWithDetailsAsync(caseId, ct))!);
    }

    public async Task<OverstaySecurityReleaseResultDTO> SecurityReleaseAsync(
        int caseId,
        OverstaySecurityReleaseRequest request,
        int? adminUserId,
        CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var reason = request.Reason.Trim();

        var overstay = await LoadCaseWithDetailsAsync(caseId, ct)
            ?? throw new KeyNotFoundException($"Overstay case #{caseId} was not found.");

        bool bollardAttempted = false;
        bool bollardSucceeded = false;
        string? bollardOutcome = null;

        if (request.TriggerBollardUnlock &&
            overstay.IoTDevice is { IsAssigned: true } device &&
            !string.IsNullOrWhiteSpace(device.Esp32Serial))
        {
            bollardAttempted = true;
            try
            {
                var ack = await _mqttService.PublishCommandAndWaitForAckAsync(
                    device.Esp32Serial,
                    overstay.BookingId,
                    "UNLOCK",
                    ct);

                bollardSucceeded = ack.WaitStatus == MqttCommandWaitStatus.Acknowledged &&
                                   string.Equals(ack.Result, "SUCCESS", StringComparison.OrdinalIgnoreCase);
                bollardOutcome = ack.Message ?? ack.ErrorCode ?? ack.Result ?? ack.WaitStatus.ToString();
            }
            catch (Exception ex)
            {
                bollardOutcome = $"MQTT unavailable ({ex.Message})";
            }
        }
        else if (request.TriggerBollardUnlock)
        {
            bollardOutcome = "No assigned ESP32 bollard found for this parking bay.";
        }

        overstay.HardwareLockActive = false;
        overstay.HardwareLockReleasedAtUtc = nowUtc;
        overstay.HardwareLockReleasedByUserId = adminUserId;
        overstay.Status = request.MarkResolved ? "Resolved" : "Released";
        overstay.ResolvedByUserId = adminUserId;
        overstay.ResolvedAtUtc = nowUtc;
        overstay.ResolutionNotes = string.IsNullOrWhiteSpace(overstay.ResolutionNotes)
            ? $"Security release: {reason}"
            : $"{overstay.ResolutionNotes} | Security release: {reason}";
        overstay.UpdatedAt = nowUtc;

        if (overstay.IoTDevice is not null)
        {
            IoTDeviceLogWriter.Add(_db, overstay.IoTDevice, "overstay_security_release",
                bollardAttempted ? (bollardSucceeded ? "unlocked" : "software_release_only") : "released",
                nowUtc, overstay.BookingId,
                $"case={overstay.CaseReference}; reason={reason}; bollard={bollardOutcome ?? "not_requested"}");
        }

        await _db.SaveChangesAsync(ct);

        await _accessLogService.LogAsync(adminUserId, "AdminOverstaySecurityRelease", true,
            $"Case={overstay.CaseReference}; Reason={reason}; BollardAttempted={bollardAttempted}; BollardSucceeded={bollardSucceeded}",
            overstay.BookingId, overstay.IoTDeviceId);

        try
        {
            await _notifications.PublishAsync(new NotificationPublishRequest(
                overstay.RenterId,
                "booking.overstay_released",
                new Dictionary<string, object?>
                {
                    ["bookingId"] = overstay.BookingId,
                    ["spotName"] = overstay.ParkingSpot.ParkingLabel ?? $"Bay #{overstay.ParkingSpotId}",
                    ["reason"] = reason
                },
                entityType: "Booking",
                entityId: overstay.BookingId.ToString(),
                dedupeKey: $"booking.overstay_released:{overstay.OverstayCaseId}:{nowUtc:yyyyMMddHHmmss}"), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish overstay security release notification for case {CaseId}.", caseId);
        }

        return new OverstaySecurityReleaseResultDTO
        {
            Case = MapDto((await LoadCaseWithDetailsAsync(caseId, ct))!),
            BollardUnlockAttempted = bollardAttempted,
            BollardUnlockSucceeded = bollardSucceeded,
            BollardOutcome = bollardOutcome
        };
    }

    public async Task<SystemConfigDTO> UpdatePolicyAsync(
        UpdateOverstayPolicyRequest request,
        int? adminUserId,
        CancellationToken ct = default)
    {
        var config = await _db.SystemConfigurations
            .Include(c => c.UpdatedByUser)
            .FirstOrDefaultAsync(c => c.Id == 1, ct);

        if (config is null)
        {
            config = new SystemConfiguration
            {
                Id = 1,
                CommissionRate = 10.00m,
                GracePeriodMinutes = 15,
                OverstayPenaltyMultiplier = 1.50m,
                OverstayMaxDailyPenalty = 30.00m,
                HardwareExitLockEnabled = true,
                TaxRatePercent = 8.00m
            };
            _db.SystemConfigurations.Add(config);
        }

        if (request.GracePeriodMinutes.HasValue)
            config.GracePeriodMinutes = request.GracePeriodMinutes.Value;

        if (request.OverstayPenaltyMultiplier.HasValue)
            config.OverstayPenaltyMultiplier = Math.Round(request.OverstayPenaltyMultiplier.Value, 2);

        if (request.OverstayMaxDailyPenalty.HasValue)
            config.OverstayMaxDailyPenalty = Math.Round(request.OverstayMaxDailyPenalty.Value, 2);

        if (request.HardwareExitLockEnabled.HasValue)
            config.HardwareExitLockEnabled = request.HardwareExitLockEnabled.Value;

        config.UpdatedAt = DateTime.UtcNow;
        config.UpdatedByUserId = adminUserId;

        await _db.SaveChangesAsync(ct);
        await _db.Entry(config).Reference(c => c.UpdatedByUser).LoadAsync(ct);

        await _accessLogService.LogAsync(adminUserId, "UpdateOverstayPolicy", true,
            $"Grace={config.GracePeriodMinutes}m; Multiplier={config.OverstayPenaltyMultiplier}x; MaxDaily=RM {config.OverstayMaxDailyPenalty:F2}; ExitLock={config.HardwareExitLockEnabled}");

        return new SystemConfigDTO
        {
            CommissionRate = config.CommissionRate,
            GracePeriodMinutes = config.GracePeriodMinutes,
            OverstayPenaltyMultiplier = config.OverstayPenaltyMultiplier,
            OverstayMaxDailyPenalty = config.OverstayMaxDailyPenalty,
            HardwareExitLockEnabled = config.HardwareExitLockEnabled,
            TaxRatePercent = config.TaxRatePercent,
            UpdatedAt = config.UpdatedAt,
            UpdatedByUserId = config.UpdatedByUserId,
            UpdatedByName = config.UpdatedByUser is not null
                ? $"{config.UpdatedByUser.FirstName} {config.UpdatedByUser.LastName}".Trim()
                : null
        };
    }

    private async Task<bool> DispatchWarningInternalAsync(
        OverstayCase overstay,
        Booking booking,
        SystemConfiguration config,
        DateTime nowUtc,
        bool forceResend,
        CancellationToken ct)
    {
        var spotName = booking.ParkingSpot.ParkingLabel ?? $"Bay #{booking.ParkingSpotId}";
        var windowEndMyt = new DateTimeOffset(overstay.ScheduledEndAtUtc, TimeSpan.Zero)
            .ToOffset(MalaysiaOffset)
            .ToString("HH:mm", CultureInfo.InvariantCulture);

        var dedupeKey = forceResend
            ? $"booking.overstay_warning:{booking.BookingId}:{overstay.WindowDate:yyyyMMdd}:manual:{nowUtc:yyyyMMddHHmmss}"
            : $"booking.overstay_warning:{booking.BookingId}:{overstay.WindowDate:yyyyMMdd}";

        var sentAny = false;
        try
        {
            var notification = await _notifications.PublishAsync(new NotificationPublishRequest(
                booking.RenterId,
                "booking.overstay_warning",
                new Dictionary<string, object?>
                {
                    ["bookingId"] = booking.BookingId,
                    ["spotName"] = spotName,
                    ["windowEnd"] = windowEndMyt,
                    ["windowDate"] = overstay.WindowDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["graceMinutes"] = config.GracePeriodMinutes,
                    ["hourlyTariff"] = $"RM {overstay.HourlyPenaltyTariff:F2}",
                    ["maxPenalty"] = $"RM {config.OverstayMaxDailyPenalty:F2}"
                },
                entityType: "Booking",
                entityId: booking.BookingId.ToString(),
                dedupeKey: dedupeKey), ct);

            if (notification is not null)
            {
                overstay.PushWarningSentAtUtc ??= nowUtc;
                sentAny = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send push/email overstay warning for booking {BookingId}.", booking.BookingId);
        }

        if (!string.IsNullOrWhiteSpace(booking.Renter?.PhoneNumber) &&
            (forceResend || !overstay.SmsWarningSentAtUtc.HasValue))
        {
            var smsText =
                $"ParkJom Alert: Booking {booking.BookingReference} at {spotName} is in overstay ({overstay.OverstayMinutes}m past grace). " +
                $"Tariff RM {overstay.HourlyPenaltyTariff:F2}/hr applies. Please vacate immediately.";
            var smsResult = await _sms360Service.SendMessageAsync(booking.Renter.PhoneNumber, smsText, ct);
            overstay.SmsWarningSentAtUtc = nowUtc;
            overstay.SmsProviderRef = smsResult.ProviderMessageId;
            overstay.SmsDeliveryStatus = smsResult.Success
                ? "Sent"
                : $"Failed ({smsResult.ErrorCode ?? "ERROR"})";
            if (smsResult.Success) sentAny = true;
        }
        else if (string.IsNullOrWhiteSpace(overstay.SmsDeliveryStatus))
        {
            overstay.SmsDeliveryStatus = "NoPhoneNumber";
        }

        if (overstay.Status == "Detected" && overstay.PushWarningSentAtUtc.HasValue)
        {
            overstay.Status = "WarningSent";
        }

        overstay.UpdatedAt = nowUtc;
        if (booking.ParkingSpot.IoTDevice is not null)
        {
            IoTDeviceLogWriter.Add(_db, booking.ParkingSpot.IoTDevice, "overstay_warning_sent",
                sentAny ? "success" : "recorded", nowUtc, booking.BookingId,
                $"case={overstay.CaseReference}; windowDate={overstay.WindowDate:yyyy-MM-dd}; sms={overstay.SmsDeliveryStatus}");
        }

        await _db.SaveChangesAsync(ct);
        return sentAny;
    }

    private async Task<bool> DebitUnbilledPenaltyInternalAsync(
        OverstayCase overstay,
        Booking booking,
        SystemConfiguration config,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var delta = Math.Round(overstay.CalculatedPenaltyAmount - overstay.DebitedAmount - overstay.WaivedAmount, 2, MidpointRounding.AwayFromZero);
        if (delta <= 0m) return false;

        var reference = $"OV-{booking.BookingId}-{overstay.WindowDate:yyyyMMdd}";
        var debitedNow = false;
        var insufficientFundsNotified = false;

        await using (var dbTx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var renterWallet = await _db.Wallets
                .FromSqlInterpolated($"SELECT * FROM [Wallets] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = {booking.RenterId} AND [Status] = {nameof(WalletStatus.Active)}")
                .FirstOrDefaultAsync(ct);

            var ownerWallet = await _db.Wallets
                .FromSqlInterpolated($"SELECT * FROM [Wallets] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = {booking.ParkingSpot.OwnerId} AND [Status] = {nameof(WalletStatus.Active)}")
                .FirstOrDefaultAsync(ct);

            var pendingTx = await _db.Transactions
                .FirstOrDefaultAsync(t =>
                    t.BookingId == booking.BookingId &&
                    t.TransactionType == TransactionType.OverstayPenalty &&
                    t.TransactionStatus == TransactionStatus.Pending &&
                    t.Amount < 0 &&
                    t.ReferenceNumber == reference, ct);

            if (renterWallet is not null && ownerWallet is not null && renterWallet.Balance >= delta)
            {
                _walletService.Deduct(renterWallet, delta, nowUtc);
                ownerWallet.Balance += delta;
                ownerWallet.UpdatedAt = nowUtc;

                Transaction debitTx;
                if (pendingTx is not null)
                {
                    pendingTx.Amount = -(overstay.DebitedAmount + delta);
                    pendingTx.TransactionStatus = TransactionStatus.Completed;
                    pendingTx.UpdatedAt = nowUtc;
                    debitTx = pendingTx;
                }
                else
                {
                    var txRef = overstay.DebitedAmount == 0m
                        ? reference
                        : $"{reference}-H{overstay.BillableHours}";
                    debitTx = new Transaction
                    {
                        WalletId = renterWallet.WalletId,
                        BookingId = booking.BookingId,
                        TransactionType = TransactionType.OverstayPenalty,
                        Amount = -delta,
                        PaymentMethod = PaymentMethod.Wallet,
                        TransactionStatus = TransactionStatus.Completed,
                        ReferenceNumber = txRef,
                        CreatedAt = nowUtc,
                        UpdatedAt = nowUtc
                    };
                    _db.Transactions.Add(debitTx);
                }

                _db.Transactions.Add(new Transaction
                {
                    WalletId = ownerWallet.WalletId,
                    BookingId = booking.BookingId,
                    TransactionType = TransactionType.OverstayPenalty,
                    Amount = delta,
                    PaymentMethod = PaymentMethod.Wallet,
                    TransactionStatus = TransactionStatus.Completed,
                    ReferenceNumber = overstay.DebitedAmount == 0m ? reference : $"{reference}-H{overstay.BillableHours}",
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                });

                await _db.SaveChangesAsync(ct);

                overstay.DebitedAmount += delta;
                overstay.PaymentStatus = "Paid";
                overstay.Status = "Penalized";
                overstay.LedgerDebitedAtUtc = nowUtc;
                overstay.PenaltyTransactionId = debitTx.TransactionId;
                overstay.HardwareLockActive = false;
                overstay.HardwareLockReleasedAtUtc ??= overstay.HardwareLockEngagedAtUtc.HasValue ? nowUtc : null;

                booking.OverstayHours = Math.Max(booking.OverstayHours, overstay.BillableHours);
                booking.OverstayPenaltyAmount += delta;
                booking.UpdatedAt = nowUtc;

                if (booking.ParkingSpot.IoTDevice is not null)
                {
                    IoTDeviceLogWriter.Add(_db, booking.ParkingSpot.IoTDevice, "overstay_penalty_assessed", "paid", nowUtc,
                        booking.BookingId, $"case={overstay.CaseReference}; hours={overstay.BillableHours}; debited=RM {delta:F2}");
                }

                await _db.SaveChangesAsync(ct);
                await dbTx.CommitAsync(ct);
                debitedNow = true;
            }
            else
            {
                var fallbackWallet = renterWallet ?? await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == booking.RenterId, ct);
                if (fallbackWallet is not null)
                {
                    if (pendingTx is not null)
                    {
                        pendingTx.Amount = -delta;
                        pendingTx.UpdatedAt = nowUtc;
                    }
                    else
                    {
                        pendingTx = new Transaction
                        {
                            WalletId = fallbackWallet.WalletId,
                            BookingId = booking.BookingId,
                            TransactionType = TransactionType.OverstayPenalty,
                            Amount = -delta,
                            PaymentMethod = PaymentMethod.Wallet,
                            TransactionStatus = TransactionStatus.Pending,
                            ReferenceNumber = reference,
                            CreatedAt = nowUtc,
                            UpdatedAt = nowUtc
                        };
                        _db.Transactions.Add(pendingTx);
                    }
                    await _db.SaveChangesAsync(ct);
                    overstay.PenaltyTransactionId = pendingTx.TransactionId;
                }

                overstay.PaymentStatus = "PaymentDue";
                overstay.Status = "Penalized";
                overstay.HardwareLockActive = config.HardwareExitLockEnabled;
                overstay.HardwareLockEngagedAtUtc ??= config.HardwareExitLockEnabled ? nowUtc : null;
                overstay.UpdatedAt = nowUtc;

                booking.OverstayHours = Math.Max(booking.OverstayHours, overstay.BillableHours);
                booking.OverstayPenaltyAmount = Math.Max(booking.OverstayPenaltyAmount, overstay.CalculatedPenaltyAmount);
                booking.UpdatedAt = nowUtc;

                if (booking.ParkingSpot.IoTDevice is not null)
                {
                    IoTDeviceLogWriter.Add(_db, booking.ParkingSpot.IoTDevice, "overstay_penalty_assessed", "payment_due", nowUtc,
                        booking.BookingId, $"case={overstay.CaseReference}; due=RM {delta:F2}; exitLock={overstay.HardwareLockActive}");
                }

                await _db.SaveChangesAsync(ct);
                await dbTx.CommitAsync(ct);
                insufficientFundsNotified = true;
            }
        }

        try
        {
            if (debitedNow)
            {
                await _notifications.PublishAsync(new NotificationPublishRequest(
                    booking.RenterId,
                    "booking.overstay_charged",
                    new Dictionary<string, object?>
                    {
                        ["bookingId"] = booking.BookingId,
                        ["penaltyAmount"] = $"RM {delta:F2}",
                        ["overstayHours"] = overstay.BillableHours,
                        ["windowDate"] = overstay.WindowDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    },
                    entityType: "Booking",
                    entityId: booking.BookingId.ToString(),
                    dedupeKey: $"booking.overstay_charged:{overstay.OverstayCaseId}:h{overstay.BillableHours}"), ct);
            }
            else if (insufficientFundsNotified)
            {
                await _notifications.PublishAsync(new NotificationPublishRequest(
                    booking.RenterId,
                    "booking.overstay_payment_due",
                    new Dictionary<string, object?>
                    {
                        ["bookingId"] = booking.BookingId,
                        ["penaltyAmount"] = $"RM {delta:F2}",
                        ["overstayHours"] = overstay.BillableHours,
                        ["windowDate"] = overstay.WindowDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    },
                    entityType: "Booking",
                    entityId: booking.BookingId.ToString(),
                    dedupeKey: $"booking.overstay_due:{overstay.OverstayCaseId}:{overstay.WindowDate:yyyyMMdd}"), ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish overstay billing notification for case {CaseRef}.", overstay.CaseReference);
        }

        return debitedNow;
    }

    private Task<OverstayCase?> LoadCaseWithDetailsAsync(int caseId, CancellationToken ct)
        => _db.OverstayCases
            .Include(c => c.Booking)
                .ThenInclude(b => b.Vehicle)
            .Include(c => c.Booking)
                .ThenInclude(b => b.ParkingSpot)
                    .ThenInclude(s => s.Property)
            .Include(c => c.Booking)
                .ThenInclude(b => b.ParkingSpot)
                    .ThenInclude(s => s.IoTDevice)
            .Include(c => c.Renter)
            .Include(c => c.ParkingSpot)
                .ThenInclude(s => s.Property)
            .Include(c => c.IoTDevice)
            .FirstOrDefaultAsync(c => c.OverstayCaseId == caseId, ct);

    private async Task<SystemConfiguration> GetPolicyAsync(CancellationToken ct)
        => await _db.SystemConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == 1, ct)
           ?? new SystemConfiguration
           {
               Id = 1,
               CommissionRate = 10.00m,
               GracePeriodMinutes = 15,
               OverstayPenaltyMultiplier = 1.50m,
               OverstayMaxDailyPenalty = 30.00m,
               HardwareExitLockEnabled = true,
               TaxRatePercent = 8.00m
           };

    private static OverstayCaseDTO MapDto(OverstayCase c)
    {
        var remainingDue = Math.Max(0m, c.CalculatedPenaltyAmount - c.DebitedAmount - c.WaivedAmount);
        var renterName = $"{c.Renter.FirstName} {c.Renter.LastName}".Trim();
        return new OverstayCaseDTO
        {
            OverstayCaseId = c.OverstayCaseId,
            CaseReference = c.CaseReference,
            BookingId = c.BookingId,
            BookingReference = c.Booking.BookingReference,
            RenterId = c.RenterId,
            RenterName = string.IsNullOrEmpty(renterName) ? c.Renter.Email : renterName,
            RenterEmail = c.Renter.Email,
            RenterPhone = c.Renter.PhoneNumber,
            VehiclePlate = c.Booking.Vehicle?.NumberPlate ?? "—",
            ParkingSpotId = c.ParkingSpotId,
            SpotLabel = c.ParkingSpot.ParkingLabel ?? $"Bay #{c.ParkingSpotId}",
            PropertyName = c.ParkingSpot.Property?.PropertyName ?? "—",
            IoTDeviceId = c.IoTDeviceId,
            Esp32Serial = c.IoTDevice?.Esp32Serial,
            DeviceStatus = c.IoTDevice?.DeviceStatus.ToString(),
            WindowDate = c.WindowDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ScheduledEndAtUtc = c.ScheduledEndAtUtc,
            GraceEndsAtUtc = c.GraceEndsAtUtc,
            DetectedAtUtc = c.DetectedAtUtc,
            DepartedAtUtc = c.DepartedAtUtc,
            OverstayMinutes = c.OverstayMinutes,
            BillableHours = c.BillableHours,
            BaseHourlyRate = c.BaseHourlyRate,
            PenaltyMultiplier = c.PenaltyMultiplier,
            HourlyPenaltyTariff = c.HourlyPenaltyTariff,
            CalculatedPenaltyAmount = c.CalculatedPenaltyAmount,
            DebitedAmount = c.DebitedAmount,
            WaivedAmount = c.WaivedAmount,
            RemainingDueAmount = remainingDue,
            Status = c.Status,
            PaymentStatus = c.PaymentStatus,
            PushWarningSentAtUtc = c.PushWarningSentAtUtc,
            SmsWarningSentAtUtc = c.SmsWarningSentAtUtc,
            SmsProviderRef = c.SmsProviderRef,
            SmsDeliveryStatus = c.SmsDeliveryStatus,
            LedgerDebitedAtUtc = c.LedgerDebitedAtUtc,
            PenaltyTransactionId = c.PenaltyTransactionId,
            HardwareLockActive = c.HardwareLockActive,
            HardwareLockEngagedAtUtc = c.HardwareLockEngagedAtUtc,
            HardwareLockReleasedAtUtc = c.HardwareLockReleasedAtUtc,
            HardwareLockReleasedByUserId = c.HardwareLockReleasedByUserId,
            DisputeInvestigationId = c.DisputeInvestigationId,
            ResolutionNotes = c.ResolutionNotes,
            ResolvedByUserId = c.ResolvedByUserId,
            ResolvedAtUtc = c.ResolvedAtUtc,
            UpdatedAt = c.UpdatedAt
        };
    }

    private static bool TryReadTelemetry(string? payload, out double? distanceCm, out string? bollardPosition)
    {
        distanceCm = null;
        bollardPosition = null;
        if (string.IsNullOrWhiteSpace(payload)) return false;

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.TryGetProperty("distanceCm", out var distanceElement) &&
                distanceElement.ValueKind == JsonValueKind.Number &&
                distanceElement.TryGetDouble(out var parsedDistance) &&
                double.IsFinite(parsedDistance))
            {
                distanceCm = parsedDistance;
            }

            if (root.TryGetProperty("bollardPosition", out var positionElement) &&
                positionElement.ValueKind == JsonValueKind.String)
            {
                bollardPosition = positionElement.GetString();
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IEnumerable<Availability> GetAvailabilityRulesForDate(IEnumerable<Availability> rules, DateOnly date)
        => rules.Where(rule =>
            (!rule.EffectiveFrom.HasValue || date >= rule.EffectiveFrom.Value) &&
            (!rule.EffectiveUntil.HasValue || date <= rule.EffectiveUntil.Value) &&
            (rule.DayType == DayType.Everyday ||
             (rule.DayType == DayType.Weekday && date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) ||
             (rule.DayType == DayType.Weekend && date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)));

    private static bool IsAllDayRental(IEnumerable<Availability> rules)
        => rules.Any(rule => rule.DayType == DayType.Everyday &&
                             rule.StartTime == TimeOnly.MinValue &&
                             rule.EndTime == new TimeOnly(23, 59));
}
