using System.Globalization;
using System.Security.Claims;
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
    private readonly AccessLogService _accessLogService;
    private readonly TransactionService _transactionService;
    private readonly WalletService _walletService;
    private readonly ILogger<BookingCheckoutController> _logger;

    public BookingCheckoutController(
        ApplicationDbContext context,
        AccessLogService accessLogService,
        TransactionService transactionService,
        WalletService walletService,
        ILogger<BookingCheckoutController> logger)
    {
        _context = context;
        _accessLogService = accessLogService;
        _transactionService = transactionService;
        _walletService = walletService;
        _logger = logger;
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
        var userId = GetUserId();
        if (spotId <= 0 || userId <= 0)
        {
            return BadRequest(Error(StatusCodes.Status400BadRequest, "spotId and authenticated user are required."));
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

            if (spot.OwnerId == userId)
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
                .Where(item => item.RenterId == userId
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
                RenterId = userId,
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

    /// <summary>Confirms and pays for a quote in one idempotent database transaction.</summary>
    [HttpPost("~/api/bookings/confirm")]
    [ProducesResponseType(typeof(ConfirmBookingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConfirmBookingResponse>> ConfirmBooking(
        [FromBody] ConfirmBookingRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim();
        if (userId <= 0 || string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
        {
            return BadRequest(Error(StatusCodes.Status400BadRequest, "A valid Idempotency-Key header is required."));
        }

        var existing = await FindExistingBooking(userId, idempotencyKey, cancellationToken);
        if (existing != null)
        {
            return ExistingBookingResult(existing, request);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);

        try
        {
            var quoteKey = request.QuoteId;
            var quote = await _context.BookingQuotes
                .FirstOrDefaultAsync(item => item.BookingQuoteId == quoteKey, cancellationToken);
            if (quote == null)
            {
                return NotFound(Error(StatusCodes.Status404NotFound, "Booking quote not found."));
            }

            // locks the parking spot row while the booking transaction is being processed.
            // To prevent two users from booking the same parking spot at the same time.
            var lockedSpot = await _context.ParkingSpots
                .FromSqlInterpolated($"SELECT * FROM [ParkingSpots] WITH (UPDLOCK, HOLDLOCK) WHERE [ParkingSpotId] = {quote.ParkingSpotId}")
                .FirstOrDefaultAsync(cancellationToken);
            if (lockedSpot == null)
            {
                return NotFound(Error(StatusCodes.Status404NotFound, "Parking spot not found."));
            }


            // check if another request with same idempotency key has already created a booking for this user
            // prevent duplicated booking creation if user sends the same request multiple times
            var racedBooking = await FindExistingBooking(userId, idempotencyKey, cancellationToken);
            if (racedBooking != null)
            {
                await transaction.CommitAsync(cancellationToken);
                return ExistingBookingResult(racedBooking, request);
            }

            if (quote.RenterId != userId)
            {
                return Conflict(Error(StatusCodes.Status409Conflict, "This quote does not belong to the authenticated renter."));
            }

            if (quote.RedeemedAt.HasValue || quote.ExpiresAt <= DateTime.UtcNow)
            {
                return Conflict(Error(StatusCodes.Status409Conflict, "The booking quote has expired or was already used."));
            }

            var verificationApproved = await _context.ParkingVerificationRequests.AnyAsync(
                item => item.ParkingSpotId == lockedSpot.ParkingSpotId && item.IsCurrent && item.VerificationStatus == VerificationStatus.Approved,
                cancellationToken);
            if (!IsBookable(lockedSpot) || !verificationApproved || lockedSpot.OwnerId == userId)
            {
                return BadRequest(Error(StatusCodes.Status400BadRequest, "Parking spot is not available for booking."));
            }

            var vehicle = await _context.Vehicles.FirstOrDefaultAsync(
                item => item.VehicleId == request.VehicleId && item.UserId == userId,
                cancellationToken);
            if (vehicle == null)
            {
                return BadRequest(Error(StatusCodes.Status400BadRequest, "Vehicle not found or does not belong to you."));
            }

            var rules = await _context.Availabilities
                .Where(item => item.ParkingSpotId == quote.ParkingSpotId)
                .ToListAsync(cancellationToken);
            var quoteStart = DateOnly.FromDateTime(quote.StartDate);
            var quoteEndExclusive = DateOnly.FromDateTime(quote.EndDate);
            var coveredDays = CountCoveredDays(rules, quoteStart, quoteEndExclusive);
            if (coveredDays <= 0 || coveredDays != quote.BookedDays ||
                await HasBookingOverlap(quote.ParkingSpotId, quoteStart, quoteEndExclusive, cancellationToken))
            {
                return Conflict(Error(StatusCodes.Status409Conflict, "The selected period is no longer available."));
            }

            // lock wallet to prevent race conditions when multiple bookings are being processed at the same time 
            var lockedWallets = await _context.Wallets
                .FromSqlInterpolated($"SELECT * FROM [Wallets] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] IN ({userId}, {lockedSpot.OwnerId}) AND [Status] = {WalletStatus.Active.ToString()}")
                .OrderBy(item => item.WalletId)
                .ToListAsync(cancellationToken);
            var renterWallet = lockedWallets.FirstOrDefault(item => item.UserId == userId);
            var ownerWallet = lockedWallets.FirstOrDefault(item => item.UserId == lockedSpot.OwnerId);

            // Atomic server-side increment
            // Increment commission and total revenue in a single SQL statement to avoid race conditions
            var platformWallet = await _context.PlatformWallets
                .AsNoTracking()
                .OrderBy(item => item.PlatformWalletId)
                .FirstOrDefaultAsync(cancellationToken);

            // If any of the wallets are missing, return a 400 Bad Request error. 
            // This ensures that the booking process cannot proceed without all necessary wallets being present.
            if (renterWallet == null || ownerWallet == null || platformWallet == null)
            {
                // future enhancement: create an alert message to notify the admin
                return BadRequest(Error(StatusCodes.Status400BadRequest, "An active renter, owner, and platform wallet are required."));
            }

            if (renterWallet.Balance < quote.RenterTotal)
            {
                return BadRequest(Error(StatusCodes.Status400BadRequest, "Insufficient wallet balance."));
            }

            var now = DateTime.UtcNow;

            // generate booking reference as UUID, collision prevention applied
            var bookingReference = GenerateBookingReference();

            var booking = new Booking
            {
                BookingReference = bookingReference,
                RenterId = userId,
                ParkingSpotId = quote.ParkingSpotId,
                VehicleId = request.VehicleId,
                StartDate = quote.StartDate,
                EndDate = quote.EndDate,
                BookingStatus = BookingStatus.Confirmed,
                TotalAmount = quote.RenterTotal,
                BookedDays = quote.BookedDays,
                RateType = quote.RateType,
                RatePerDaySnapshot = quote.RatePerDay,
                RentalSubtotal = quote.RentalSubtotal,
                PlatformCommissionRate = quote.PlatformCommissionRate,
                PlatformCommissionAmount = quote.PlatformCommissionAmount,
                OwnerPayoutAmount = quote.OwnerPayoutAmount,
                BookingQuoteId = quote.BookingQuoteId,
                IdempotencyKey = idempotencyKey,
                CreatedAt = now,
                UpdatedAt = now
            };

            // deduct renter wallet balance 
            _walletService.Deduct(renterWallet, quote.RenterTotal, now);

            // put owner wallet on hold for payout
            // actual payout will be processed by a separate scheduled job after the booking period ends
            _walletService.Hold(ownerWallet, quote.OwnerPayoutAmount, now);

            quote.RedeemedAt = now;

            _context.Bookings.Add(booking);

            // Record the three ledger entries for this booking: renter payment, owner payout, platform commission.
            _transactionService.Create(renterWallet.WalletId, null, booking, TransactionType.Payment, -quote.RenterTotal, bookingReference, now);
            _transactionService.Create(ownerWallet.WalletId, null, booking, TransactionType.OwnerPayout, quote.OwnerPayoutAmount, bookingReference, now);
            _transactionService.Create(null, platformWallet.PlatformWalletId, booking, TransactionType.PlatformCommission, quote.PlatformCommissionAmount, bookingReference, now);

            await _context.SaveChangesAsync(cancellationToken);

            // increment platform wallet balance and total revenue (atomic server-side increment)
            await _walletService.IncrementPlatformWalletAsync(platformWallet, quote.PlatformCommissionAmount, now, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            await _accessLogService.LogAsync(User, "ConfirmBooking", true, $"BookingId={booking.BookingId}", booking.BookingId);

            return Ok(new ConfirmBookingResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Booking confirmed and paid successfully.",
                Data = MapBooking(booking)
            });
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            var racedBooking = await FindExistingBooking(userId, idempotencyKey, cancellationToken);
            if (racedBooking != null)
            {
                return ExistingBookingResult(racedBooking, request);
            }

            _logger.LogError(ex, "Database error confirming booking for user {UserId}", userId);
            return Conflict(Error(StatusCodes.Status409Conflict, "The booking could not be confirmed because the request conflicted with another booking."));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error confirming booking for user {UserId}", userId);
            await _accessLogService.LogAsync(User, "ConfirmBooking", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, Error(500, "An error occurred while confirming the booking."));
        }
    }

    /// <summary>Returns the authenticated user's numeric identifier from the JWT claims.</summary>
    private int GetUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : 0;

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

    /// <summary>Finds a previously confirmed booking for the renter's idempotency key.</summary>
    private async Task<Booking?> FindExistingBooking(int userId, string key, CancellationToken cancellationToken) =>
        await _context.Bookings.Include(item => item.ParkingSpot).FirstOrDefaultAsync(item => item.RenterId == userId && item.IdempotencyKey == key, cancellationToken);

    /// <summary>Returns the existing booking for a retry, or a conflict for a changed payload.</summary>
    private ActionResult<ConfirmBookingResponse> ExistingBookingResult(Booking booking, ConfirmBookingRequest request)
    {
        if (booking.BookingQuoteId != request.QuoteId || booking.VehicleId != request.VehicleId)
        {
            return Conflict(Error(StatusCodes.Status409Conflict, "The Idempotency-Key was already used with a different booking request."));
        }
        return Ok(new ConfirmBookingResponse { Code = 200, Success = true, Message = "Booking has already been confirmed", Data = MapBooking(booking) });
    }

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
    /// Maps a confirmed booking entity to the renter checkout response.
    /// </summary>
    private static ConfirmedBookingData MapBooking(Booking booking) => new()
    {
        BookingId = booking.BookingId,
        BookingReference = booking.BookingReference,
        QuoteId = booking.BookingQuoteId!.Value,
        ParkingSpotId = booking.ParkingSpotId,
        VehicleId = booking.VehicleId,
        StartDate = DateOnly.FromDateTime(booking.StartDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        EndDate = DateOnly.FromDateTime(booking.EndDate).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        BookedDays = booking.BookedDays,
        BookingStatus = booking.BookingStatus.ToString(),
        CreatedAt = booking.CreatedAt
    };

    /// <summary>
    /// Builds the API's standard error response.
    /// </summary>
    private static ErrorResponse Error(int code, string message) => new() { Code = code, Success = false, Message = message };

    /// <summary>
    /// Generates a unique booking reference using a time-ordered UUIDv7.
    /// Collision-free without a DB check; e.g. PJ-0193F2C4A0B1C2D3E4F5A6B7C8D9E0F1.
    /// </summary>
    private static string GenerateBookingReference() => $"PJ-{Guid.CreateVersion7():N}";
}
