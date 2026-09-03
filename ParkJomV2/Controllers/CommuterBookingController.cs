using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Constants;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace ParkJomV2.Controllers;

[ApiController]
[Authorize]
[Route("api/commuter/bookings")]
public class CommuterBookingsController : ControllerBase
{
    private const int MaximumPageSize = 50;
    private readonly ApplicationDbContext _context;
    private readonly CurrentUserService _currentUser;
    private readonly AccessLogService _accessLogService;
    private readonly TransactionService _transactionService;
    private readonly WalletService _walletService;
    private readonly ILogger<CommuterBookingsController> _logger;

    public CommuterBookingsController(
        ApplicationDbContext context,
        CurrentUserService currentUser,
        AccessLogService accessLogService,
        TransactionService transactionService,
        WalletService walletService,
        ILogger<CommuterBookingsController> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _accessLogService = accessLogService;
        _transactionService = transactionService;
        _walletService = walletService;
        _logger = logger;
    }

    /// <summary>
    /// Returns the authenticated commuter's bookings.
    /// Without query parameters it returns a paged list; pass ?booking-id= to fetch a single booking's detail.
    /// List filters: status (Pending, Confirmed, Cancelled, Completed, Expired, Active),
    /// fromDate (YYYY-MM-DD, inclusive) and toDate (YYYY-MM-DD, inclusive) that the booking range overlaps.
    /// </summary>
    [HttpGet("my-bookings")]
    [ProducesResponseType(typeof(CommuterBookingListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(BookingDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<object>> GetMyBookings(
        [FromQuery(Name = "booking-id")] int? bookingId = null,
        [FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, MaximumPageSize)] int pageSize = 10,
        [FromQuery] string? status = null,
        [FromQuery] string? fromDate = null,
        [FromQuery] string? toDate = null)
    {
        try
        {
            var user = await _currentUser.GetCurrentUserAsync();

            if (user == null)
            {
                await _accessLogService.LogAsync(User, "GetMyBookings", false, "User not found");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            // Single-booking detail: GET /api/commuter/bookings/my-bookings?booking-id={id}
            if (bookingId.HasValue)
            {
                var booking = await _context.Bookings
                    .AsNoTracking()
                    .Include(b => b.ParkingSpot)
                    .Include(b => b.Vehicle)
                    .Include(b => b.Renter)
                    .FirstOrDefaultAsync(b => b.BookingId == bookingId.Value);

                if (booking == null)
                {
                    await _accessLogService.LogAsync(User, "GetMyBookings", false, $"Booking not found (bookingId={bookingId})");
                    return NotFound(new ErrorResponse
                    {
                        Code = StatusCodes.Status404NotFound,
                        Success = false,
                        Message = "Booking not found"
                    });
                }

                if (booking.RenterId != user.UserId && user.UserType != UserType.Admin)
                {
                    await _accessLogService.LogAsync(User, "GetMyBookings", false, $"Not authorized (bookingId={bookingId})", bookingId);
                    return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                    {
                        Code = StatusCodes.Status403Forbidden,
                        Success = false,
                        Message = "You are not authorized to view this booking"
                    });
                }

                await _accessLogService.LogAsync(User, "GetMyBookings", true, $"BookingId={bookingId}", bookingId);

                return Ok(new BookingDetailResponse
                {
                    Code = StatusCodes.Status200OK,
                    Success = true,
                    Message = "Booking retrieved successfully",
                    Data = MapToBookingResponseDTO(booking)
                });
            }

            if (!TryParseBookingStatus(status, out var bookingStatus))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "status must be one of: Pending, Confirmed, Cancelled, Completed, Expired, Active."
                });
            }

            if (!TryParseIsoDate(fromDate, out var fromDateOnly) ||
                !TryParseIsoDate(toDate, out var toDateOnly))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "fromDate and toDate must use YYYY-MM-DD format."
                });
            }

            if (fromDateOnly.HasValue && toDateOnly.HasValue && fromDateOnly.Value > toDateOnly.Value)
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "fromDate must not be later than toDate."
                });
            }

            var bookingsQuery = _context.Bookings
                .AsNoTracking()
                .Where(booking => booking.RenterId == user.UserId);

            if (bookingStatus.HasValue)
            {
                bookingsQuery = bookingsQuery.Where(booking => booking.BookingStatus == bookingStatus.Value);
            }

            if (fromDateOnly.HasValue)
            {
                var fromDateTime = fromDateOnly.Value.ToDateTime(TimeOnly.MinValue);
                bookingsQuery = bookingsQuery.Where(booking => booking.EndDate > fromDateTime);
            }

            if (toDateOnly.HasValue)
            {
                // toDate is inclusive, so a booking overlaps it as long as it starts before the next day.
                var toExclusiveDateTime = toDateOnly.Value.ToDateTime(TimeOnly.MinValue).AddDays(1);
                bookingsQuery = bookingsQuery.Where(booking => booking.StartDate < toExclusiveDateTime);
            }

            var totalCount = await bookingsQuery.CountAsync();

            var bookings = await bookingsQuery
                .Include(booking => booking.ParkingSpot)
                .Include(booking => booking.Vehicle)
                .Include(booking => booking.Reviews.Where(review => review.ReviewerId == user.UserId))
                    .ThenInclude(review => review.Reviewer)
                .OrderByDescending(booking => booking.StartDate)
                .ThenByDescending(booking => booking.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var data = bookings.Select(MapToBookingHistoryItemDTO).ToList();

            await _accessLogService.LogAsync(
                User,
                "GetMyBookings",
                true,
                $"Page={page}, PageSize={pageSize}, Status={bookingStatus?.ToString() ?? "all"}, " +
                $"FromDate={fromDateOnly?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "all"}, " +
                $"ToDate={toDateOnly?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "all"}, " +
                $"Returned={data.Count}, Total={totalCount}");

            return Ok(new CommuterBookingListResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = data.Count > 0
                    ? $"Retrieved {data.Count} booking(s) successfully"
                    : "No bookings found",
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalCount == 0
                    ? 0
                    : (int)Math.Ceiling(totalCount / (double)pageSize),
                Status = bookingStatus?.ToString(),
                FromDate = fromDateOnly?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ToDate = toDateOnly?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Data = data
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving the authenticated user's bookings");
            await _accessLogService.LogAsync(User, "GetMyBookings", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving your bookings"
            });
        }
    }


    /// <summary>
    /// Confirms and pays for a quote in one idempotent database transaction.
    /// </summary>
    [HttpPost("confirm")]
    [ProducesResponseType(typeof(ConfirmBookingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConfirmBookingResponse>> ConfirmBooking(
        [FromBody] ConfirmBookingRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _currentUser.GetCurrentUserAsync();
        var userId = user!.UserId;

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
            _transactionService.Create(renterWallet.WalletId, null, booking, TransactionType.Payment, -quote.RenterTotal, PaymentMethod.Wallet, bookingReference, now);
            _transactionService.Create(ownerWallet.WalletId, null, booking, TransactionType.OwnerPayout, quote.OwnerPayoutAmount, PaymentMethod.Wallet, bookingReference, now);
            _transactionService.Create(null, platformWallet.PlatformWalletId, booking, TransactionType.PlatformCommission, quote.PlatformCommissionAmount, PaymentMethod.Wallet, bookingReference, now);

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

    /// <summary>
    /// Cancels one of the authenticated commuter's bookings.
    /// Only the booking's renter or an admin may cancel it.
    /// </summary>
    [HttpPost("cancel")]
    [ProducesResponseType(typeof(CancelBookingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CancelBookingResponse>> CancelBooking([FromBody] CancelBookingRequest request)
    {
        try
        {
            var user = await _currentUser.GetCurrentUserAsync();

            if (user == null)
            {
                await _accessLogService.LogAsync(User, "CancelBooking", false, "User not found");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            var booking = await _context.Bookings
                .Include(b => b.ParkingSpot)
                .FirstOrDefaultAsync(b => b.BookingId == request.BookingId);

            if (booking == null)
            {
                await _accessLogService.LogAsync(User, "CancelBooking", false, $"Booking not found (bookingId={request.BookingId})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Booking not found"
                });
            }

            if (booking.RenterId != user.UserId && user.UserType != UserType.Admin)
            {
                await _accessLogService.LogAsync(User, "CancelBooking", false, $"Not authorized (bookingId={request.BookingId})", request.BookingId);
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "You are not authorized to cancel this booking"
                });
            }

            if (booking.BookingStatus == BookingStatus.Cancelled)
            {
                await _accessLogService.LogAsync(User, "CancelBooking", false, $"Already cancelled (bookingId={request.BookingId})");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Booking is already cancelled"
                });
            }

            if (booking.BookingStatus == BookingStatus.Completed)
            {
                await _accessLogService.LogAsync(User, "CancelBooking", false, $"Cannot cancel completed booking (bookingId={request.BookingId})");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Cannot cancel a completed booking"
                });
            }

            var now = DateTime.UtcNow;
            var refundAmount = 0m;

            // Only bookings that were actually paid have a ledger to reverse (legacy pending
            // bookings were created without payment and are simply cancelled).
            var hasPaidBooking = await _context.Transactions
                .AsNoTracking()
                .AnyAsync(transaction =>
                    transaction.BookingId == booking.BookingId &&
                    transaction.TransactionType == TransactionType.Payment);

            if (hasPaidBooking)
            {
                await using var dbTransaction = await _context.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable);

                try
                {
                    // Lock the renter and owner wallets plus the platform wallet so concurrent
                    // cancellations/confirmations cannot double-spend or corrupt balances.
                    var lockedWallets = await _context.Wallets
                        .FromSqlInterpolated($"SELECT * FROM [Wallets] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] IN ({booking.RenterId}, {booking.ParkingSpot.OwnerId}) AND [Status] = {WalletStatus.Active.ToString()}")
                        .OrderBy(wallet => wallet.WalletId)
                        .ToListAsync();

                    var renterWallet = lockedWallets.FirstOrDefault(wallet => wallet.UserId == booking.RenterId);
                    var ownerWallet = lockedWallets.FirstOrDefault(wallet => wallet.UserId == booking.ParkingSpot.OwnerId);

                    var platformWallet = await _context.PlatformWallets
                        .AsNoTracking()
                        .OrderBy(item => item.PlatformWalletId)
                        .FirstOrDefaultAsync();

                    if (renterWallet == null || ownerWallet == null || platformWallet == null)
                    {
                        await dbTransaction.RollbackAsync();
                        return BadRequest(new ErrorResponse
                        {
                            Code = StatusCodes.Status400BadRequest,
                            Success = false,
                            Message = "An active renter, owner, and platform wallet are required to process the refund."
                        });
                    }

                    refundAmount = booking.TotalAmount;
                    var ownerPayout = booking.OwnerPayoutAmount;
                    var commission = booking.PlatformCommissionAmount;

                    // 1) Refund the renter's wallet balance.
                    _walletService.Refund(renterWallet, refundAmount, now);

                    // 2) Release the owner's held payout (never paid out for a cancelled booking).
                    _walletService.ReleaseHold(ownerWallet, ownerPayout, now);

                    // 3) Atomically reverse the commission credited to the platform wallet at confirmation.
                    await _walletService.ApplyRefundToPlatformAsync(platformWallet, commission, now, CancellationToken.None);

                    booking.BookingStatus = BookingStatus.Cancelled;
                    booking.CancellationReason = request.CancellationReason;
                    booking.CancelledAt = now;
                    booking.RefundAmount = refundAmount;
                    booking.UpdatedAt = now;

                    // Write the refund ledger rows: renter + refund, owner & platform reversals.
                    _transactionService.Create(renterWallet.WalletId, null, booking, TransactionType.Refund, refundAmount, PaymentMethod.Wallet, booking.BookingReference, now);
                    _transactionService.Create(ownerWallet.WalletId, null, booking, TransactionType.Refund, -ownerPayout, PaymentMethod.Wallet, booking.BookingReference, now);
                    _transactionService.Create(null, platformWallet.PlatformWalletId, booking, TransactionType.Refund, -commission, PaymentMethod.Wallet, booking.BookingReference, now);

                    await _context.SaveChangesAsync();
                    await dbTransaction.CommitAsync();
                }
                catch
                {
                    await dbTransaction.RollbackAsync();
                    throw;
                }
            }
            else
            {
                // Booking was never paid (e.g. legacy pending booking): just cancel it.
                booking.BookingStatus = BookingStatus.Cancelled;
                booking.CancellationReason = request.CancellationReason;
                booking.CancelledAt = now;
                booking.UpdatedAt = now;
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation(
                "Booking cancelled. BookingId={BookingId}, UserId={UserId}, Reason={Reason}, RefundAmount={RefundAmount}",
                booking.BookingId, user.UserId, request.CancellationReason, refundAmount);

            await _accessLogService.LogAsync(User, "CancelBooking", true, $"BookingId={booking.BookingId}; RefundAmount={refundAmount}", booking.BookingId);

            return Ok(new CancelBookingResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = refundAmount > 0
                    ? $"Booking cancelled and RM {refundAmount:0.00} refunded to your wallet."
                    : "Booking cancelled successfully.",
                BookingId = booking.BookingId,
                BookingStatus = booking.BookingStatus.ToString(),
                CancelledAt = booking.CancelledAt,
                RefundAmount = refundAmount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling booking {BookingId}", request.BookingId);
            await _accessLogService.LogAsync(User, "CancelBooking", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while cancelling the booking"
            });
        }
    }

    /// <summary>
    /// Builds the API's standard error response.
    /// </summary>
    private static ErrorResponse Error(int code, string message) => new() { Code = code, Success = false, Message = message };

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
    /// Generates a unique booking reference using a time-ordered UUIDv7.
    /// Collision-free without a DB check; e.g. PJ-0193F2C4A0B1C2D3E4F5A6B7C8D9E0F1.
    /// </summary>
    private static string GenerateBookingReference() => $"PJ-{Guid.CreateVersion7():N}";

    /// <summary>
    /// Take the request's status string and parse it to the BookingStatus enum.
    /// Returns true when parsing succeeds (or the value is blank), false otherwise.
    /// </summary>
    private static bool TryParseBookingStatus(string? status, out BookingStatus? bookingStatus)
    {
        bookingStatus = null;
        if (string.IsNullOrWhiteSpace(status))
        {
            return true;
        }

        var matchingStatusName = Enum.GetNames<BookingStatus>()
            .FirstOrDefault(candidate => string.Equals(
                candidate,
                status.Trim(),
                StringComparison.OrdinalIgnoreCase));

        if (matchingStatusName == null)
        {
            return false;
        }

        bookingStatus = Enum.Parse<BookingStatus>(matchingStatusName);
        return true;
    }

    /// <summary>
    /// Take the request's date string (YYYY-MM-DD) and parse it to a DateOnly.
    /// Returns true when parsing succeeds (or the value is blank), false otherwise.
    /// </summary>
    private static bool TryParseIsoDate(string? date, out DateOnly? dateOnly)
    {
        dateOnly = null;
        if (string.IsNullOrWhiteSpace(date))
        {
            return true;
        }

        if (!DateOnly.TryParseExact(
                date.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        dateOnly = parsed;
        return true;
    }

    private static BookingResponseDTO MapToBookingResponseDTO(Booking booking)
    {
        return new BookingResponseDTO
        {
            BookingId = booking.BookingId,
            BookingReference = booking.BookingReference,
            RenterId = booking.RenterId,
            ParkingSpotId = booking.ParkingSpotId,
            ParkingLabel = booking.ParkingSpot?.ParkingLabel,
            VehicleId = booking.VehicleId,
            StartDate = booking.StartDate,
            EndDate = booking.EndDate,
            BookingStatus = booking.BookingStatus.ToString(),
            TotalAmount = booking.TotalAmount,
            CancellationReason = booking.CancellationReason,
            CancelledAt = booking.CancelledAt,
            CreatedAt = booking.CreatedAt
        };
    }

    /// <summary>
    /// Maps a booking to the my-bookings item (booking plus its review eligibility and any
    /// review the current commuter has already submitted for it).
    /// </summary>
    private static BookingHistoryItemDTO MapToBookingHistoryItemDTO(Booking booking)
    {
        var review = booking.Reviews.SingleOrDefault();

        return new BookingHistoryItemDTO
        {
            Booking = MapToBookingResponseDTO(booking),
            CanReview = booking.BookingStatus == BookingStatus.Completed && review == null,
            Review = review == null ? null : MapToReviewDTO(review, booking)
        };
    }

    private static ReviewDTO MapToReviewDTO(Review review, Booking booking)
    {
        var firstName = review.Reviewer.FirstName?.Trim();
        var lastInitial = string.IsNullOrWhiteSpace(review.Reviewer.LastName)
            ? string.Empty
            : $" {char.ToUpperInvariant(review.Reviewer.LastName.Trim()[0])}.";

        return new ReviewDTO
        {
            ReviewId = review.ReviewId,
            ParkingSpotId = review.ParkingSpotId,
            Rating = review.Rating,
            Comment = review.Comment,
            ReviewerDisplayName = string.IsNullOrWhiteSpace(firstName)
                ? "ParkJom commuter"
                : firstName + lastInitial,
            IsVerifiedBooking = booking.RenterId == review.ReviewerId &&
                                booking.BookingStatus == BookingStatus.Completed,
            OwnerReply = review.OwnerReply,
            OwnerReplyAt = review.OwnerReplyAt,
            CreatedAt = review.CreatedAt,
            UpdatedAt = review.UpdatedAt
        };
    }
}
