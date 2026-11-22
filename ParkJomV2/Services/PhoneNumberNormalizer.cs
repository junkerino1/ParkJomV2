namespace ParkJomV2.Services;

public static class PhoneNumberNormalizer
{
    /// <summary>Uses the same canonical 60-prefixed digits for profiles and phone verification.</summary>
    public static string? NormalizeMalaysian(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = new string(value.Where(character => character is >= '0' and <= '9').ToArray());
        if (digits.StartsWith("0", StringComparison.Ordinal)) digits = "60" + digits[1..];
        if (digits.Length is < 10 or > 12 || !digits.StartsWith("60", StringComparison.Ordinal)) return null;
        return digits;
    }
}
