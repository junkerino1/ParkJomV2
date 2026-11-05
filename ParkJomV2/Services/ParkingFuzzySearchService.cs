using System.Text.RegularExpressions;
using ParkJomV2.Models;

namespace ParkJomV2.Services;

public class ParkingSearchResultMatch
{
    public ParkingSpot Spot { get; set; } = null!;
    public int Score { get; set; }
    public string MatchReason { get; set; } = string.Empty;
}

public class ParkingFuzzySearchResult
{
    public List<ParkingSearchResultMatch> Matches { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();
    public string? DidYouMean { get; set; }
}

public static class ParkingFuzzySearchService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "near", "at", "in", "by", "for", "to",
        "condo", "condominium", "residence", "residences", "apartment", "bay",
        "parking", "lot", "station", "stn", "lrt", "mrt", "jalan", "jln"
    };

    /// <summary>
    /// Removes whitespace, hyphens, slashes, and punctuation for compact matching.
    /// E.g. "PV 128" -> "pv128", "pv-18" -> "pv18", "Wangsa Maju" -> "wangsamaju"
    /// </summary>
    public static string NormalizeCompact(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        return Regex.Replace(input.Trim().ToLowerInvariant(), @"[\s\-_,./\\#+]+", "");
    }

    /// <summary>
    /// Tokenizes input into discrete lowercase words.
    /// </summary>
    public static string[] Tokenize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return Array.Empty<string>();
        return Regex.Split(input.Trim().ToLowerInvariant(), @"[\s\-_,./\\#+]+")
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();
    }

    /// <summary>
    /// Computes classic Levenshtein distance between two strings.
    /// </summary>
    public static int LevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
        if (string.IsNullOrEmpty(t)) return s.Length;
        if (s == t) return 0;

        int n = s.Length;
        int m = t.Length;
        int[] prevRow = new int[m + 1];
        int[] currRow = new int[m + 1];

        for (int j = 0; j <= m; j++) prevRow[j] = j;

        for (int i = 1; i <= n; i++)
        {
            currRow[0] = i;
            char sChar = s[i - 1];

            for (int j = 1; j <= m; j++)
            {
                int cost = (sChar == t[j - 1]) ? 0 : 1;
                currRow[j] = Math.Min(
                    Math.Min(currRow[j - 1] + 1, prevRow[j] + 1),
                    prevRow[j - 1] + cost
                );
            }

            Array.Copy(currRow, prevRow, m + 1);
        }

        return prevRow[m];
    }

    /// <summary>
    /// Normalized similarity ratio between 0.0 and 1.0 based on Levenshtein distance.
    /// </summary>
    public static double SimilarityRatio(string s, string t)
    {
        if (string.IsNullOrEmpty(s) && string.IsNullOrEmpty(t)) return 1.0;
        if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(t)) return 0.0;
        int maxLen = Math.Max(s.Length, t.Length);
        if (maxLen == 0) return 1.0;
        int dist = LevenshteinDistance(s, t);
        return Math.Max(0.0, 1.0 - ((double)dist / maxLen));
    }

    /// <summary>
    /// Checks if characters of sub appear in full in sequential order (wildcard match).
    /// </summary>
    public static bool IsSubsequence(string sub, string full)
    {
        if (string.IsNullOrEmpty(sub)) return true;
        if (string.IsNullOrEmpty(full) || sub.Length > full.Length) return false;

        int subIdx = 0;
        for (int fullIdx = 0; fullIdx < full.Length && subIdx < sub.Length; fullIdx++)
        {
            if (sub[subIdx] == full[fullIdx])
            {
                subIdx++;
            }
        }
        return subIdx == sub.Length;
    }

    /// <summary>
    /// Evaluates match quality between a search query and a parking spot.
    /// Returns a score between 0 and 100 with a human-readable match reason.
    /// </summary>
    public static (int Score, string Reason) EvaluateSpot(string rawQuery, ParkingSpot spot)
    {
        if (string.IsNullOrWhiteSpace(rawQuery)) return (100, "All spots");

        string queryTrim = rawQuery.Trim().ToLowerInvariant();
        string queryCompact = NormalizeCompact(rawQuery);
        string[] queryTokens = Tokenize(rawQuery);
        string[] meaningfulTokens = queryTokens.Where(t => !StopWords.Contains(t)).ToArray();
        if (meaningfulTokens.Length == 0) meaningfulTokens = queryTokens;

        string propName = spot.Property?.PropertyName ?? string.Empty;
        string propCompact = NormalizeCompact(propName);
        string[] propTokens = Tokenize(propName);

        string stationName = spot.Property?.Station?.StationName ?? string.Empty;
        string stationCompact = NormalizeCompact(stationName);
        string[] stationTokens = Tokenize(stationName);

        string address = spot.Property?.Address ?? string.Empty;
        string addressCompact = NormalizeCompact(address);
        string[] addressTokens = Tokenize(address);

        string label = spot.ParkingLabel ?? string.Empty;
        string labelCompact = NormalizeCompact(label);
        string[] labelTokens = Tokenize(label);

        string desc = $"{spot.Description} {spot.Property?.Description}".Trim();
        string[] allWords = propTokens.Concat(stationTokens).Concat(addressTokens).Concat(labelTokens).ToArray();
        var allWordSet = new HashSet<string>(allWords, StringComparer.OrdinalIgnoreCase);

        int bestScore = 0;
        string bestReason = string.Empty;

        void UpdateBest(int score, string reason)
        {
            if (score > bestScore)
            {
                bestScore = score;
                bestReason = reason;
            }
        }

        // 1. EXACT / DELIMITER-AGNOSTIC MATCH (Highest confidence)
        if (string.Equals(propName, rawQuery.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            UpdateBest(100, "Exact property name match");
        }
        else if (propCompact.Length > 0 && queryCompact.Length > 0 && propCompact == queryCompact)
        {
            UpdateBest(98, "Normalized property match (spaces/symbols ignored)");
        }
        else if (string.Equals(stationName, rawQuery.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            UpdateBest(95, "Exact station name match");
        }
        else if (stationCompact.Length > 0 && queryCompact.Length > 0 && stationCompact == queryCompact)
        {
            UpdateBest(94, "Normalized station match (spaces/symbols ignored)");
        }

        // 2. COMPACT SUBSTRING MATCH ("remove text in between")
        if (queryCompact.Length >= 2)
        {
            if (propCompact.StartsWith(queryCompact))
            {
                UpdateBest(92, "Property name starts with search term");
            }
            else if (propCompact.Contains(queryCompact))
            {
                UpdateBest(88, "Property name contains search term");
            }
            else if (stationCompact.StartsWith(queryCompact))
            {
                UpdateBest(86, "Station name starts with search term");
            }
            else if (stationCompact.Contains(queryCompact))
            {
                UpdateBest(84, "Station name contains search term");
            }
            else if (addressCompact.Contains(queryCompact))
            {
                UpdateBest(80, "Address matches search term");
            }
            else if (labelCompact.Contains(queryCompact))
            {
                UpdateBest(78, "Parking label matches search term");
            }
        }

        // 3. STANDARD CONTAINS MATCH (Words with whitespace)
        if (propName.Contains(queryTrim, StringComparison.OrdinalIgnoreCase))
        {
            UpdateBest(85, "Property name contains phrase");
        }
        else if (stationName.Contains(queryTrim, StringComparison.OrdinalIgnoreCase))
        {
            UpdateBest(82, "Station name contains phrase");
        }
        else if (address.Contains(queryTrim, StringComparison.OrdinalIgnoreCase))
        {
            UpdateBest(78, "Address contains phrase");
        }
        else if (label.Contains(queryTrim, StringComparison.OrdinalIgnoreCase))
        {
            UpdateBest(75, "Parking bay label contains phrase");
        }
        else if (desc.Contains(queryTrim, StringComparison.OrdinalIgnoreCase))
        {
            UpdateBest(65, "Description matches phrase");
        }

        // 4. MULTI-TOKEN OVERLAP MATCH (e.g. "Wangsa Maju PV" or "Setapak PV 128")
        if (meaningfulTokens.Length > 1)
        {
            int matchedTokenCount = 0;

            foreach (var token in meaningfulTokens)
            {
                if (token.All(char.IsDigit))
                {
                    // Pure digit token (e.g. "18", "128", "9", "1") must match a discrete word or number prefix
                    if (allWordSet.Contains(token) || allWords.Any(w => w.StartsWith(token) && w.All(char.IsDigit)))
                    {
                        matchedTokenCount++;
                    }
                }
                else
                {
                    string cToken = NormalizeCompact(token);
                    if (allWordSet.Contains(token) || allWords.Any(w => w.StartsWith(token)) || (cToken.Length >= 2 && NormalizeCompact(string.Join("", allWords)).Contains(cToken)))
                    {
                        matchedTokenCount++;
                    }
                }
            }

            if (matchedTokenCount == meaningfulTokens.Length)
            {
                UpdateBest(75 + (meaningfulTokens.Length * 3), "All search terms matched across property/station/address");
            }
            else if (matchedTokenCount > 0 && meaningfulTokens.Length > 2)
            {
                int partialScore = 35 + (int)(((double)matchedTokenCount / meaningfulTokens.Length) * 25);
                UpdateBest(partialScore, $"{matchedTokenCount}/{meaningfulTokens.Length} terms matched");
            }
        }

        // 5. WILDCARD SUBSEQUENCE MATCH ("the wildcard", e.g. "pv18" inside "pv18residences")
        if (queryCompact.Length >= 3)
        {
            if (IsSubsequence(queryCompact, propCompact))
            {
                UpdateBest(72, "Wildcard pattern matched property");
            }
            else if (IsSubsequence(queryCompact, stationCompact))
            {
                UpdateBest(68, "Wildcard pattern matched station");
            }
        }

        // 6. FUZZY SEARCH (Levenshtein Distance & Edit Similarity for typos)
        if (queryCompact.Length >= 3)
        {
            if (propCompact.Length >= 3)
            {
                int dist = LevenshteinDistance(queryCompact, propCompact);
                double sim = SimilarityRatio(queryCompact, propCompact);

                // For strings of similar length (e.g. "pv18" vs "pv128")
                if (dist <= 2 || sim >= 0.70)
                {
                    int fuzzyScore = 45 + (int)(sim * 35);
                    UpdateBest(fuzzyScore, $"Fuzzy property match ({(int)(sim * 100)}% similarity)");
                }
            }

            if (stationCompact.Length >= 3)
            {
                int dist = LevenshteinDistance(queryCompact, stationCompact);
                double sim = SimilarityRatio(queryCompact, stationCompact);

                if (dist <= 2 || sim >= 0.70)
                {
                    int fuzzyScore = 40 + (int)(sim * 35);
                    UpdateBest(fuzzyScore, $"Fuzzy station match ({(int)(sim * 100)}% similarity)");
                }
            }

            // Word-level fuzzy match (e.g. "wangsa majo" vs "wangsa maju", "setepak" vs "setapak")
            foreach (var qToken in meaningfulTokens.Where(t => t.Length >= 3))
            {
                foreach (var pToken in propTokens.Concat(stationTokens))
                {
                    if (pToken.Length < 3) continue;
                    int d = LevenshteinDistance(qToken, pToken);
                    if (d <= 1 || (qToken.Length >= 5 && d <= 2))
                    {
                        double sim = SimilarityRatio(qToken, pToken);
                        int score = 45 + (int)(sim * 30);
                        UpdateBest(score, $"Fuzzy word match '{pToken}' ({(int)(sim * 100)}%)");
                    }
                }
            }
        }

        return (bestScore, bestReason);
    }

    /// <summary>
    /// Executes the full fuzzy search pipeline over a list of parking spots.
    /// Returns scored, ranked matches and intelligent "Did you mean" suggestions.
    /// </summary>
    public static ParkingFuzzySearchResult Search(IEnumerable<ParkingSpot> allSpots, string? query)
    {
        var result = new ParkingFuzzySearchResult();
        var rawQuery = query?.Trim();

        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            result.Matches = allSpots.Select(s => new ParkingSearchResultMatch
            {
                Spot = s,
                Score = 100,
                MatchReason = "All available spots"
            }).ToList();
            return result;
        }

        var scored = new List<ParkingSearchResultMatch>();
        var suggestionCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var spot in allSpots)
        {
            var (score, reason) = EvaluateSpot(rawQuery, spot);

            // Consider it a match if score is positive and meaningful
            if (score >= 35)
            {
                scored.Add(new ParkingSearchResultMatch
                {
                    Spot = spot,
                    Score = score,
                    MatchReason = reason
                });
            }

            // Collect suggestions for near-matches or candidates
            if (!string.IsNullOrWhiteSpace(spot.Property?.PropertyName))
            {
                suggestionCandidates.Add(spot.Property.PropertyName);
            }
            if (!string.IsNullOrWhiteSpace(spot.Property?.Station?.StationName))
            {
                suggestionCandidates.Add(spot.Property.Station.StationName);
            }
        }

        // Order by score descending, then CreatedAt descending
        result.Matches = scored
            .OrderByDescending(m => m.Score)
            .ThenByDescending(m => m.Spot.CreatedAt)
            .ToList();

        // Build "Did you mean" suggestions based on query similarity
        string compactQ = NormalizeCompact(rawQuery);
        var rankedSuggestions = suggestionCandidates
            .Select(name => new
            {
                Name = name,
                Similarity = Math.Max(
                    SimilarityRatio(rawQuery.ToLowerInvariant(), name.ToLowerInvariant()),
                    SimilarityRatio(compactQ, NormalizeCompact(name))
                )
            })
            .Where(x => x.Similarity >= 0.55 && !string.Equals(x.Name, rawQuery, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Similarity)
            .Take(4)
            .Select(x => x.Name)
            .ToList();

        result.Suggestions = rankedSuggestions;
        result.DidYouMean = rankedSuggestions.FirstOrDefault();

        return result;
    }
}
