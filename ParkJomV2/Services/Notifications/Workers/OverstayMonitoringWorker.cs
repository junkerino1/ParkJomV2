using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using ParkJomV2.Services.Notifications;

namespace ParkJomV2.Services.Notifications.Workers;

/// <summary>Checks fresh ESP32 telemetry each minute and notifies renters still occupying a spot after its daily grace period.</summary>
public sealed class OverstayMonitoringJob
{
    private static readonly TimeSpan MalaysiaOffset = TimeSpan.FromHours(8);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OverstayMonitoringJob> _logger;

    public OverstayMonitoringJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<OverstayMonitoringJob> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var nowUtc = DateTime.UtcNow;
        var freshnessMinutes = Math.Clamp(
            _configuration.GetValue<int?>("IoT:TelemetryFreshnessMinutes") ?? 10,
            5,
            30);
        var freshSince = nowUtc.AddMinutes(-freshnessMinutes);

        var bookings = await db.Bookings
            .AsNoTracking()
            .Where(b => b.BookingStatus == BookingStatus.Active &&
                        b.CheckedInAt != null &&
                        b.ActualExitAt == null)
            .Include(b => b.ParkingSpot)
                .ThenInclude(spot => spot.IoTDevice)
            .Include(b => b.ParkingSpot)
                .ThenInclude(spot => spot.ParkingAvailabilities)
            .ToListAsync(cancellationToken);

        var eligibleBookings = bookings
            .Where(b => b.CheckedInAt.HasValue &&
                        b.ParkingSpot.IoTDevice is { IsAssigned: true } device &&
                        !string.IsNullOrWhiteSpace(device.Esp32Serial))
            .ToList();
        if (eligibleBookings.Count == 0) return 0;

        var topicRoot = (_configuration["Mqtt:TopicRoot"] ?? "parkjom/devices").TrimEnd('/');
        var expectedTopicByDevice = eligibleBookings
            .Select(b => b.ParkingSpot.IoTDevice!)
            .DistinctBy(device => device.IoTDeviceId)
            .ToDictionary(device => device.IoTDeviceId, device => $"{topicRoot}/{device.Esp32Serial}/telemetry");
        var deviceIds = expectedTopicByDevice.Keys.ToList();
        var expectedTopics = expectedTopicByDevice.Values.ToList();
        var latestTelemetryByDevice = await db.IoTStatusLogs
            .AsNoTracking()
            .Where(log => deviceIds.Contains(log.IoTDeviceId) &&
                          log.LoggedAt >= freshSince &&
                          log.Topic != null && expectedTopics.Contains(log.Topic))
            .OrderByDescending(log => log.LoggedAt)
            .ToListAsync(cancellationToken);
        var latestByDevice = latestTelemetryByDevice
            .GroupBy(log => log.IoTDeviceId)
            .ToDictionary(group => group.Key, group => group.First());

        var warningsSent = 0;
        foreach (var booking in eligibleBookings)
        {
            var device = booking.ParkingSpot.IoTDevice!;
            if (!latestByDevice.TryGetValue(device.IoTDeviceId, out var telemetry) ||
                telemetry.LoggedAt < booking.CheckedInAt!.Value ||
                !TryReadTelemetry(telemetry.Message, out var distanceCm, out var bollardPosition) ||
                distanceCm is null || distanceCm.Value > 50 ||
                !string.Equals(bollardPosition, "UNLOCKED", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var localNow = new DateTimeOffset(nowUtc, TimeSpan.Zero).ToOffset(MalaysiaOffset);
            var localDate = DateOnly.FromDateTime(localNow.DateTime);
            var bookingStartDate = DateOnly.FromDateTime(booking.StartDate);
            var bookingEndDateExclusive = DateOnly.FromDateTime(booking.EndDate);
            if (localDate < bookingStartDate || localDate >= bookingEndDateExclusive) continue;

            var rules = GetAvailabilityRulesForDate(booking.ParkingSpot.ParkingAvailabilities, localDate).ToList();
            if (rules.Count == 0 || IsAllDayRental(rules)) continue;

            var windowEnd = rules.Max(rule => rule.EndTime);
            var windowEndLocal = new DateTimeOffset(localDate.ToDateTime(windowEnd), MalaysiaOffset);
            var graceEndsAtUtc = windowEndLocal.AddMinutes(15).UtcDateTime;
            if (nowUtc < graceEndsAtUtc) continue;

            try
            {
                var notification = await notifications.PublishAsync(new NotificationPublishRequest(
                    booking.RenterId,
                    "booking.overstay_warning",
                    new Dictionary<string, object?>
                    {
                        ["bookingId"] = booking.BookingId,
                        ["spotName"] = booking.ParkingSpot.ParkingLabel ?? $"Bay #{booking.ParkingSpotId}",
                        ["windowEnd"] = windowEnd.ToString("HH:mm"),
                        ["windowDate"] = localDate.ToString("yyyy-MM-dd")
                    },
                    entityType: "Booking",
                    entityId: booking.BookingId.ToString(),
                    dedupeKey: $"booking.overstay_warning:{booking.BookingId}:{localDate:yyyyMMdd}"), cancellationToken);

                if (notification is null) continue;

                IoTDeviceLogWriter.Add(db, device, "overstay_warning_sent", "success", nowUtc, booking.BookingId,
                    $"source=cron; windowDate={localDate:yyyy-MM-dd}; windowEnd={windowEnd:HH\\:mm}; telemetryAtUtc={telemetry.LoggedAt:O}; distanceCm={distanceCm.Value:F1}; graceMinutes=15");
                await db.SaveChangesAsync(cancellationToken);
                warningsSent++;
                _logger.LogInformation("Overstay warning sent for booking {BookingId} using telemetry from {TelemetryAtUtc}.",
                    booking.BookingId, telemetry.LoggedAt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send overstay warning for booking {BookingId}.", booking.BookingId);
                IoTDeviceLogWriter.Add(db, device, "overstay_warning_sent", "failed", nowUtc, booking.BookingId,
                    $"source=cron; windowDate={localDate:yyyy-MM-dd}; {ex.Message}");
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        return warningsSent;
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
