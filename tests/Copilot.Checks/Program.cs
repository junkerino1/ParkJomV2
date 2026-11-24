using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using ParkJomV2.Services.Copilot;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception($"Assertion failed: {message}");
}

static string LocateRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "ParkJomV2", "Data", "Gtfs", "kl_rail_stops.json")))
            return dir.FullName;
        dir = dir.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate ParkJomV2/Data/Gtfs/kl_rail_stops.json.");
}

// 1. GTFS Station Search (tokenization + noise stripping + compact + typo tolerance)
var repoRoot = LocateRepoRoot();
var env = new TestWebHostEnvironment(Path.Combine(repoRoot, "ParkJomV2"));
var gtfs = new GtfsService(env, NullLogger<GtfsService>.Instance);

var subangStops = gtfs.GetAllStops(null, "subang lrt");
Assert(subangStops.Count >= 3, $"Expected >=3 stops for 'subang lrt', got {subangStops.Count}");
Assert(subangStops.Any(s => s.StopName.Contains("Subang Jaya", StringComparison.OrdinalIgnoreCase)),
    "Expected 'subang lrt' to match Subang Jaya.");
Assert(subangStops.Any(s => s.StopName.Contains("Lembah Subang", StringComparison.OrdinalIgnoreCase)),
    "Expected 'subang lrt' to match Lembah Subang.");
Assert(subangStops.Any(s => s.StopName.Contains("Subang Alam", StringComparison.OrdinalIgnoreCase)),
    "Expected 'subang lrt' to match Subang Alam.");

var ss15Stops = gtfs.GetAllStops(null, "ss15");
Assert(ss15Stops.Any(s => s.StopName.Equals("Ss 15", StringComparison.OrdinalIgnoreCase)),
    "Expected compact query 'ss15' to match SS 15.");

var typoStops = gtfs.GetAllStops(null, "wangsa majo");
Assert(typoStops.Any(s => s.StopName.Contains("Wangsa Maju", StringComparison.OrdinalIgnoreCase)),
    "Expected typo query 'wangsa majo' to match Wangsa Maju.");

// 2. CopilotAvailabilityHelper (today rejection, booking conflicts, weekday/weekend rules, pricing)
var today = new DateOnly(2026, 11, 22); // Sunday

ParkingSpot BuildSpot(decimal? dailyRate, decimal? monthlyRate, params Availability[] rules) => new()
{
    ParkingSpotId = 104,
    PropertyId = 9,
    OwnerId = 2,
    ParkingLabel = "104/3",
    IsPublished = true,
    AvailabilityStatus = AvailabilityStatus.Available,
    DailyRate = dailyRate,
    MonthlyRate = monthlyRate,
    ParkingAvailabilities = rules.ToList(),
};

var everydayRule = new Availability
{
    DayType = DayType.Everyday,
    StartTime = new TimeOnly(7, 0),
    EndTime = new TimeOnly(22, 0),
};
var weekdayRule = new Availability
{
    DayType = DayType.Weekday,
    StartTime = new TimeOnly(7, 0),
    EndTime = new TimeOnly(22, 0),
};

var everydaySpot = BuildSpot(3.00m, 90.00m, everydayRule);
var confirmedBookingNov24 = new Booking
{
    BookingStatus = BookingStatus.Confirmed,
    StartDate = new DateTime(2026, 11, 24, 0, 0, 0),
    EndDate = new DateTime(2026, 11, 25, 0, 0, 0),
};

// Rejects same-day ("today") for private bays
var todayEval = CopilotAvailabilityHelper.EvaluateDateRange(everydaySpot, new[] { confirmedBookingNov24 }, today, today, today);
Assert(!todayEval.IsAvailable, "Today must be rejected for private bays.");
Assert(todayEval.ErrorReason != null && todayEval.ErrorReason.Contains("2026-11-23") && todayEval.ErrorReason.Contains("public"),
    "Same-day rejection should explain tomorrow minimum and public drive-in lots.");

// Rejects 24 Nov when booked, returns 25 Nov as next available date
var nov24 = new DateOnly(2026, 11, 24);
var nov24Eval = CopilotAvailabilityHelper.EvaluateDateRange(everydaySpot, new[] { confirmedBookingNov24 }, nov24, nov24, today);
Assert(!nov24Eval.IsAvailable, "Booked 24 Nov must be rejected.");
Assert(nov24Eval.UnavailableDates.Contains(nov24), "UnavailableDates should include 2026-11-24.");
Assert(nov24Eval.NextAvailableDates.Contains(new DateOnly(2026, 11, 25)), "NextAvailableDates should include 2026-11-25.");

