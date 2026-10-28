using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")]
public class AdminDashboardController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<AdminDashboardController> _logger;

    public AdminDashboardController(
        ApplicationDbContext context,
        AccessLogService accessLogService,
        ILogger<AdminDashboardController> logger)
    {
        _context = context;
        _accessLogService = accessLogService;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/admin/dashboard
    /// Aggregates live platform counters, booking counts, weekly trend, stations,
    /// hourly breakdown, user growth, and revenue by rate type.
    /// </summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(AdminDashboardResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminDashboardResponse>> GetDashboard()
    {
        try
        {
            var now = DateTime.UtcNow;

            // 1. Users
            var totalUsers = await _context.Users.CountAsync(u => u.AccountStatus != "Deactivated");
            var totalOwners = await _context.Users.CountAsync(u => u.UserType == UserType.PropertyOwner && u.AccountStatus != "Deactivated");
            var totalCommuters = await _context.Users.CountAsync(u => u.UserType == UserType.Renter && u.AccountStatus != "Deactivated");

            // 2. Bollards
            var totalBollards = await _context.IoTDevices.CountAsync();
            var onlineBollards = await _context.IoTDevices.CountAsync(d => d.DeviceStatus == DeviceStatus.Online);
            var onlineBollardsRate = totalBollards > 0
                ? (int)Math.Round((double)onlineBollards / totalBollards * 100)
                : 0;

            // 3. Verification & Support & Overstays
            var pendingListingsCount = await _context.ParkingVerificationRequests
                .CountAsync(vr => vr.IsCurrent && vr.VerificationStatus == VerificationStatus.Pending);

            var openDisputesCount = await _context.DisputeInvestigations
                .CountAsync(d => d.Status != DisputeStatus.Approved && d.Status != DisputeStatus.Declined);

            var openTicketsCount = await _context.SupportTickets
                .CountAsync(t => t.Status != SupportTicketStatus.Resolved
                              && t.Status != SupportTicketStatus.Closed);

            var activeOverstaysCount = await _context.Bookings
                .CountAsync(b => b.BookingStatus == BookingStatus.Active && b.EndDate < now);

            // 4. Booking Counts
            var bookingStatusGroups = await _context.Bookings
                .GroupBy(b => b.BookingStatus)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var counts = new AdminDashboardBookingCountsDTO
            {
                Pending = bookingStatusGroups.FirstOrDefault(x => x.Status == BookingStatus.Pending)?.Count ?? 0,
                Confirmed = bookingStatusGroups.FirstOrDefault(x => x.Status == BookingStatus.Confirmed)?.Count ?? 0,
                Active = bookingStatusGroups.FirstOrDefault(x => x.Status == BookingStatus.Active)?.Count ?? 0,
                Completed = bookingStatusGroups.FirstOrDefault(x => x.Status == BookingStatus.Completed)?.Count ?? 0,
                Cancelled = bookingStatusGroups.FirstOrDefault(x => x.Status == BookingStatus.Cancelled)?.Count ?? 0,
                Expired = bookingStatusGroups.FirstOrDefault(x => x.Status == BookingStatus.Expired)?.Count ?? 0,
            };
            counts.Total = counts.Pending + counts.Confirmed + counts.Active + counts.Completed + counts.Cancelled + counts.Expired;

            // 5. Financial metrics
            var activeBookings = counts.Active;
            var validStatuses = new[] { BookingStatus.Confirmed, BookingStatus.Active, BookingStatus.Completed };

            var grossBookingValue = await _context.Bookings
                .Where(b => validStatuses.Contains(b.BookingStatus))
                .SumAsync(b => (decimal?)b.TotalAmount) ?? 0m;

            var platformCommission = await _context.Bookings
                .Where(b => validStatuses.Contains(b.BookingStatus))
                .SumAsync(b => (decimal?)b.PlatformCommissionAmount) ?? 0m;

            var activeOrCompleted = new[] { BookingStatus.Active, BookingStatus.Completed };
            var ownerPayoutTotal = await _context.Bookings
                .Where(b => activeOrCompleted.Contains(b.BookingStatus))
                .SumAsync(b => (decimal?)b.OwnerPayoutAmount) ?? 0m;

            var stats = new AdminDashboardStatsDTO
            {
                TotalRevenue = platformCommission,
                PlatformCommission = platformCommission,
                GrossBookingValue = grossBookingValue,
                OwnerPayoutTotal = ownerPayoutTotal,
                ActiveBookings = activeBookings,
                TotalUsers = totalUsers,
                TotalOwners = totalOwners,
                TotalCommuters = totalCommuters,
                OnlineBollardsRate = onlineBollardsRate,
                TotalBollards = totalBollards,
                OnlineBollards = onlineBollards,
                PendingListingsCount = pendingListingsCount,
                OpenDisputesCount = openDisputesCount,
                ActiveOverstaysCount = activeOverstaysCount,
                OpenTicketsCount = openTicketsCount
            };

            // 6. Weekly trend (past 7 days)
            var sevenDaysAgo = now.Date.AddDays(-6);
            var weeklyRaw = await _context.Bookings
                .Where(b => b.CreatedAt >= sevenDaysAgo)
                .Select(b => new { b.CreatedAt, b.TotalAmount, b.PlatformCommissionAmount })
                .ToListAsync();

            var weekly = new List<DashboardDailyPointDTO>();
            for (int i = 6; i >= 0; i--)
            {
                var dayDate = now.Date.AddDays(-i);
                var nextDayDate = dayDate.AddDays(1);
                var dayBookings = weeklyRaw.Where(b => b.CreatedAt >= dayDate && b.CreatedAt < nextDayDate).ToList();

                weekly.Add(new DashboardDailyPointDTO
                {
                    Day = dayDate.ToString("ddd"),
                    Date = dayDate.ToString("yyyy-MM-dd"),
                    Bookings = dayBookings.Count,
                    Revenue = dayBookings.Sum(b => b.TotalAmount),
                    Commission = dayBookings.Sum(b => b.PlatformCommissionAmount)
                });
            }

            // 7. Top Stations
            var stations = await _context.Bookings
                .Where(b => b.ParkingSpot.Property != null && b.ParkingSpot.Property.Station != null)
                .GroupBy(b => b.ParkingSpot.Property.Station.StationName)
                .Select(g => new DashboardStationPointDTO
                {
                    Name = g.Key,
                    Bookings = g.Count(),
                    Revenue = g.Sum(b => b.TotalAmount)
                })
                .OrderByDescending(s => s.Bookings)
                .Take(5)
                .ToListAsync();

            // 8. Hourly utilization (00:00 to 23:00 across past 30 days)
            var thirtyDaysAgo = now.AddDays(-30);
            var hourlyRaw = await _context.Bookings
                .Where(b => b.CreatedAt >= thirtyDaysAgo)
                .Select(b => b.CreatedAt.Hour)
                .ToListAsync();

            var hourly = new List<DashboardHourlyPointDTO>();
            for (int h = 0; h < 24; h++)
            {
                hourly.Add(new DashboardHourlyPointDTO
                {
                    Hour = $"{h:D2}:00",
                    Bookings = hourlyRaw.Count(hour => hour == h)
                });
            }

            // 9. User growth (past 6 months)
            var usersTimeline = await _context.Users
                .Select(u => new { u.UserType, u.CreatedAt })
                .ToListAsync();

            var userGrowth = new List<DashboardUserGrowthPointDTO>();
            for (int m = 5; m >= 0; m--)
            {
                var targetMonth = now.AddMonths(-m);
                var monthEnd = new DateTime(targetMonth.Year, targetMonth.Month, DateTime.DaysInMonth(targetMonth.Year, targetMonth.Month), 23, 59, 59, DateTimeKind.Utc);

                userGrowth.Add(new DashboardUserGrowthPointDTO
                {
                    Month = targetMonth.ToString("MMM"),
                    Drivers = usersTimeline.Count(u => u.UserType == UserType.Renter && u.CreatedAt <= monthEnd),
                    Owners = usersTimeline.Count(u => u.UserType == UserType.PropertyOwner && u.CreatedAt <= monthEnd)
                });
            }

            // 10. Revenue by booking rate type (Daily vs Monthly)
            var revenueByType = await _context.Bookings
                .GroupBy(b => b.RateType)
                .Select(g => new DashboardRevenueTypePointDTO
                {
                    Type = g.Key.ToString(),
                    Revenue = g.Sum(b => b.TotalAmount),
                    Bookings = g.Count()
                })
                .ToListAsync();

            var dashboardData = new AdminDashboardDataDTO
            {
                Stats = stats,
                BookingCounts = counts,
                Weekly = weekly,
                Stations = stations,
                Hourly = hourly,
                UserGrowth = userGrowth,
                RevenueByType = revenueByType,
                GeneratedAt = now.ToString("o"),
                TimeZone = "Asia/Kuala_Lumpur"
            };

            await _accessLogService.LogAsync(User, "GetAdminDashboard", true, "Retrieved live platform dashboard metrics");

            return Ok(new AdminDashboardResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Admin dashboard metrics retrieved successfully",
                Data = dashboardData
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating admin dashboard metrics");
            return StatusCode(StatusCodes.Status500InternalServerError, new AdminDashboardResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while generating admin dashboard metrics"
            });
        }
    }
}
