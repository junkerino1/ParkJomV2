using System.IO.Compression;
using System.Xml.Linq;
using ParkJomV2.Models;
using ParkJomV2.Services.Overstay;
using ParkJomV2.Services.Settlement;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException($"ASSERTION FAILED: {message}");
    }
}

Console.WriteLine("[1] MalaysianBankDirectory BIC resolution & BNM IBG generation");
Assert(MalaysianBankDirectory.ResolveBic("Maybank Berhad") == "MBBEMYKL", "Maybank BIC mismatch");
Assert(MalaysianBankDirectory.ResolveBic("CIMB Bank") == "CIBBMYKL", "CIMB BIC mismatch");
Assert(MalaysianBankDirectory.ResolveBic("Public Bank") == "PBBEMYKL", "Public Bank BIC mismatch");
Assert(MalaysianBankDirectory.ResolveBic("RHB Bank") == "RHBBMYKL", "RHB BIC mismatch");
Assert(MalaysianBankDirectory.ResolveBic("Hong Leong Bank") == "HLBBMYKL", "HLB BIC mismatch");
Assert(MalaysianBankDirectory.ResolveBic("Unknown Cooperative") == "PAYNETMY", "Fallback BIC mismatch");

var batch = new SettlementBatch
{
    SettlementBatchId = 10,
    BatchReference = "IBG-20261122-001",
    Rail = "IBG",
    Currency = "MYR",
    BnmFormatVersion = "BNM-IBG-v2.1",
    CreatedAt = new DateTime(2026, 11, 22, 6, 0, 0, DateTimeKind.Utc)
};
var items = new List<SettlementBatchItem>
{
    new()
    {
        TransactionId = 205,
        OwnerUserId = 12,
        BankName = "CIMB Bank",
        BankCode = "CIBBMYKL",
        AccountNumber = "7012345678",
        AccountHolderName = "Ali Bin Abu",
        Amount = 45.50m,
        Transaction = new Transaction { ReferenceNumber = "WDR-20261122-12" },
        OwnerUser = new User { Email = "ali@parkjom.my" }
    },
    new()
    {
        TransactionId = 101,
        OwnerUserId = 7,
        BankName = "Maybank",
        BankCode = "MBBEMYKL",
        AccountNumber = "114012345678",
        AccountHolderName = "Siti Aminah",
        Amount = 120.00m,
        Transaction = new Transaction { ReferenceNumber = "WDR-20261122-7" },
        OwnerUser = new User { Email = "siti@parkjom.my" }
    }
};

var (payload1, hash1) = MalaysianBankDirectory.GenerateBnmIbgPayload(batch, items);
var (payload2, hash2) = MalaysianBankDirectory.GenerateBnmIbgPayload(batch, items.AsEnumerable().Reverse());
Assert(payload1 == payload2, "IBG payload must be deterministically ordered by TransactionId");
Assert(hash1 == hash2 && hash1.Length == 64, "SHA-256 checksum must be 64 hex chars and deterministic");
Assert(payload1.Contains("01|PARKJOM|BNM-IBG-v2.1|IBG-20261122-001|IBG|MYR|20261122|2|16550", StringComparison.Ordinal),
    "Header record mismatch");
Assert(payload1.Contains("02|00001|MBBEMYKL|114012345678|Siti Aminah|12000|WDR-20261122-7|siti@parkjom.my", StringComparison.Ordinal),
    "First detail record (sorted by TransactionId 101) mismatch");
Assert(payload1.Contains("99|2|16550|IBG-20261122-001", StringComparison.Ordinal),
    "Trailer record mismatch");
Console.WriteLine("  PASS MalaysianBankDirectory checks");

