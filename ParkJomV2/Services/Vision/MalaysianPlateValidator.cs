using System.Text;
using System.Text.RegularExpressions;

namespace ParkJomV2.Services.Vision;

/// <summary>
/// Malaysian JPJ license plate syntactic validator, position-aware homoglyph normalizer,
/// and weighted Levenshtein similarity engine for ALPR barrier verification.
/// </summary>
public static partial class MalaysianPlateValidator
{
    [GeneratedRegex(@"[^A-Z0-9]", RegexOptions.Compiled)]
    private static partial Regex NonAlphanumericRegex();

    // Standard Malaysian JPJ patterns:
    // 1. Standard Peninsular/Sabah/Sarawak: 1-3 letters + 1-4 digits + optional suffix letter (e.g. VEE8492, WXY1234A, SAA1234B, QAA1234)
    // 2. Special series: PUTRAJAYA / PROTON / SUWA / G1M / EV + 1-4 digits
    [GeneratedRegex(@"^(?<prefix>PUTRAJAYA|PROTON|PERODUA|MALAYSIA|SUHAKAM|PATRIOT|RIMAU|RAPID|LOTUS|BAMBEE|TIGER|G1M|USM|UUM|UTM|UKM|UiTM|IIUM|UM|[A-Z]{1,3})(?<digits>\d{1,4})(?<suffix>[A-Z]?)$", RegexOptions.Compiled)]
    private static partial Regex JpjStandardRegex();

    /// <summary>
    /// Normalizes a raw OCR plate string using position-aware homoglyph correction
    /// (e.g., 'O'/'Q' inside the numeric group becomes '0', '8' inside the letter prefix becomes 'B').
    /// </summary>
    public static (bool IsValid, string CompactPlate, string FormattedPlate, string Region) NormalizeAndValidate(string? rawPlate)
    {
        if (string.IsNullOrWhiteSpace(rawPlate))
            return (false, string.Empty, string.Empty, "Unknown");

        var cleaned = NonAlphanumericRegex().Replace(rawPlate.Trim().ToUpperInvariant(), "");
        if (cleaned.Length < 2)
            return (false, cleaned, cleaned, "Unknown");

        // Check if it already matches JPJ format directly
        var directMatch = JpjStandardRegex().Match(cleaned);
        if (directMatch.Success)
        {
            var prefix = directMatch.Groups["prefix"].Value;
            var digits = directMatch.Groups["digits"].Value.TrimStart('0');
            if (string.IsNullOrEmpty(digits)) digits = "0";
            var suffix = directMatch.Groups["suffix"].Value;
            var compact = $"{prefix}{digits}{suffix}";
            var formatted = string.IsNullOrEmpty(suffix)
                ? $"{prefix} {digits}"
                : $"{prefix} {digits} {suffix}";
            return (true, compact, formatted, ResolveJpjRegion(prefix));
        }

        // Position-aware homoglyph repair:
        // Typical Malaysian plate has 1-3 leading letters, 1-4 middle digits, and 0-1 trailing letter.
        var repaired = RepairHomoglyphs(cleaned);
        var repairedMatch = JpjStandardRegex().Match(repaired);
        if (repairedMatch.Success)
        {
            var prefix = repairedMatch.Groups["prefix"].Value;
            var digits = repairedMatch.Groups["digits"].Value;
            var suffix = repairedMatch.Groups["suffix"].Value;
            var compact = $"{prefix}{digits}{suffix}";
            var formatted = string.IsNullOrEmpty(suffix)
                ? $"{prefix} {digits}"
                : $"{prefix} {digits} {suffix}";
            return (true, compact, formatted, ResolveJpjRegion(prefix));
        }

        return (false, cleaned, cleaned, "Non-Standard / Custom");
    }

    private static string RepairHomoglyphs(string cleaned)
    {
        if (cleaned.Length < 2 || cleaned.Length > 9)
            return cleaned;

        var chars = cleaned.ToCharArray();

        // First char in standard Malaysian plates is always a letter
        chars[0] = DigitToLetter(chars[0]);

        // Find transition into numeric core (positions 1..3)
        int firstDigitIdx = -1;
        for (int i = 1; i < chars.Length; i++)
        {
            if (char.IsDigit(chars[i]))
            {
                firstDigitIdx = i;
                break;
            }
        }

        if (firstDigitIdx == -1 && chars.Length >= 4)
        {
            // If OCR read all letters (e.g. "VEEB49Z"), assume prefix is up to 3 chars
            firstDigitIdx = Math.Min(3, chars.Length - 1);
        }

        if (firstDigitIdx > 0)
        {
            for (int i = 0; i < firstDigitIdx && i < 3; i++)
                chars[i] = DigitToLetter(chars[i]);

            int lastIdx = chars.Length - 1;
            for (int i = firstDigitIdx; i <= lastIdx; i++)
            {
                // Keep final char as letter if it's already clearly a non-homoglyph letter
                if (i == lastIdx && char.IsLetter(chars[i]) && !IsAmbiguousLetter(chars[i]))
                    continue;

                chars[i] = LetterToDigit(chars[i]);
            }
        }

        return new string(chars);
    }

