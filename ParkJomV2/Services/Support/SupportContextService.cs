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

        var activeBookingDto = activeBookingEntity != null ? MapBookingSummary(activeBookingEntity, now) : null;

        return new SupportContextDto
        {
            UserId = user.UserId,
            UserName = $"{user.FirstName} {user.LastName}".Trim(),
            UserEmail = user.Email,
            UserType = user.UserType.ToString(),
            WalletBalance = user.Wallet?.Balance ?? 0m,
            ActiveBooking = activeBookingDto,
            CanSelfUnlockGate = activeBookingDto?.CanSelfUnlockGate ?? false,
            QuickIssueTemplates = GetQuickIssueTemplates(user.UserType),
            RecentBookings = recentBookingsEntities.Select(b => MapBookingSummary(b, now)).ToList(),
            Vehicles = vehicles,
            RecentTransactions = recentTransactions,
            OwnedSpots = ownedSpots,
            RecentAccessLogs = recentLogs
        };
    }

    public static List<QuickIssueTemplateDto> GetQuickIssueTemplates(UserType userType)
    {
        if (userType == UserType.PropertyOwner)
        {
            return new List<QuickIssueTemplateDto>
            {
                new()
                {
                    IssueCode = "RENTER_OVERSTAY",
                    Title = "Renter Overstay / Blocking Spot",
                    Description = "Current booking has ended, but renter vehicle is still parked in the spot blocking subsequent reservations.",
                    Category = "OwnerSupport",
                    DefaultPriority = "P1",
                    EstimatedResponseTime = "Within 15 minutes",
                    RequiresBooking = true,
                    SuggestPhotoUpload = true,
                    SupportsSelfUnlock = false
                },
                new()
                {
                    IssueCode = "IOT_OFFLINE",
                    Title = "Smart Barrier / Hardware Issue",
                    Description = "Barrier gate is unresponsive, sensor offline, or hardware failure requires on-site inspection.",
                    Category = "ParkingAccess",
                    DefaultPriority = "P1",
                    EstimatedResponseTime = "Within 15 minutes",
                    RequiresBooking = false,
                    SuggestPhotoUpload = true,
                    SupportsSelfUnlock = false
                },
                new()
                {
                    IssueCode = "OWNER_PAYOUT",
                    Title = "Host Payout & Revenue Inquiry",
                    Description = "Booking earnings breakdown, commission verification, or wallet payout timeline inquiries.",
                    Category = "OwnerSupport",
                    DefaultPriority = "P2",
                    EstimatedResponseTime = "Within 1 hour",
                    RequiresBooking = false,
                    SuggestPhotoUpload = false,
                    SupportsSelfUnlock = false
                },
                new()
                {
                    IssueCode = "OTHER",
                    Title = "Other Host Inquiries",
                    Description = "Spot verification, calendar scheduling, listing details, or account assistance.",
                    Category = "OwnerSupport",
                    DefaultPriority = "P2",
                    EstimatedResponseTime = "Within 1 hour",
                    RequiresBooking = false,
                    SuggestPhotoUpload = false,
                    SupportsSelfUnlock = false
                }
            };
        }

        return new List<QuickIssueTemplateDto>
        {
            new()
            {
                IssueCode = "GATE_STUCK",
                Title = "Gate Stuck / Cannot Enter or Exit",
                Description = "Barrier failed to open or license plate was not recognized upon arrival. Emergency self-unlock or human agent available.",
                Category = "ParkingAccess",
                DefaultPriority = "P0",
                EstimatedResponseTime = "Immediate self-unlock / 24/7 urgent dispatch",
                RequiresBooking = true,
                SuggestPhotoUpload = false,
                SupportsSelfUnlock = true
            },
            new()
            {
                IssueCode = "SPOT_OCCUPIED",
                Title = "Reserved Spot Occupied",
                Description = "Reserved spot is occupied by an unauthorized vehicle. Attach a photo for immediate relocation or full refund.",
                Category = "ParkingAccess",
                DefaultPriority = "P0",
                EstimatedResponseTime = "Within 5 minutes",
                RequiresBooking = true,
                SuggestPhotoUpload = true,
                SupportsSelfUnlock = false
            },
            new()
            {
                IssueCode = "REFUND_REQUEST",
                Title = "Refund or Cancellation Request",
                Description = "Unable to park, facility malfunction, or requesting refund assistance for a booking charge.",
                Category = "Payment",
                DefaultPriority = "P2",
                EstimatedResponseTime = "Within 30 minutes",
                RequiresBooking = true,
                SuggestPhotoUpload = false,
                SupportsSelfUnlock = false
            },
            new()
            {
                IssueCode = "DUPLICATE_CHARGE",
                Title = "Duplicate Charge / Billing Dispute",
                Description = "Charged twice for the same booking, incorrect amount, or disputing overstay penalty charges.",
                Category = "Payment",
                DefaultPriority = "P1",
                EstimatedResponseTime = "Within 15 minutes",
                RequiresBooking = false,
                SuggestPhotoUpload = true,
                SupportsSelfUnlock = false
            },
            new()
            {
                IssueCode = "OTHER",
                Title = "General Inquiry & Feedback",
                Description = "Account settings, license plate update, app feedback, or general support.",
                Category = "General",
                DefaultPriority = "P2",
                EstimatedResponseTime = "Within 1 hour",
                RequiresBooking = false,
                SuggestPhotoUpload = false,
                SupportsSelfUnlock = false
            }
        };
    }

    public static bool CanBookingSelfUnlock(Booking b, DateTime now)
    {
        var isEligibleStatus = b.BookingStatus == BookingStatus.Confirmed || b.BookingStatus == BookingStatus.Active;
        var isWithinWindow = b.StartDate <= now.AddHours(1) && b.EndDate >= now.AddHours(-2);
        var isIotOnline = b.ParkingSpot?.IoTDevice != null && b.ParkingSpot.IoTDevice.DeviceStatus == DeviceStatus.Online;
        return isEligibleStatus && isWithinWindow && isIotOnline;
    }

    private static SupportBookingSummaryDto MapBookingSummary(Booking b, DateTime now)
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
            LastHeartbeatAt = iot?.LastHeartbeatAt,
            CanSelfUnlockGate = CanBookingSelfUnlock(b, now)
        };
    }
}