Console.WriteLine("[2] OpenXmlXlsxWriter multi-sheet OOXML package generation");
var xlsxBytes = OpenXmlXlsxWriter.BuildWorkbook(new[]
{
    new WorkbookSheet(
        "Summary & Tax",
        new[] { "Metric", "Amount (MYR)" },
        new List<IReadOnlyList<object?>>
        {
            new object?[] { "Gross Commission", 100.00m },
            new object?[] { "SST Tax (8%)", 7.41m }
        }),
    new WorkbookSheet(
        "Financial Journal",
        new[] { "Ref", "User", "Amount", "Timestamp" },
        new List<IReadOnlyList<object?>>
        {
            new object?[] { "TX-100", "driver@parkjom.my", 25.50m, new DateTime(2026, 11, 22, 8, 30, 0, DateTimeKind.Utc) }
        })
});
Assert(xlsxBytes.Length > 500, "XLSX output too small");
using (var ms = new MemoryStream(xlsxBytes))
using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
{
    Assert(zip.GetEntry("[Content_Types].xml") is not null, "Missing [Content_Types].xml");
    Assert(zip.GetEntry("_rels/.rels") is not null, "Missing _rels/.rels");
    Assert(zip.GetEntry("xl/workbook.xml") is not null, "Missing xl/workbook.xml");
    Assert(zip.GetEntry("xl/styles.xml") is not null, "Missing xl/styles.xml");
    var sheet1Entry = zip.GetEntry("xl/worksheets/sheet1.xml");
    var sheet2Entry = zip.GetEntry("xl/worksheets/sheet2.xml");
    Assert(sheet1Entry is not null && sheet2Entry is not null, "Missing worksheet XML entries");

    using var s1 = sheet1Entry!.Open();
    var xdoc = XDocument.Load(s1);
    var xmlText = xdoc.ToString();
    Assert(xmlText.Contains("Gross Commission", StringComparison.Ordinal), "Sheet1 missing expected label");
    Assert(xmlText.Contains("100.00", StringComparison.Ordinal), "Sheet1 missing expected decimal");
}
Console.WriteLine("  PASS OpenXmlXlsxWriter checks");

Console.WriteLine("[3] OverstayEnforcementService.CalculateTariff math");
var t1 = OverstayEnforcementService.CalculateTariff(dailyRateSnapshot: 16.00m, windowHours: 8m, overstayMinutes: 25, penaltyMultiplier: 1.50m, maxDailyPenalty: 30.00m);
Assert(t1.BaseHourlyRate == 2.00m, $"Expected base 2.00, got {t1.BaseHourlyRate}");
Assert(t1.HourlyPenaltyTariff == 3.00m, $"Expected 1.5x tariff 3.00, got {t1.HourlyPenaltyTariff}");
Assert(t1.BillableHours == 1, $"Expected 1 billable hr for 25 mins, got {t1.BillableHours}");
Assert(t1.CalculatedPenalty == 3.00m, $"Expected penalty 3.00, got {t1.CalculatedPenalty}");

var t2 = OverstayEnforcementService.CalculateTariff(dailyRateSnapshot: 40.00m, windowHours: 8m, overstayMinutes: 61, penaltyMultiplier: 1.50m, maxDailyPenalty: 30.00m);
Assert(t2.BaseHourlyRate == 5.00m && t2.HourlyPenaltyTariff == 7.50m && t2.BillableHours == 2 && t2.CalculatedPenalty == 15.00m,
    $"Expected 2 billable hrs at 7.50 = 15.00, got {t2.BillableHours}h / {t2.CalculatedPenalty}");

var tCapped = OverstayEnforcementService.CalculateTariff(dailyRateSnapshot: 80.00m, windowHours: 8m, overstayMinutes: 250, penaltyMultiplier: 1.50m, maxDailyPenalty: 30.00m);
Assert(tCapped.BillableHours == 5 && tCapped.HourlyPenaltyTariff == 15.00m && tCapped.CalculatedPenalty == 30.00m,
    $"Expected daily cap of 30.00, got {tCapped.CalculatedPenalty}");

var tFallback = OverstayEnforcementService.CalculateTariff(dailyRateSnapshot: 0m, windowHours: 0m, overstayMinutes: 10, penaltyMultiplier: 1.50m, maxDailyPenalty: 30.00m);
Assert(tFallback.BaseHourlyRate == 5.00m && tFallback.HourlyPenaltyTariff == 7.50m && tFallback.CalculatedPenalty == 7.50m,
    "Fallback tariff mismatch");
Console.WriteLine("  PASS OverstayEnforcementService.CalculateTariff checks");

Console.WriteLine("\nALL SETTLEMENT & OVERSTAY BACKEND CHECKS PASSED\n");