// Rejects multi-day span crossing booked 24 Nov
var spanEval = CopilotAvailabilityHelper.EvaluateDateRange(everydaySpot, new[] { confirmedBookingNov24 }, new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 25), today);
Assert(!spanEval.IsAvailable, "Span crossing booked 24 Nov must be rejected.");
Assert(spanEval.UnavailableDates.SequenceEqual(new[] { nov24 }), "Only 24 Nov should be marked unavailable in span.");

// Rejects weekend date on weekday-only schedule
var weekdaySpot = BuildSpot(4.00m, 100.00m, weekdayRule);
var saturdayNov28 = new DateOnly(2026, 11, 28);
var weekendEval = CopilotAvailabilityHelper.EvaluateDateRange(weekdaySpot, Array.Empty<Booking>(), saturdayNov28, saturdayNov28, today);
Assert(!weekendEval.IsAvailable, "Saturday must be rejected on weekday-only availability.");
Assert(weekendEval.NextAvailableDates.Contains(new DateOnly(2026, 11, 30)), "Next weekday 2026-11-30 should be offered.");

// Pricing tiers: daily (1-9d), discounted daily (10-20d), monthly prorated (21+d), missing rate rejection
var dailyEval = CopilotAvailabilityHelper.EvaluateDateRange(everydaySpot, Array.Empty<Booking>(), new DateOnly(2026, 11, 26), new DateOnly(2026, 11, 28), today);
Assert(dailyEval.IsAvailable && dailyEval.RateType == BookingRateType.Daily && dailyEval.EstimatedSubtotal == 9.00m,
    $"Expected 3 days daily = RM 9.00, got {dailyEval.EstimatedSubtotal}.");

var tenDayEval = CopilotAvailabilityHelper.EvaluateDateRange(everydaySpot, Array.Empty<Booking>(), new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 10), today);
Assert(tenDayEval.IsAvailable && tenDayEval.RateType == BookingRateType.DiscountedDaily && tenDayEval.EstimatedSubtotal == 27.00m,
    $"Expected 10 days 10% discounted = RM 27.00, got {tenDayEval.EstimatedSubtotal}.");

var monthlyEval = CopilotAvailabilityHelper.EvaluateDateRange(everydaySpot, Array.Empty<Booking>(), new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 21), today);
Assert(monthlyEval.IsAvailable && monthlyEval.RateType == BookingRateType.MonthlyProrated && monthlyEval.EstimatedSubtotal == 63.00m,
    $"Expected 21 days prorated monthly = RM 63.00, got {monthlyEval.EstimatedSubtotal}.");

var monthlyOnlySpot = BuildSpot(null, 90.00m, everydayRule);
var missingDailyEval = CopilotAvailabilityHelper.EvaluateDateRange(monthlyOnlySpot, Array.Empty<Booking>(), new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 1), today);
Assert(!missingDailyEval.IsAvailable, "Spot missing daily rate must reject 1-day range.");

// 3. GeminiClient.ResolveModelWaterfall (all 10 active models, soft-capped demoted, cooling sorted by recovery)
var options = new GeminiOptions
{
    Models = new List<string>
    {
        "gemini-4-hydrogen",
        "gemini-3.8-flash",
        "gemini-3.6-flash",
        "gemini-3-flash-preview",
        "gemini-2.5-flash",
        "gemini-3.5-flash-lite",
        "gemini-3.1-flash-lite",
        "gemini-2.5-flash-lite",
        "gemma-4-31b-it",
        "gemma-4-26b-a4b-it",
    }
};
var nowUtc = new DateTime(2026, 11, 22, 1, 0, 0, DateTimeKind.Utc);
var cooldowns = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
{
    ["gemini-4-hydrogen"] = nowUtc.AddMinutes(5),
    ["gemini-3.8-flash"] = nowUtc.AddMinutes(1),
};
var softLimited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "gemini-3.6-flash",
};
var waterfall = GeminiClient.ResolveModelWaterfall(options, cooldowns, softLimited, nowUtc);
Assert(waterfall.Count == 10, $"Expected all 10 configured models, got {waterfall.Count}.");
Assert(waterfall[0] == "gemini-3-flash-preview", $"Expected gemini-3-flash-preview first when top 3 are cooling/soft-limited, got {waterfall[0]}.");
Assert(waterfall[7] == "gemini-3.6-flash", $"Expected soft-limited gemini-3.6-flash after ready models, got {waterfall[7]}.");
Assert(waterfall[8] == "gemini-3.8-flash" && waterfall[9] == "gemini-4-hydrogen",
    "Expected hard-cooled models at the end ordered by soonest recovery.");

