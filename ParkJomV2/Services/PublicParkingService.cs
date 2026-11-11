using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Constants;
using ParkJomV2.Models.Enums;
using Stripe.Checkout;

namespace ParkJomV2.Services;

public class PublicParkingService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly TransactionService _transactionService;
    private readonly ILogger<PublicParkingService> _logger;

    public PublicParkingService(
        ApplicationDbContext context,
        IConfiguration configuration,
        TransactionService transactionService,
        ILogger<PublicParkingService> logger)
    {
        _context = context;
        _configuration = configuration;
        _transactionService = transactionService;
        _logger = logger;
    }

    private static string NormalizePlate(string plate)
    {
        if (string.IsNullOrWhiteSpace(plate)) return string.Empty;
        return string.Concat(plate.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    }

    private static bool IsLotCurrentlyOpen(PublicParkingLot lot, DateTime utcNow)
    {
        if (lot.OperatingType == PublicParkingOperatingType.Unlimited24Hours)
        {
            return true;
        }

        // Malaysia timezone is UTC+8
        var malaysiaTime = utcNow.AddHours(8).TimeOfDay;

        if (TimeSpan.TryParse(lot.OpeningTime, out var open) &&
            TimeSpan.TryParse(lot.ClosingTime, out var close))
        {
            if (open <= close)
            {
                return malaysiaTime >= open && malaysiaTime <= close;
            }
            // Overnight window (e.g. 18:00 to 04:00)
            return malaysiaTime >= open || malaysiaTime <= close;
        }

        return true;
    }

    public async Task<List<PublicParkingLotDto>> GetLotsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var lots = await _context.PublicParkingLots
            .AsNoTracking()
            .Include(l => l.Owner)
            .Include(l => l.Sessions.Where(s =>
                s.Status == PublicParkingSessionStatus.Parked ||
                s.Status == PublicParkingSessionStatus.PaymentPending ||
                s.Status == PublicParkingSessionStatus.Paid))
            .Where(l => l.IsActive)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        return lots.Select(l =>
        {
            var occupied = l.Sessions.Count;
            var available = Math.Max(0, l.TotalCapacity - occupied);
            return new PublicParkingLotDto
            {
                PublicParkingLotId = l.PublicParkingLotId,
                OwnerId = l.OwnerId,
                OwnerName = $"{l.Owner?.FirstName} {l.Owner?.LastName}".Trim(),
                PropertyId = l.PropertyId,
                LotName = l.LotName,
                Address = l.Address,
                Latitude = l.Latitude,
                Longitude = l.Longitude,
                TotalCapacity = l.TotalCapacity,
                OccupiedSpaces = occupied,
                AvailableSpaces = available,
                OperatingType = l.OperatingType,
                OpeningTime = l.OpeningTime,
                ClosingTime = l.ClosingTime,
                IsCurrentlyOpen = IsLotCurrentlyOpen(l, now),
                HourlyRate = l.HourlyRate,
                DailyCapRate = l.DailyCapRate,
                FlatRatePerEntry = l.FlatRatePerEntry,
                OvernightSurcharge = l.OvernightSurcharge,
                InitialGraceMinutes = l.InitialGraceMinutes,
                ExitGraceMinutes = l.ExitGraceMinutes,
                IsActive = l.IsActive,
                Description = l.Description,
                CreatedAt = l.CreatedAt
            };
        }).ToList();
    }

    public async Task<PublicParkingLotDto?> GetLotByIdAsync(int lotId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var lot = await _context.PublicParkingLots
            .AsNoTracking()
            .Include(l => l.Owner)
            .Include(l => l.Sessions.Where(s =>
                s.Status == PublicParkingSessionStatus.Parked ||
                s.Status == PublicParkingSessionStatus.PaymentPending ||
                s.Status == PublicParkingSessionStatus.Paid))
            .FirstOrDefaultAsync(l => l.PublicParkingLotId == lotId, ct);

        if (lot == null) return null;

        var occupied = lot.Sessions.Count;
        var available = Math.Max(0, lot.TotalCapacity - occupied);

        return new PublicParkingLotDto
        {
            PublicParkingLotId = lot.PublicParkingLotId,
            OwnerId = lot.OwnerId,
            OwnerName = $"{lot.Owner?.FirstName} {lot.Owner?.LastName}".Trim(),
            PropertyId = lot.PropertyId,
            LotName = lot.LotName,
            Address = lot.Address,
            Latitude = lot.Latitude,
            Longitude = lot.Longitude,
            TotalCapacity = lot.TotalCapacity,
            OccupiedSpaces = occupied,
            AvailableSpaces = available,
            OperatingType = lot.OperatingType,
            OpeningTime = lot.OpeningTime,
            ClosingTime = lot.ClosingTime,
            IsCurrentlyOpen = IsLotCurrentlyOpen(lot, now),
            HourlyRate = lot.HourlyRate,
            DailyCapRate = lot.DailyCapRate,
            FlatRatePerEntry = lot.FlatRatePerEntry,
            OvernightSurcharge = lot.OvernightSurcharge,
            InitialGraceMinutes = lot.InitialGraceMinutes,
            ExitGraceMinutes = lot.ExitGraceMinutes,
            IsActive = lot.IsActive,
            Description = lot.Description,
            CreatedAt = lot.CreatedAt
        };
    }

    public async Task<PublicParkingLotDto> CreateLotAsync(int ownerId, CreatePublicParkingLotRequest req, CancellationToken ct = default)
    {
        var lot = new PublicParkingLot
        {
            OwnerId = ownerId,
            PropertyId = req.PropertyId,
            LotName = req.LotName.Trim(),
            Address = req.Address.Trim(),
            Latitude = req.Latitude,
            Longitude = req.Longitude,
            TotalCapacity = req.TotalCapacity,
            OperatingType = req.OperatingType,
            OpeningTime = req.OpeningTime?.Trim(),
            ClosingTime = req.ClosingTime?.Trim(),
            HourlyRate = req.HourlyRate,
            DailyCapRate = req.DailyCapRate,
            FlatRatePerEntry = req.FlatRatePerEntry,
            OvernightSurcharge = req.OvernightSurcharge,
            InitialGraceMinutes = req.InitialGraceMinutes,
            ExitGraceMinutes = req.ExitGraceMinutes,
            IsActive = true,
            Description = req.Description?.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.PublicParkingLots.Add(lot);
        await _context.SaveChangesAsync(ct);

        return (await GetLotByIdAsync(lot.PublicParkingLotId, ct))!;
    }

    public async Task<PublicParkingLotDto?> UpdateLotAsync(int ownerId, int lotId, UpdatePublicParkingLotRequest req, CancellationToken ct = default)
    {
        var lot = await _context.PublicParkingLots.FirstOrDefaultAsync(l => l.PublicParkingLotId == lotId && l.OwnerId == ownerId, ct);
        if (lot == null) return null;

        lot.LotName = req.LotName.Trim();
        lot.Address = req.Address.Trim();
        lot.TotalCapacity = req.TotalCapacity;
        lot.OperatingType = req.OperatingType;
        lot.OpeningTime = req.OpeningTime?.Trim();
        lot.ClosingTime = req.ClosingTime?.Trim();
        lot.HourlyRate = req.HourlyRate;
        lot.DailyCapRate = req.DailyCapRate;
        lot.FlatRatePerEntry = req.FlatRatePerEntry;
        lot.OvernightSurcharge = req.OvernightSurcharge;
        lot.InitialGraceMinutes = req.InitialGraceMinutes;
        lot.ExitGraceMinutes = req.ExitGraceMinutes;
        lot.IsActive = req.IsActive;
        lot.Description = req.Description?.Trim();
        lot.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
        return await GetLotByIdAsync(lotId, ct);
    }

    public async Task<PublicParkingSessionDto> RecordEntryAsync(PublicParkingEntryRequest req, int? userId = null, CancellationToken ct = default)
    {
        var plate = NormalizePlate(req.CarPlateNumber);
        if (string.IsNullOrWhiteSpace(plate))
        {
            throw new ArgumentException("Car plate number is required.");
        }

        var lot = await _context.PublicParkingLots
            .Include(l => l.Sessions.Where(s =>
                s.Status == PublicParkingSessionStatus.Parked ||
                s.Status == PublicParkingSessionStatus.PaymentPending ||
                s.Status == PublicParkingSessionStatus.Paid))
            .FirstOrDefaultAsync(l => l.PublicParkingLotId == req.PublicParkingLotId, ct);

        if (lot == null || !lot.IsActive)
        {
            throw new InvalidOperationException("Public parking lot not found or inactive.");
        }

        var now = DateTime.UtcNow;
        if (!IsLotCurrentlyOpen(lot, now))
        {
            throw new InvalidOperationException($"Parking lot is currently closed. Operating hours: {lot.OpeningTime} - {lot.ClosingTime}.");
        }

        // Check if car is already parked in this lot
        var existing = lot.Sessions.FirstOrDefault(s => s.CarPlateNumber == plate &&
            (s.Status == PublicParkingSessionStatus.Parked || s.Status == PublicParkingSessionStatus.PaymentPending));
        if (existing != null)
        {
            return MapToDto(existing, lot, now);
        }

        // Check capacity
        if (lot.Sessions.Count >= lot.TotalCapacity)
        {
            throw new InvalidOperationException("This public parking lot is currently FULL. Please wait for an available space.");
        }

        // Auto-match user by vehicle plate if not provided
        if (!userId.HasValue)
        {
            var vehicle = await _context.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.NumberPlate == plate, ct);
            if (vehicle != null)
            {
                userId = vehicle.UserId;
            }
        }

        var sessionCode = $"PUB-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        var session = new PublicParkingSession
        {
            SessionCode = sessionCode,
            PublicParkingLotId = lot.PublicParkingLotId,
            CarPlateNumber = plate,
            UserId = userId,
            EntryTimeUtc = now,
            HourlyRateSnapshot = lot.HourlyRate,
            Status = PublicParkingSessionStatus.Parked,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.PublicParkingSessions.Add(session);
        await _context.SaveChangesAsync(ct);

        return MapToDto(session, lot, now);
    }

    public async Task<PublicParkingSessionDto?> LookupActiveSessionAsync(string plateOrCode, CancellationToken ct = default)
    {
        var cleaned = plateOrCode.Trim();
        var normalizedPlate = NormalizePlate(cleaned);

        var now = DateTime.UtcNow;
        var session = await _context.PublicParkingSessions
            .Include(s => s.ParkingLot)
            .Where(s => (s.SessionCode == cleaned || s.CarPlateNumber == normalizedPlate) &&
                        (s.Status == PublicParkingSessionStatus.Parked ||
                         s.Status == PublicParkingSessionStatus.PaymentPending ||
                         s.Status == PublicParkingSessionStatus.Paid))
            .OrderByDescending(s => s.EntryTimeUtc)
            .FirstOrDefaultAsync(ct);

        if (session == null) return null;

        return MapToDto(session, session.ParkingLot, now);
    }

    public async Task<PublicParkingSessionDto> PayWithWalletAsync(int userId, string sessionCode, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var session = await _context.PublicParkingSessions
            .Include(s => s.ParkingLot)
                .ThenInclude(l => l.Owner)
            .FirstOrDefaultAsync(s => s.SessionCode == sessionCode, ct);

        if (session == null)
        {
            throw new InvalidOperationException("Parking session not found.");
        }

        if (session.Status == PublicParkingSessionStatus.Paid || session.Status == PublicParkingSessionStatus.Exited)
        {
            return MapToDto(session, session.ParkingLot, now);
        }

        var calculation = CalculateAmount(session, session.ParkingLot, now);
        var amountDue = calculation.TotalAmount;

        // If free within grace period, mark paid immediately without balance deduction
        if (amountDue <= 0)
        {
            session.Status = PublicParkingSessionStatus.Paid;
            session.PaidAtUtc = now;
            session.TotalAmount = 0m;
            session.PaymentMethod = "FreeGrace";
            session.UpdatedAt = now;
            await _context.SaveChangesAsync(ct);
            return MapToDto(session, session.ParkingLot, now);
        }

        var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet == null)
        {
            throw new InvalidOperationException("Wallet not found.");
        }

        if (wallet.Balance < amountDue)
        {
            throw new InvalidOperationException($"Insufficient wallet balance. Balance: RM{wallet.Balance:0.00}, Required: RM{amountDue:0.00}. Please top up your wallet or pay via Stripe.");
        }

        await using var dbTx = await _context.Database.BeginTransactionAsync(ct);

        // Deduct from commuter wallet
        wallet.Balance -= amountDue;
        wallet.UpdatedAt = now;

        // Commission calculation (10%)
        var commission = Math.Round(amountDue * (PlatformConstants.CommissionRate / 100m), 2);
        var ownerPayout = amountDue - commission;

        // Debit commuter transaction
        var commuterTx = _transactionService.Create(
            wallet.WalletId,
            null,
            booking: null,
            TransactionType.Payment,
            amountDue,
            PaymentMethod.Wallet,
            $"Public Parking - {session.CarPlateNumber} ({session.ParkingLot.LotName})",
            now);

        // Credit owner payout if owner has wallet
        var ownerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == session.ParkingLot.OwnerId, ct);
        if (ownerWallet != null)
        {
            ownerWallet.Balance += ownerPayout;
            ownerWallet.UpdatedAt = now;
            _transactionService.Create(
                ownerWallet.WalletId,
                null,
                booking: null,
                TransactionType.OwnerPayout,
                ownerPayout,
                PaymentMethod.Wallet,
                $"Public Parking Earnings - {session.CarPlateNumber}",
                now);
        }

        // Credit platform wallet commission
        var platformWallet = await _context.PlatformWallets.FirstOrDefaultAsync(ct);
        if (platformWallet != null)
        {
            platformWallet.Balance += commission;
            platformWallet.UpdatedAt = now;
            _transactionService.Create(
                null,
                platformWallet.PlatformWalletId,
                booking: null,
                TransactionType.PlatformCommission,
                commission,
                PaymentMethod.Wallet,
                $"Public Parking Commission - {session.CarPlateNumber}",
                now);
        }

        session.UserId = userId;
        session.Status = PublicParkingSessionStatus.Paid;
        session.PaidAtUtc = now;
        session.TotalAmount = amountDue;
        session.PaymentMethod = "Wallet";
        session.WalletTransactionId = commuterTx.TransactionId;
        session.UpdatedAt = now;

        await _context.SaveChangesAsync(ct);
        await dbTx.CommitAsync(ct);

        return MapToDto(session, session.ParkingLot, now);
    }

    public async Task<PublicParkingCheckoutStripeResponse> CreateStripeCheckoutAsync(
        string sessionCode,
        string returnTarget = "web",
        int? userId = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var session = await _context.PublicParkingSessions
            .Include(s => s.ParkingLot)
            .FirstOrDefaultAsync(s => s.SessionCode == sessionCode, ct);

        if (session == null)
        {
            throw new InvalidOperationException("Parking session not found.");
        }

        if (session.Status == PublicParkingSessionStatus.Paid || session.Status == PublicParkingSessionStatus.Exited)
        {
            throw new InvalidOperationException("This parking session has already been paid.");
        }

        var calculation = CalculateAmount(session, session.ParkingLot, now);
        var amountDue = calculation.TotalAmount;

        if (amountDue <= 0)
        {
            // Within grace period: directly mark paid
            session.Status = PublicParkingSessionStatus.Paid;
            session.PaidAtUtc = now;
            session.TotalAmount = 0m;
            session.PaymentMethod = "FreeGrace";
            session.UpdatedAt = now;
            await _context.SaveChangesAsync(ct);

            return new PublicParkingCheckoutStripeResponse
            {
                CheckoutUrl = "",
                SessionId = "FREE_GRACE"
            };
        }

        var secretKey = _configuration["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException("Stripe secret key is not configured.");
        }

        Stripe.StripeConfiguration.ApiKey = secretKey;

        var apiBaseUrl = _configuration["ApiBaseUrl"]?.TrimEnd('/')
            ?? "https://parkjom-api-gbgcbycbcjghczgu.malaysiawest-01.azurewebsites.net";

        var returnBase = returnTarget == "native"
            ? (_configuration["AppUrls:NativeReturnUrl"] ?? "parkjom://commuter")
            : (_configuration["AppUrls:WebReturnUrl"] ?? "https://united-perigee-400000.web.app/commuter");

        var successUrl = $"{apiBaseUrl}/api/public-parking/stripe/success?session_id={{CHECKOUT_SESSION_ID}}&sessionCode={session.SessionCode}&returnTarget={returnTarget}";
        var cancelUrl = $"{returnBase}?stripe_status=cancelled";

        var sessionCreateOptions = new SessionCreateOptions
        {
            Mode = "payment",
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            PaymentMethodTypes = new List<string> { "card" },
            Metadata = new Dictionary<string, string>
            {
                ["type"] = "public_parking",
                ["public_parking_session_id"] = session.PublicParkingSessionId.ToString(),
                ["session_code"] = session.SessionCode,
                ["car_plate"] = session.CarPlateNumber,
                ["user_id"] = userId?.ToString() ?? "guest"
            },
            LineItems = new List<SessionLineItemOptions>
            {
                new()
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "myr",
                        UnitAmount = (long)(amountDue * 100),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = $"Parking - {session.ParkingLot.LotName}",
                            Description = $"Plate: {session.CarPlateNumber} ({calculation.DurationMinutes} mins parked)"
                        }
                    }
                }
            }
        };

        var service = new SessionService();
        var stripeSession = await service.CreateAsync(sessionCreateOptions, cancellationToken: ct);

        session.StripeSessionId = stripeSession.Id;
        session.Status = PublicParkingSessionStatus.PaymentPending;
        session.UpdatedAt = now;
        await _context.SaveChangesAsync(ct);

        return new PublicParkingCheckoutStripeResponse
        {
            CheckoutUrl = stripeSession.Url,
            SessionId = stripeSession.Id
        };
    }

    public async Task ProcessStripeCompletedAsync(string stripeSessionId, string? paymentIntentId, CancellationToken ct = default)
    {
        var session = await _context.PublicParkingSessions
            .Include(s => s.ParkingLot)
            .FirstOrDefaultAsync(s => s.StripeSessionId == stripeSessionId, ct);

        if (session == null || session.Status == PublicParkingSessionStatus.Paid)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var calc = CalculateAmount(session, session.ParkingLot, now);

        session.Status = PublicParkingSessionStatus.Paid;
        session.PaidAtUtc = now;
        session.TotalAmount = calc.TotalAmount;
        session.PaymentMethod = "Stripe";
        session.StripePaymentIntentId = paymentIntentId;
        session.UpdatedAt = now;

        await _context.SaveChangesAsync(ct);
    }

    public async Task<PublicParkingSessionDto> RecordExitAsync(PublicParkingExitRequest req, CancellationToken ct = default)
    {
        var plate = NormalizePlate(req.CarPlateNumber);
        var now = DateTime.UtcNow;

        var session = await _context.PublicParkingSessions
            .Include(s => s.ParkingLot)
            .Where(s => s.PublicParkingLotId == req.PublicParkingLotId &&
                        s.CarPlateNumber == plate &&
                        (s.Status == PublicParkingSessionStatus.Parked ||
                         s.Status == PublicParkingSessionStatus.PaymentPending ||
                         s.Status == PublicParkingSessionStatus.Paid))
            .OrderByDescending(s => s.EntryTimeUtc)
            .FirstOrDefaultAsync(ct);

        if (session == null)
        {
            throw new InvalidOperationException($"No active parking session found for vehicle plate {plate}.");
        }

        var lot = session.ParkingLot;
        var duration = (int)Math.Max(0, (now - session.EntryTimeUtc).TotalMinutes);

        // Case 1: Already paid
        if (session.Status == PublicParkingSessionStatus.Paid)
        {
            var minutesSincePaid = session.PaidAtUtc.HasValue
                ? (int)(now - session.PaidAtUtc.Value).TotalMinutes
                : 0;

            if (minutesSincePaid > lot.ExitGraceMinutes)
            {
                // Exceeded exit grace period -> additional hour charge
                var extraMinutes = minutesSincePaid - lot.ExitGraceMinutes;
                var extraHours = (int)Math.Ceiling(extraMinutes / 60.0);
                var extraFee = extraHours * lot.HourlyRate;
                throw new InvalidOperationException($"Exit grace period of {lot.ExitGraceMinutes} minutes expired ({minutesSincePaid} mins elapsed). Additional overstay fee of RM{extraFee:0.00} required.");
            }

            session.Status = PublicParkingSessionStatus.Exited;
            session.ExitTimeUtc = now;
            session.DurationMinutes = duration;
            session.UpdatedAt = now;
            await _context.SaveChangesAsync(ct);

            return MapToDto(session, lot, now);
        }

        // Case 2: Unpaid, but within initial grace period (Free exit)
        if (duration <= lot.InitialGraceMinutes)
        {
            session.Status = PublicParkingSessionStatus.Exited;
            session.ExitTimeUtc = now;
            session.PaidAtUtc = now;
            session.DurationMinutes = duration;
            session.TotalAmount = 0m;
            session.PaymentMethod = "FreeGrace";
            session.UpdatedAt = now;
            await _context.SaveChangesAsync(ct);

            return MapToDto(session, lot, now);
        }

        // Case 3: Unpaid and exceeded initial grace period
        var calc = CalculateAmount(session, lot, now);
        throw new InvalidOperationException($"Payment of RM{calc.TotalAmount:0.00} required before exiting. Parked duration: {calc.DurationMinutes} minutes.");
    }

    public async Task<List<PublicParkingSessionDto>> GetActiveSessionsForOwnerAsync(int ownerId, int? lotId = null, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var query = _context.PublicParkingSessions
            .Include(s => s.ParkingLot)
            .Where(s => s.ParkingLot.OwnerId == ownerId &&
                        (s.Status == PublicParkingSessionStatus.Parked ||
                         s.Status == PublicParkingSessionStatus.PaymentPending ||
                         s.Status == PublicParkingSessionStatus.Paid));

        if (lotId.HasValue)
        {
            query = query.Where(s => s.PublicParkingLotId == lotId.Value);
        }

        var sessions = await query.OrderByDescending(s => s.EntryTimeUtc).ToListAsync(ct);
        return sessions.Select(s => MapToDto(s, s.ParkingLot, now)).ToList();
    }

    private static (int DurationMinutes, decimal TotalAmount, bool CanExitFree, int GraceRemaining, decimal OvernightSurcharge) CalculateAmount(
        PublicParkingSession session,
        PublicParkingLot lot,
        DateTime nowUtc)
    {
        var durationMinutes = (int)Math.Max(1, (nowUtc - session.EntryTimeUtc).TotalMinutes);

        if (durationMinutes <= lot.InitialGraceMinutes)
        {
            var graceRemaining = Math.Max(0, lot.InitialGraceMinutes - durationMinutes);
            return (durationMinutes, 0m, true, graceRemaining, 0m);
        }

        decimal baseAmount = 0m;

        // Owner decision: Flat Rate per Entry vs Hourly Rate
        if (lot.FlatRatePerEntry.HasValue && lot.FlatRatePerEntry.Value > 0)
        {
            baseAmount = lot.FlatRatePerEntry.Value;
        }
        else
        {
            var billableHours = (int)Math.Ceiling(durationMinutes / 60.0);
            if (lot.DailyCapRate.HasValue && lot.DailyCapRate.Value > 0)
            {
                var fullDays = durationMinutes / 1440;
                var remainingMinutes = durationMinutes % 1440;
                var remainingHours = (int)Math.Ceiling(remainingMinutes / 60.0);
                var dailyCharge = Math.Min(remainingHours * lot.HourlyRate, lot.DailyCapRate.Value);
                baseAmount = (fullDays * lot.DailyCapRate.Value) + dailyCharge;
            }
            else
            {
                baseAmount = billableHours * lot.HourlyRate;
            }
        }

        // Overnight Surcharge Check (for Limited Hours lots)
        decimal overnightSurcharge = 0m;
        if (lot.OperatingType == PublicParkingOperatingType.LimitedHours &&
            lot.OvernightSurcharge.HasValue &&
            lot.OvernightSurcharge.Value > 0)
        {
            // Malaysia time UTC+8
            var entryMy = session.EntryTimeUtc.AddHours(8);
            var nowMy = nowUtc.AddHours(8);

            // If parked across days (different calendar day) or current time is outside opening hours
            bool crossedOvernight = nowMy.Date > entryMy.Date;
            if (TimeSpan.TryParse(lot.ClosingTime, out var closeTime) && TimeSpan.TryParse(lot.OpeningTime, out var openTime))
            {
                if (nowMy.TimeOfDay > closeTime || nowMy.TimeOfDay < openTime || crossedOvernight)
                {
                    overnightSurcharge = lot.OvernightSurcharge.Value;
                }
            }
            else if (crossedOvernight || durationMinutes >= 960) // >= 16 hours
            {
                overnightSurcharge = lot.OvernightSurcharge.Value;
            }
        }

        var totalAmount = baseAmount + overnightSurcharge;
        return (durationMinutes, totalAmount, false, 0, overnightSurcharge);
    }

    private static PublicParkingSessionDto MapToDto(PublicParkingSession s, PublicParkingLot lot, DateTime nowUtc)
    {
        var calc = CalculateAmount(s, lot, nowUtc);
        var finalDuration = s.ExitTimeUtc.HasValue ? s.DurationMinutes : calc.DurationMinutes;
        var finalAmount = s.Status == PublicParkingSessionStatus.Paid || s.Status == PublicParkingSessionStatus.Exited
            ? s.TotalAmount
            : calc.TotalAmount;

        return new PublicParkingSessionDto
        {
            PublicParkingSessionId = s.PublicParkingSessionId,
            SessionCode = s.SessionCode,
            PublicParkingLotId = s.PublicParkingLotId,
            LotName = lot.LotName,
            LotAddress = lot.Address,
            CarPlateNumber = s.CarPlateNumber,
            UserId = s.UserId,
            EntryTimeUtc = s.EntryTimeUtc,
            ExitTimeUtc = s.ExitTimeUtc,
            PaidAtUtc = s.PaidAtUtc,
            DurationMinutes = finalDuration,
            HourlyRate = s.HourlyRateSnapshot > 0 ? s.HourlyRateSnapshot : lot.HourlyRate,
            OvernightSurchargeAmount = calc.OvernightSurcharge,
            TotalAmount = finalAmount,
            Status = s.Status,
            PaymentMethod = s.PaymentMethod,
            GraceMinutesRemaining = calc.GraceRemaining,
            CanExitFree = calc.CanExitFree
        };
    }
}
