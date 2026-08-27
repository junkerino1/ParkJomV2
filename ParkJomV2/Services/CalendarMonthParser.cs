using System.Globalization;

namespace ParkJomV2.Services;

public static class CalendarMonthParser
{
    /// <summary>
    /// Parses an ISO calendar month and returns its inclusive start and exclusive end dates.
    /// </summary>
    public static bool TryParse(
        string? month,
        out DateOnly monthStart,
        out DateOnly monthEndExclusive)
    {
        monthStart = default;
        monthEndExclusive = default;

        var normalizedMonth = month?.Trim();
        if (normalizedMonth?.Length != 7 ||
            !DateOnly.TryParseExact(
                $"{normalizedMonth}-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out monthStart) ||
            (monthStart.Year == DateOnly.MaxValue.Year && monthStart.Month == 12))
        {
            monthStart = default;
            return false;
        }

        monthEndExclusive = monthStart.AddMonths(1);
        return true;
    }
}
