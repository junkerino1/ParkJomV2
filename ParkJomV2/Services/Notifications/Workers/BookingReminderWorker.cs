using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Services.Notifications.Workers;

public class BookingReminderJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public BookingReminderJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public Task RunOnceAsync(CancellationToken cancellationToken)
        => CheckBookingRemindersAsync(cancellationToken);

    private async Task CheckBookingRemindersAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var now = DateTime.UtcNow;

        // 1. T-60 minute reminders: Confirmed bookings starting between now + 50m and now + 65m
        var window60Start = now.AddMinutes(50);
        var window60End = now.AddMinutes(65);

        var starting60Bookings = await context.Bookings
            .AsNoTracking()
            .Include(b => b.ParkingSpot)
            .Where(b => b.BookingStatus == BookingStatus.Confirmed &&
                        b.StartDate >= window60Start &&
                        b.StartDate <= window60End)
            .ToListAsync(ct);

        foreach (var b in starting60Bookings)
        {
            var dedupe = $"booking.starting_soon:{b.BookingId}:60";
            await notificationService.PublishAsync(new NotificationPublishRequest(
                userId: b.RenterId,
                type: "booking.starting_soon",
                data: new Dictionary<string, object?>
                {
                    ["bookingId"] = b.BookingId,
                    ["bookingReference"] = b.BookingReference,
                    ["spotName"] = b.ParkingSpot?.ParkingLabel ?? $"Bay #{b.ParkingSpotId}",
                    ["minutesBefore"] = 60
                },
                entityType: "Booking",
                entityId: b.BookingId.ToString(),
                dedupeKey: dedupe,
                priorityOverride: NotificationPriority.High
            ), ct);
        }

        // 2. T-15 minute reminders: Confirmed bookings starting between now + 10m and now + 20m
        var window15Start = now.AddMinutes(10);
        var window15End = now.AddMinutes(20);

        var starting15Bookings = await context.Bookings
            .AsNoTracking()
            .Include(b => b.ParkingSpot)
            .Where(b => b.BookingStatus == BookingStatus.Confirmed &&
                        b.StartDate >= window15Start &&
                        b.StartDate <= window15End)
            .ToListAsync(ct);

        foreach (var b in starting15Bookings)
        {
            var dedupe = $"booking.starting_soon:{b.BookingId}:15";
            await notificationService.PublishAsync(new NotificationPublishRequest(
                userId: b.RenterId,
                type: "booking.starting_soon",
                data: new Dictionary<string, object?>
                {
                    ["bookingId"] = b.BookingId,
                    ["bookingReference"] = b.BookingReference,
                    ["spotName"] = b.ParkingSpot?.ParkingLabel ?? $"Bay #{b.ParkingSpotId}",
                    ["minutesBefore"] = 15
                },
                entityType: "Booking",
                entityId: b.BookingId.ToString(),
                dedupeKey: dedupe,
                priorityOverride: NotificationPriority.High
            ), ct);
        }

        // 3. T-30 minute ending reminders: Active bookings ending between now + 20m and now + 35m
        var windowEndStart = now.AddMinutes(20);
        var windowEndEnd = now.AddMinutes(35);

        var endingBookings = await context.Bookings
            .AsNoTracking()
            .Include(b => b.ParkingSpot)
            .Where(b => b.BookingStatus == BookingStatus.Active &&
                        b.EndDate >= windowEndStart &&
                        b.EndDate <= windowEndEnd)
            .ToListAsync(ct);

        foreach (var b in endingBookings)
        {
            var dedupe = $"booking.ending_soon:{b.BookingId}:30";
            await notificationService.PublishAsync(new NotificationPublishRequest(
                userId: b.RenterId,
                type: "booking.ending_soon",
                data: new Dictionary<string, object?>
                {
                    ["bookingId"] = b.BookingId,
                    ["bookingReference"] = b.BookingReference,
                    ["spotName"] = b.ParkingSpot?.ParkingLabel ?? $"Bay #{b.ParkingSpotId}",
                    ["minutesBefore"] = 30
                },
                entityType: "Booking",
                entityId: b.BookingId.ToString(),
                dedupeKey: dedupe,
                priorityOverride: NotificationPriority.High
            ), ct);
        }
    }
}
