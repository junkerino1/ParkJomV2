using System.Globalization;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Services.Copilot;

public sealed record CopilotDateRangeAvailability(
    bool IsAvailable,
    string? ErrorReason,
    List<DateOnly> UnavailableDates,
    List<DateOnly> NextAvailableDates,
    int AvailableDayCount,
    BookingRateType RateType,
    decimal RatePerDay,
    decimal? EstimatedSubtotal);

/// <summary>
/// Shared availability, booking-conflict, and pricing evaluator for Copilot tools.
/// Mirrors <c>BookingCheckoutController</c> rules so Copilot never recommends dates or rates checkout rejects.
/// </summary>
public static class CopilotAvailabilityHelper
{
    public const int DefaultLookaheadDays = 30;
    public const int MaxBookingDays = 31;

    public static DateOnly GetMinimumBookingDate(DateOnly todayInKl) => todayInKl.AddDays(1);

    /// <summary>Converts a booking end timestamp to the exclusive date boundary used by checkout.</summary>
    public static DateOnly GetBookingEndExclusiveDate(DateTime bookingEnd)
    {
        var endDate = DateOnly.FromDateTime(bookingEnd);
        return bookingEnd.TimeOfDay == TimeSpan.Zero ? endDate : endDate.AddDays(1);
    }

    /// <summary>Returns merged availability intervals for one Malaysia calendar date.</summary>
    public static List<(TimeOnly From, TimeOnly To)> GetCoverageForDate(IEnumerable<Availability> rules, DateOnly date)
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

    /// <summary>Expands Confirmed and Active bookings into occupied calendar dates within [windowStart, windowEndExclusive).</summary>
    public static HashSet<DateOnly> BuildBookedDateSet(
        IEnumerable<Booking> bookings,
        DateOnly windowStart,
        DateOnly windowEndExclusive)
    {
        var booked = new HashSet<DateOnly>();
        if (windowEndExclusive <= windowStart) return booked;

        foreach (var booking in bookings)
        {
            if (booking.BookingStatus is not (BookingStatus.Confirmed or BookingStatus.Active))
                continue;

            var bookingStart = DateOnly.FromDateTime(booking.StartDate);
            var bookingEndExclusive = GetBookingEndExclusiveDate(booking.EndDate);
            var overlapStart = bookingStart > windowStart ? bookingStart : windowStart;
            var overlapEnd = bookingEndExclusive < windowEndExclusive ? bookingEndExclusive : windowEndExclusive;

            for (var d = overlapStart; d < overlapEnd; d = d.AddDays(1))
                booked.Add(d);
        }

        return booked;
    }

    /// <summary>Returns up to <paramref name="maxDates"/> bookable dates starting on or after <paramref name="fromDate"/>.</summary>
    public static List<DateOnly> GetUpcomingAvailableDates(
        IEnumerable<Availability> rules,
        IReadOnlySet<DateOnly> bookedDates,
        DateOnly fromDate,
        int maxDates = 7,
        int lookaheadDays = DefaultLookaheadDays)
    {
        var ruleList = rules as ICollection<Availability> ?? rules.ToList();
        var results = new List<DateOnly>();
        if (maxDates <= 0 || lookaheadDays <= 0 || ruleList.Count == 0) return results;

        for (var i = 0; i < lookaheadDays && results.Count < maxDates; i++)
        {
            var date = fromDate.AddDays(i);
            if (bookedDates.Contains(date)) continue;
            if (GetCoverageForDate(ruleList, date).Count > 0)
                results.Add(date);
        }

        return results;
    }

    /// <summary>
    /// Platform pricing rules: 1–9d daily rate, 10–20d 10% discounted daily, 21+d prorated monthly rate.
    /// </summary>
    public static bool TryCalculatePrice(
        ParkingSpot spot,
        int days,
        out BookingRateType rateType,
        out decimal ratePerDay,
        out decimal subtotal,
        out string error)
    {
        rateType = BookingRateType.Daily;
        ratePerDay = 0m;
        subtotal = 0m;
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
            error = "This parking spot does not have a rate configured for the requested duration.";
            return false;
        }

