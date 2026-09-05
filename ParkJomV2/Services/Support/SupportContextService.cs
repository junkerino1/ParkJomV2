using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Services.Support;

public class SupportContextService
{
    private readonly ApplicationDbContext _context;

    public SupportContextService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<SupportContextDto> GetUserContextAsync(int userId, int? targetBookingId = null, int? targetVehicleId = null)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Include(u => u.Wallet)
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null)
        {
            return new SupportContextDto();
        }

        var now = DateTime.UtcNow;

        // Active or target booking
        var bookingsQuery = _context.Bookings
            .AsNoTracking()
            .Where(b => b.RenterId == userId)
            .Include(b => b.ParkingSpot)
                .ThenInclude(p => p.Property)
            .Include(b => b.ParkingSpot)
                .ThenInclude(p => p.IoTDevice)
            .Include(b => b.Vehicle);

        var activeBookingEntity = targetBookingId.HasValue
            ? await bookingsQuery.FirstOrDefaultAsync(b => b.BookingId == targetBookingId.Value)
            : await bookingsQuery.OrderByDescending(b => b.StartDate)
                .FirstOrDefaultAsync(b => b.BookingStatus == BookingStatus.Confirmed
                    || b.BookingStatus == BookingStatus.Active
                    || (b.StartDate <= now.AddHours(2) && b.EndDate >= now.AddHours(-2)));

        var recentBookingsEntities = await bookingsQuery
            .OrderByDescending(b => b.CreatedAt)
            .Take(5)
            .ToListAsync();

        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .Where(v => v.UserId == userId)
            .Select(v => new SupportVehicleSummaryDto
            {
                VehicleId = v.VehicleId,
                LicensePlate = v.NumberPlate,
                MakeModel = $"{v.VehicleBrand} {v.VehicleModel}".Trim(),
                Color = v.VehicleColor ?? string.Empty
            })
            .ToListAsync();

        var recentTransactions = await _context.Transactions
            .AsNoTracking()
            .Where(t => t.Wallet != null && t.Wallet.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(5)
            .Select(t => new SupportTransactionSummaryDto
            {
                TransactionId = t.TransactionId,
                ReferenceNumber = t.ReferenceNumber,
                TransactionType = t.TransactionType.ToString(),
                Amount = t.Amount,
                PaymentMethod = t.PaymentMethod.ToString(),
                TransactionStatus = t.TransactionStatus.ToString(),
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();

        var ownedSpots = new List<SupportParkingSpotSummaryDto>();
        if (user.UserType == UserType.PropertyOwner || user.UserType == UserType.Admin)
        {
            ownedSpots = await _context.ParkingSpots
                .AsNoTracking()
                .Where(p => p.OwnerId == userId)
                .Include(p => p.Property)
                .Include(p => p.IoTDevice)
                .Select(p => new SupportParkingSpotSummaryDto
                {
                    ParkingSpotId = p.ParkingSpotId,
                    SpotNumber = p.ParkingLabel ?? $"Spot #{p.ParkingSpotId}",
                    PropertyName = p.Property != null ? p.Property.PropertyName : "Unknown Property",
                    AvailabilityStatus = p.AvailabilityStatus.ToString(),
                    HasIoTDevice = p.IoTDevice != null,
                    IoTStatus = p.IoTDevice != null ? p.IoTDevice.DeviceStatus.ToString() : null
                })
                .ToListAsync();
        }

        var recentLogs = await _context.AccessLogs
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.AccessedAt)
            .Take(5)
            .Select(a => new SupportAccessLogSummaryDto
            {
                AccessLogId = a.AccessLogId,
                Actions = a.Actions,
                AccessedAt = a.AccessedAt,
                BookingId = a.BookingId,
                IoTDeviceId = a.IoTDeviceId
            })
            .ToListAsync();

        return new SupportContextDto
        {
            UserId = user.UserId,
            UserName = $"{user.FirstName} {user.LastName}".Trim(),
            UserEmail = user.Email,
            UserType = user.UserType.ToString(),
            WalletBalance = user.Wallet?.Balance ?? 0m,
            ActiveBooking = activeBookingEntity != null ? MapBookingSummary(activeBookingEntity) : null,
            RecentBookings = recentBookingsEntities.Select(MapBookingSummary).ToList(),
            Vehicles = vehicles,
            RecentTransactions = recentTransactions,
            OwnedSpots = ownedSpots,
            RecentAccessLogs = recentLogs
        };
    }

    private static SupportBookingSummaryDto MapBookingSummary(Booking b)
    {
        var iot = b.ParkingSpot?.IoTDevice;
        return new SupportBookingSummaryDto
        {
            BookingId = b.BookingId,
            BookingReference = b.BookingReference,
            ParkingSpotName = b.ParkingSpot?.ParkingLabel ?? "Unknown Spot",
            PropertyAddress = b.ParkingSpot?.Property?.Address ?? "Unknown Address",
            VehiclePlate = b.Vehicle?.NumberPlate ?? "Unknown Plate",
            StartDate = b.StartDate,
            EndDate = b.EndDate,
            Status = b.BookingStatus.ToString(),
            TotalAmount = b.TotalAmount,
            HasIoTDevice = iot != null,
            IoTDeviceStatus = iot?.DeviceStatus.ToString(),
            LastHeartbeatAt = iot?.LastHeartbeatAt
        };
    }
}