    private static bool IsAmbiguousLetter(char c) => c is 'O' or 'Q' or 'I' or 'Z' or 'S' or 'B';

    private static char DigitToLetter(char c) => c switch
    {
        '0' => 'Q',
        '1' => 'I',
        '2' => 'Z',
        '5' => 'S',
        '8' => 'B',
        _ => c
    };

    private static char LetterToDigit(char c) => c switch
    {
        'O' or 'Q' or 'D' => '0',
        'I' or 'L' => '1',
        'Z' => '2',
        'S' => '5',
        'G' => '6',
        'B' => '8',
        _ => c
    };

    public static string ResolveJpjRegion(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return "Malaysia";
        if (prefix.Equals("PUTRAJAYA", StringComparison.OrdinalIgnoreCase) || prefix.StartsWith('F'))
            return "Putrajaya Federal Territory";
        if (prefix.StartsWith("EV", StringComparison.OrdinalIgnoreCase))
            return "Malaysia EV Special Series";
        if (prefix.Length > 3)
            return "Special Commemorative Series";

        return prefix[0] switch
        {
            'V' or 'W' => "Kuala Lumpur Federal Territory",
            'B' => "Selangor",
            'J' => "Johor",
            'P' => "Penang",
            'A' => "Perak",
            'K' => "Kedah",
            'C' => "Pahang",
            'D' => "Kelantan",
            'M' => "Melaka",
            'N' => "Negeri Sembilan",
            'R' => "Perlis",
            'T' => "Terengganu",
            'S' => "Sabah",
            'Q' => "Sarawak",
            'L' => "Labuan",
            'Z' => "Malaysian Armed Forces",
            _ => "Malaysia JPJ"
        };
    }

    /// <summary>
    /// Computes a weighted similarity score in [0.0, 1.0] between expected and detected plates,
    /// where visually ambiguous OCR character pairs (0/O/D/Q, 8/B, 1/I, 5/S, 2/Z) incur a 0.20 cost instead of 1.0.
    /// </summary>
    public static double ComputeSimilarity(string? expectedPlate, string? detectedPlate)
    {
        var a = NonAlphanumericRegex().Replace((expectedPlate ?? "").ToUpperInvariant(), "");
        var b = NonAlphanumericRegex().Replace((detectedPlate ?? "").ToUpperInvariant(), "");

        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return 0.0;
        if (a == b)
            return 1.0;

        int n = a.Length;
        int m = b.Length;
        var dp = new double[n + 1, m + 1];

        for (int i = 0; i <= n; i++) dp[i, 0] = i;
        for (int j = 0; j <= m; j++) dp[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                double subCost = SubstitutionCost(a[i - 1], b[j - 1]);
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1.0, dp[i, j - 1] + 1.0),
                    dp[i - 1, j - 1] + subCost);
            }
        }

        double maxLen = Math.Max(n, m);
        double score = Math.Max(0.0, 1.0 - (dp[n, m] / maxLen));
        return Math.Round(score, 4);
    }

    private static double SubstitutionCost(char c1, char c2)
    {
        if (c1 == c2) return 0.0;
        if (AreHomoglyphs(c1, c2)) return 0.20;
        return 1.0;
    }

    private static bool AreHomoglyphs(char a, char b)
    {
        static string Group(char c) => c switch
        {
            '0' or 'O' or 'Q' or 'D' => "ZERO",
            '1' or 'I' or 'L' or 'T' => "ONE",
            '2' or 'Z' => "TWO",
            '5' or 'S' => "FIVE",
            '6' or 'G' => "SIX",
            '8' or 'B' => "EIGHT",
            'M' or 'W' => "MW",
            'V' or 'U' or 'Y' => "VUY",
            _ => c.ToString()
        };
        return Group(a) == Group(b);
    }
}
