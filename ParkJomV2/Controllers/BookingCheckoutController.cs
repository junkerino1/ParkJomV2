using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Constants;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;

namespace ParkJomV2.Controllers;

[ApiController]
[Authorize]
[Route("api/public/parking")]
public class BookingCheckoutController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly CurrentUserService _currentUser;
    private readonly AccessLogService _accessLogService;
    private readonly TransactionService _transactionService;
    private readonly WalletService _walletService;
    private readonly ILogger<BookingCheckoutController> _logger;

    public BookingCheckoutController(
        ApplicationDbContext context,
        CurrentUserService currentUser,
        AccessLogService accessLogService,
        TransactionService transactionService,
        WalletService walletService,
        ILogger<BookingCheckoutController> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _accessLogService = accessLogService;
        _transactionService = transactionService;
        _walletService = walletService;
        _logger = logger;
    }

    /// <summary>
    /// Returns the future dates and configured time ranges that are available for booking
    /// during a requested Malaysia calendar month.
    /// </summary>
    [HttpGet("{spotId:int}/booking-availability")]
    [ProducesResponseType(typeof(BookingAvailabilityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BookingAvailabilityResponse>> GetBookingAvailability(
        int spotId,
        [FromQuery] string? month,
        CancellationToken cancellationToken)
    {
        if (!CalendarMonthParser.TryParse(month, out var monthStart, out var monthEndExclusive))
        {
            return BadRequest(Error(StatusCodes.Status400BadRequest, "month must use YYYY-MM format."));
        }

        var user = await _currentUser.GetCurrentUserAsync();
        if (user == null)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                Error(StatusCodes.Status403Forbidden, "Authenticated user not found."));
        }

        try
        {
            var spot = await _context.ParkingSpots
                .AsNoTracking()
                .Include(item => item.VerificationRequests.Where(item => item.IsCurrent))
                .Include(item => item.ParkingAvailabilities)
                .FirstOrDefaultAsync(item => item.ParkingSpotId == spotId, cancellationToken);

            if (spot == null)
            {
                return NotFound(Error(StatusCodes.Status404NotFound, "Parking spot not found."));
            }

            if (spot.OwnerId == user.UserId)
            {
                return BadRequest(Error(StatusCodes.Status400BadRequest, "You cannot book your own parking spot."));
            }

            if (!IsBookable(spot))
            {
                return BadRequest(Error(StatusCodes.Status400BadRequest,
                    "Parking spot is not currently available for booking."));
            }

            if (!spot.VerificationRequests.Any(item => item.VerificationStatus == VerificationStatus.Approved))
            {
                return BadRequest(Error(StatusCodes.Status400BadRequest,
                    "Parking spot verification is not approved."));
            }

            var minimumBookingDate = MalaysiaToday().AddDays(1);
            var firstDate = monthStart > minimumBookingDate ? monthStart : minimumBookingDate;
            var availableDates = new List<BookingAvailabilityDateData>();

            if (firstDate < monthEndExclusive)
            {
                var blockingBookings = await _context.Bookings
                    .AsNoTracking()
                    .Where(booking =>
                        booking.ParkingSpotId == spotId &&
                        (booking.BookingStatus == BookingStatus.Confirmed ||
                         booking.BookingStatus == BookingStatus.Active) &&
                        booking.StartDate < monthEndExclusive.ToDateTime(TimeOnly.MinValue) &&
                        booking.EndDate > firstDate.ToDateTime(TimeOnly.MinValue))
                    .ToListAsync(cancellationToken);

                var bookedDates = new HashSet<DateOnly>();
                foreach (var booking in blockingBookings)
                {
                    var bookingStart = DateOnly.FromDateTime(booking.StartDate);
                    var bookingEndExclusive = GetBookingEndExclusiveDate(booking.EndDate);
                    var overlapStart = bookingStart > firstDate ? bookingStart : firstDate;
                    var overlapEndExclusive = bookingEndExclusive < monthEndExclusive
                        ? bookingEndExclusive
                        : monthEndExclusive;

                    for (var date = overlapStart; date < overlapEndExclusive; date = date.AddDays(1))
                    {
                        bookedDates.Add(date);
                    }
                }

                for (var date = firstDate; date < monthEndExclusive; date = date.AddDays(1))
                {
                    if (bookedDates.Contains(date))
                    {
                        continue;
                    }

                    var timeRanges = GetCoverageForDate(spot.ParkingAvailabilities, date)
                        .Select(interval => new BookingAvailabilityTimeRangeData
                        {
                            From = interval.From.ToString("HH:mm", CultureInfo.InvariantCulture),
                            To = interval.To.ToString("HH:mm", CultureInfo.InvariantCulture)
                        })
                        .ToList();

                    if (timeRanges.Count == 0)
                    {
                        continue;
                    }

                    availableDates.Add(new BookingAvailabilityDateData
                    {
                        Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        TimeRanges = timeRanges
                    });
                }
            }

            await _accessLogService.LogAsync(User, "GetBookingAvailability", true,
                $"ParkingSpotId={spotId}, Month={monthStart:yyyy-MM}, AvailableDates={availableDates.Count}");

            return Ok(new BookingAvailabilityResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Booking availability retrieved successfully.",
                ParkingSpotId = spotId,
                Month = monthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                MinimumBookingDate = minimumBookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                TotalAvailableDates = availableDates.Count,
                AvailableDates = availableDates
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving booking availability for parking spot {ParkingSpotId}", spotId);
            await _accessLogService.LogAsync(User, "GetBookingAvailability", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError,
                Error(StatusCodes.Status500InternalServerError,
                    "An error occurred while retrieving booking availability."));
        }
    }

    /// <summary>
    /// Creates a booking quote for a parking spot and returns the calculated price and expiration time.
    /// Prevent user from appending query parameters to the URL to bypass validation. 
    /// The quote is valid for 60 minutes and must be confirmed before it expires.
    /// </summary>
    [HttpPost("{spotId:int}/booking-quotes")]
    [ProducesResponseType(typeof(BookingQuoteResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookingQuoteResponse>> CreateBookingQuote(int spotId, [FromBody] CreateBookingQuoteRequest request,
        CancellationToken cancellationToken)

        // Purpose of cancellation token: 
        // User closes the browser or cancels the HTTP request while the database query is still running,
        // the cancellation token will be triggered and the database query will be cancelled to free up resources.
    {
        var user = await _currentUser.GetCurrentUserAsync();

        if (user == null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, Error(StatusCodes.Status403Forbidden, "Authenticated user not found."));
        }

        if (spotId <= 0)
        {
            return BadRequest(Error(StatusCodes.Status400BadRequest, "Parking spot not found."));
        }

        if (!TryParseInclusiveDates(request.StartDate, request.EndDate, out var startDate, out var endDate, out var dateError))
        {
            return BadRequest(Error(StatusCodes.Status400BadRequest, dateError));
        }

        var malaysiaToday = MalaysiaToday();
        if (startDate < malaysiaToday.AddDays(1))
        {
            return BadRequest(Error(StatusCodes.Status400BadRequest, "Bookings must start tomorrow or later in Malaysia time."));
        }

        // for future feature enhancement
        // can implement voucher or discount code logic
        // currently return error if user tries to use a voucher code
        if (!string.IsNullOrWhiteSpace(request.VoucherCode))
        {
            return BadRequest(Error(StatusCodes.Status400BadRequest, "Voucher codes are not available yet."));
        }

        try
        {
            var spot = await _context.ParkingSpots
                .AsNoTracking()
                .Include(item => item.VerificationRequests.Where(item => item.IsCurrent))
                .Include(item => item.ParkingAvailabilities)
                .FirstOrDefaultAsync(item => item.ParkingSpotId == spotId, cancellationToken);

            if (spot == null)
            {
                return NotFound(Error(StatusCodes.Status404NotFound, "Parking spot not found."));
            }

            if (spot.OwnerId == user.UserId)
            {
                return BadRequest(Error(StatusCodes.Status400BadRequest, "You cannot book your own parking spot."));
            }

            if (!IsBookable(spot))
            {
                return BadRequest(Error(StatusCodes.Status400BadRequest, "Parking spot is not currently available for booking."));
            }

            if (!spot.VerificationRequests.Any(item => item.VerificationStatus == VerificationStatus.Approved))
            {
                return BadRequest(Error(StatusCodes.Status400BadRequest, "Parking spot verification is not approved."));
            }

            var endExclusive = endDate.AddDays(1);

            // Reuse a previous, still-valid quote for the same spot and period
            // instead of creating a duplicate. Expired or already-redeemed quotes are ignored.
            var existingQuote = await _context.BookingQuotes
                .Where(item => item.RenterId == user.UserId
                    && item.ParkingSpotId == spotId
                    && item.StartDate == startDate.ToDateTime(TimeOnly.MinValue)
                    && item.EndDate == endExclusive.ToDateTime(TimeOnly.MinValue)
                    && item.RedeemedAt == null
                    && item.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(item => item.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingQuote != null)
            {
                await _accessLogService.LogAsync(User, "CreateBookingQuote", true, $"BookingQuoteId={existingQuote.BookingQuoteId} (reused)");
                return Ok(new BookingQuoteResponse
                {
                    Code = StatusCodes.Status200OK,
                    Success = true,
                    Message = "Existing booking quote reused.",
                    Data = MapQuote(existingQuote)
                });
            }

            // Count only the days actually covered by the spot's availability rules.
            // For a weekday-only spot, weekends inside the requested range are simply
            // not rented rather than rejecting the whole quote.
            var coveredDays = CountCoveredDays(spot.ParkingAvailabilities, startDate, endExclusive);
            if (coveredDays <= 0)
            {
                return BadRequest(Error(StatusCodes.Status400BadRequest, "The selected period is invalid. Please try other dates."));
            }

            var overlap = await HasBookingOverlap(spotId, startDate, endExclusive, cancellationToken);
            if (overlap)
            {
                return Conflict(Error(StatusCodes.Status409Conflict, "Parking spot is already booked for part of the selected period."));
            }

            if (!TryCalculatePrice(spot, coveredDays, out var rateType, out var ratePerDay, out var subtotal, out var priceError))
            {
                return BadRequest(Error(StatusCodes.Status400BadRequest, priceError));
            }

            var now = DateTime.UtcNow;
            var quote = new BookingQuote
            {
                RenterId = user.UserId,
                ParkingSpotId = spotId,
                StartDate = startDate.ToDateTime(TimeOnly.MinValue),
                EndDate = endExclusive.ToDateTime(TimeOnly.MinValue),
                BookedDays = coveredDays,
                RateType = rateType,
                RatePerDay = ratePerDay,
                RentalSubtotal = subtotal,
                PlatformCommissionRate = PlatformConstants.CommissionRate,
                PlatformCommissionAmount = RoundMoney(subtotal * PlatformConstants.CommissionRate / 100m),
                OwnerPayoutAmount = RoundMoney(subtotal - RoundMoney(subtotal * PlatformConstants.CommissionRate / 100m)),
                RenterTotal = subtotal,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(60)
            };

            _context.BookingQuotes.Add(quote);
            await _context.SaveChangesAsync(cancellationToken);
            await _accessLogService.LogAsync(User, "CreateBookingQuote", true, $"BookingQuoteId={quote.BookingQuoteId}");

            return StatusCode(StatusCodes.Status201Created, new BookingQuoteResponse
            {
                Code = StatusCodes.Status201Created,
                Success = true,
                Message = "Booking quote created successfully.",
                Data = MapQuote(quote)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating booking quote for spot {SpotId}", spotId);
            await _accessLogService.LogAsync(User, "CreateBookingQuote", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, Error(500, "An error occurred while creating the booking quote."));
        }
    }

    /// <summary>Returns today's date in the platform's Malaysia time zone.</summary>
    private static DateOnly MalaysiaToday() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

    /// <summary>Parses customer-facing inclusive ISO dates and validates their ordering.</summary>
    private static bool TryParseInclusiveDates(string startText, string endText, out DateOnly start, out DateOnly end, out string error)
    {
        start = default;
        end = default;
        error = string.Empty;
        var validStart = DateOnly.TryParseExact(startText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start);
        var validEnd = DateOnly.TryParseExact(endText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out end);
        if (!validStart || !validEnd)
        {
            error = "startDate and endDate must use valid YYYY-MM-DD dates.";
            return false;
        }

        if (start > end)
        {
            error = "startDate must be on or before endDate.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if parking spot is published, verified, and available for booking. Does not check for date conflicts or ownership.
    /// </summary>
    private static bool IsBookable(ParkingSpot spot) => spot.IsPublished && spot.AvailabilityStatus == AvailabilityStatus.Available;

    /// <summary>
    /// Counts how many days within a range are covered by at least one availability interval.
    /// </summary>
    private static int CountCoveredDays(IEnumerable<Availability> rules, DateOnly start, DateOnly endExclusive)
    {
        var count = 0;
        for (var date = start; date < endExclusive; date = date.AddDays(1))
        {
            if (GetCoverageForDate(rules, date).Count > 0) 
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Returns merged availability intervals for one Malaysia calendar date.
    /// </summary>
    private static List<(TimeOnly From, TimeOnly To)> GetCoverageForDate(IEnumerable<Availability> rules, DateOnly date)
    {
        var intervals = rules.Where(rule =>
            (!rule.EffectiveFrom.HasValue || date >= rule.EffectiveFrom.Value) &&
            (!rule.EffectiveUntil.HasValue || date <= rule.EffectiveUntil.Value) &&
            (rule.DayType == DayType.Everyday ||
             (rule.DayType == DayType.Weekday && date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) ||
             (rule.DayType == DayType.Weekend && date.DayOfWeek is (DayOfWeek.Saturday or DayOfWeek.Sunday))))
            .OrderBy(rule => rule.StartTime)
            .Select(rule => (From: rule.StartTime, To: rule.EndTime));

        var merged = new List<(TimeOnly From, TimeOnly To)>();
        foreach (var interval in intervals)
        {
            if (merged.Count == 0 || interval.From > merged[^1].To) merged.Add(interval);
            else if (interval.To > merged[^1].To) merged[^1] = (merged[^1].From, interval.To);
        }
        return merged;
    }

    /// <summary>Converts a booking end timestamp to the exclusive date boundary used by the calendar.</summary>
    private static DateOnly GetBookingEndExclusiveDate(DateTime bookingEnd)
    {
        var endDate = DateOnly.FromDateTime(bookingEnd);
        return bookingEnd.TimeOfDay == TimeSpan.Zero ? endDate : endDate.AddDays(1);
    }

    /// <summary>
    /// Checks if there is any confirmed or active booking that overlaps with the requested period for a parking spot.
    /// The end date is exclusive, so a booking that ends on the requested start date does not count as an overlap. 
    /// This method uses a database query with a cancellation token to avoid long-running
    /// </summary>
    private async Task<bool> HasBookingOverlap(int spotId, DateOnly start, DateOnly endExclusive, CancellationToken cancellationToken)
    {
        var startBoundary = start.ToDateTime(TimeOnly.MinValue);
        var endBoundary = endExclusive.ToDateTime(TimeOnly.MinValue);
        return await _context.Bookings.AnyAsync(item =>
            item.ParkingSpotId == spotId &&
            (item.BookingStatus == BookingStatus.Confirmed || item.BookingStatus == BookingStatus.Active) &&
            item.StartDate < endBoundary && item.EndDate > startBoundary,
            cancellationToken);
    }

    /// <summary>
    /// Calculates parking spot price using the number of days actually rented (the days
    /// covered by the spot's availability rules, e.g. weekdays only for a weekday-only spot).
    /// Platform rate rules:
    /// - 1-9 days: Daily rate
    /// - 10-20 days: 10% discounted daily rate
    /// - 21+ days: Monthly rate 
    /// Returns false if the spot does not have a configured rate for the requested period.
    /// </summary>
    private static bool TryCalculatePrice(ParkingSpot spot, int days, out BookingRateType rateType, out decimal ratePerDay, out decimal subtotal, out string error)
    {
        rateType = BookingRateType.Daily;
        ratePerDay = 0;
        subtotal = 0;
        error = string.Empty;
        if (days <= 0) 
        { 
            error = "The booking period must contain at least one day."; 
            return false; 
        }

        if (days <= 9 && spot.DailyRate.HasValue) 
        { 
            rateType = BookingRateType.Daily; 
            ratePerDay = spot.DailyRate.Value; 
            subtotal = ratePerDay * days; 
        }
        else if (days <= 20 && spot.DailyRate.HasValue) 
        { 
            rateType = BookingRateType.DiscountedDaily;
            ratePerDay = RoundMoney(spot.DailyRate.Value * 0.90m); 
            subtotal = spot.DailyRate.Value * 0.90m * days; 
        }
        else if (days >= 21 && spot.MonthlyRate.HasValue) 
        { 
            rateType = BookingRateType.MonthlyProrated; 
            ratePerDay = RoundMoney(spot.MonthlyRate.Value / 30m); 
            subtotal = spot.MonthlyRate.Value / 30m * days; 
        }
        else 
        { 
            error = "The parking spot does not have a rate configured for this period."; 
            return false; 
        }

        subtotal = RoundMoney(subtotal);
        ratePerDay = RoundMoney(ratePerDay);
        return true;
    }

    /// <summary>Rounds money values to two decimal places using midpoint-away-from-zero.</summary>
    private static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Maps a quote entity to the public checkout response.</summary>
    private static BookingQuoteData MapQuote(BookingQuote quote) => new()
    {
        QuoteId = quote.BookingQuoteId,
        ParkingSpotId = quote.ParkingSpotId,
        StartDate = DateOnly.FromDateTime(quote.StartDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        EndDate = DateOnly.FromDateTime(quote.EndDate).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        BookedDays = quote.BookedDays,
        RateType = quote.RateType.ToString(),
        RatePerDay = quote.RatePerDay,
        RentalSubtotal = quote.RentalSubtotal,
        ExpiresAt = quote.ExpiresAt
    };

    /// <summary>
    /// Builds the API's standard error response.
    /// </summary>
    private static ErrorResponse Error(int code, string message) => new() { Code = code, Success = false, Message = message };

}