        subtotal = RoundMoney(subtotal);
        ratePerDay = RoundMoney(ratePerDay);
        return true;
    }

    /// <summary>
    /// Evaluates whether every date in [<paramref name="start"/>..<paramref name="end"/>] is bookable and priced.
    /// </summary>
    public static CopilotDateRangeAvailability EvaluateDateRange(
        ParkingSpot spot,
        IEnumerable<Booking> blockingBookings,
        DateOnly start,
        DateOnly end,
        DateOnly todayInKl)
    {
        var minimumBookingDate = GetMinimumBookingDate(todayInKl);
        var rules = spot.ParkingAvailabilities as ICollection<Availability> ?? spot.ParkingAvailabilities.ToList();
        var bookingList = blockingBookings as ICollection<Booking> ?? blockingBookings.ToList();

        var searchFrom = start > minimumBookingDate ? start : minimumBookingDate;
        var windowEnd = searchFrom.AddDays(DefaultLookaheadDays);
        if (end >= windowEnd) windowEnd = end.AddDays(1);
        var windowStart = start < minimumBookingDate ? start : minimumBookingDate;

        var bookedDates = BuildBookedDateSet(bookingList, windowStart, windowEnd);
        var upcomingDates = GetUpcomingAvailableDates(rules, bookedDates, searchFrom);

        if (end < start)
        {
            return new CopilotDateRangeAvailability(
                false,
                "End date must be on or after start date.",
                new List<DateOnly>(),
                upcomingDates,
                0,
                BookingRateType.Daily,
                0m,
                null);
        }

        var totalSpanDays = end.DayNumber - start.DayNumber + 1;
        if (totalSpanDays > MaxBookingDays)
        {
            return new CopilotDateRangeAvailability(
                false,
                $"Bookings are limited to {MaxBookingDays} days maximum.",
                new List<DateOnly>(),
                upcomingDates,
                0,
                BookingRateType.Daily,
                0m,
                null);
        }

        if (start < minimumBookingDate)
        {
            var nextText = upcomingDates.Count > 0
                ? $" Earliest open date is {upcomingDates[0].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}."
                : string.Empty;
            return new CopilotDateRangeAvailability(
                false,
                $"Private bay bookings must start tomorrow ({minimumBookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}) or later in Malaysia time; public drive-in parking lots can be used today.{nextText}",
                new List<DateOnly>(),
                upcomingDates,
                0,
                BookingRateType.Daily,
                0m,
                null);
        }

        var unavailableDates = new List<DateOnly>();
        var availableDayCount = 0;

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (bookedDates.Contains(date) || GetCoverageForDate(rules, date).Count == 0)
            {
                unavailableDates.Add(date);
            }
            else
            {
                availableDayCount++;
            }
        }

        if (unavailableDates.Count > 0)
        {
            var previewDates = string.Join(", ", unavailableDates.Take(5).Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            var nextHint = upcomingDates.Count > 0
                ? $" Next open date: {upcomingDates[0].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}."
                : " No open dates found in the next 30 days.";

            return new CopilotDateRangeAvailability(
                false,
                $"Bay {spot.ParkingSpotId} is unavailable on {previewDates}.{nextHint}",
                unavailableDates,
                upcomingDates,
                availableDayCount,
                BookingRateType.Daily,
                0m,
                null);
        }

        if (!TryCalculatePrice(spot, availableDayCount, out var rateType, out var ratePerDay, out var subtotal, out var priceError))
        {
            return new CopilotDateRangeAvailability(
                false,
                priceError,
                new List<DateOnly>(),
                upcomingDates,
                availableDayCount,
                rateType,
                0m,
                null);
        }

        return new CopilotDateRangeAvailability(
            true,
            null,
            new List<DateOnly>(),
            upcomingDates,
            availableDayCount,
            rateType,
            ratePerDay,
            subtotal);
    }

    private static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
