using System.Globalization;

namespace ParkJomV2.Services;

public static class CalendarMonthParser
{
    /// <summary>
    /// Parses a month string in the format "YYYY-MM" and returns the start and end dates of that month.
    /// Input: 2026-09
    /// Output: monthStart = 2026-09-01, monthEndExclusive = 2026-10-01
    /// Returns true if parsing is successful, false otherwise.
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