// 4. GeminiClient.ComputeCooldown (daily RPD until Pacific midnight, RPM delay, 503, 404, 400)
var rpdCooldown = GeminiClient.ComputeCooldown(429, """{"error":"Quota exceeded for PerDay limit","retryDelay":"22s"}""", null, nowUtc);
Assert(rpdCooldown.HasValue && rpdCooldown.Value > TimeSpan.FromHours(1),
    $"Daily RPD 429 must stay cooled down until next Pacific midnight (got {rpdCooldown}).");

var rpmDelayCooldown = GeminiClient.ComputeCooldown(429, """{"error":"Rate limit exceeded","retryDelay":"42s"}""", null, nowUtc);
Assert(rpmDelayCooldown == TimeSpan.FromSeconds(42), $"Expected 42s retryDelay cooldown, got {rpmDelayCooldown}.");

var rpmDefaultCooldown = GeminiClient.ComputeCooldown(429, """{"error":"Resource exhausted RPM"}""", null, nowUtc);
Assert(rpmDefaultCooldown == TimeSpan.FromSeconds(65), $"Expected 65s default RPM cooldown, got {rpmDefaultCooldown}.");

Assert(GeminiClient.ComputeCooldown(503, "Service unavailable", null, nowUtc) == TimeSpan.FromSeconds(90), "Expected 90s for 503.");
Assert(GeminiClient.ComputeCooldown(404, "Not found", null, nowUtc) == TimeSpan.FromHours(6), "Expected 6h for 404.");
Assert(GeminiClient.ComputeCooldown(400, "Invalid argument", null, nowUtc) == null, "Expected null cooldown for 400.");

// 5. GeminiClient.SanitizeContentsForModelSwitch (strips thought parts & thoughtSignature, keeps functionCall/response/text)
var rawContents = new JsonArray
{
    new JsonObject
    {
        ["role"] = "user",
        ["parts"] = new JsonArray { new JsonObject { ["text"] = "Find Subang parking" } }
    },
    new JsonObject
    {
        ["role"] = "model",
        ["parts"] = new JsonArray
        {
            new JsonObject { ["thought"] = true, ["text"] = "Internal chain of thought" },
            new JsonObject
            {
                ["functionCall"] = new JsonObject { ["name"] = "searchParking" },
                ["thoughtSignature"] = "opaque-sig-123"
            }
        }
    },
    new JsonObject
    {
        ["role"] = "user",
        ["parts"] = new JsonArray
        {
            new JsonObject
            {
                ["functionResponse"] = new JsonObject { ["name"] = "searchParking" }
            }
        }
    }
};
var cleanedContents = GeminiClient.SanitizeContentsForModelSwitch(rawContents);
var modelParts = cleanedContents[1]!["parts"]!.AsArray();
Assert(modelParts.Count == 1, $"Expected thought part removed leaving 1 functionCall part, got {modelParts.Count}.");
Assert(modelParts[0]!["functionCall"]!["name"]!.GetValue<string>() == "searchParking", "Expected functionCall preserved.");
Assert(!modelParts[0]!.AsObject().ContainsKey("thoughtSignature"), "Expected thoughtSignature stripped.");
Assert(cleanedContents[0]!["parts"]![0]!["text"]!.GetValue<string>() == "Find Subang parking", "Expected user text preserved.");
Assert(cleanedContents[2]!["parts"]![0]!["functionResponse"]!["name"]!.GetValue<string>() == "searchParking", "Expected functionResponse preserved.");

Console.WriteLine("Copilot backend checks: GTFS search, booking availability & pricing rules, 10-model waterfall, cooldowns, and thought sanitization passed.");

sealed class TestWebHostEnvironment(string root) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Copilot.Checks";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = root;
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = root;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
