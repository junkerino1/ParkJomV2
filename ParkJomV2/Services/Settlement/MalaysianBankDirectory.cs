using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ParkJomV2.Models;

namespace ParkJomV2.Services.Settlement;

public static class MalaysianBankDirectory
{
    private static readonly (string Keyword, string BicCode)[] BankDirectory =
    {
        ("maybank", "MBBEMYKL"),
        ("malayan banking", "MBBEMYKL"),
        ("cimb", "CIBBMYKL"),
        ("public bank", "PBBEMYKL"),
        ("rhb", "RHBBMYKL"),
        ("hong leong", "HLBBMYKL"),
        ("hlb", "HLBBMYKL"),
        ("ambank", "ARBKMYKL"),
        ("amislamic", "ARBKMYKL"),
        ("bank islam", "BIMBMYKL"),
        ("bimb", "BIMBMYKL"),
        ("bank rakyat", "BKRMMYKL"),
        ("alliance", "MFBBMYKL"),
        ("affin", "PHBMMYKL"),
        ("uob", "UOVBMYKL"),
        ("ocbc", "OCBCMYKL"),
        ("hsbc", "HBMBMYKL"),
        ("standard chartered", "SCBLMYKX"),
        ("bsn", "BSNAMYK1"),
        ("bank simpanan nasional", "BSNAMYK1"),
        ("agrobank", "AGOBMYKL"),
        ("muamalat", "BMMBMYKL"),
        ("mbsb", "AFBQMYKL"),
        ("gxbank", "GXSPMYK2"),
        ("boost", "BOSTMYKL"),
        ("aeon", "AOBMMYKL"),
        ("touch 'n go", "TNGOMYKL"),
        ("tng", "TNGOMYKL"),
        ("duitnow", "PAYNETMY")
    };

    public static string ResolveBic(string? bankName)
    {
        if (string.IsNullOrWhiteSpace(bankName))
        {
            return "PAYNETMY";
        }

        var normalized = bankName.Trim().ToLowerInvariant();
        foreach (var (keyword, bicCode) in BankDirectory)
        {
            if (normalized.Contains(keyword, StringComparison.Ordinal))
            {
                return bicCode;
            }
        }

        return "PAYNETMY";
    }

    public static (string Payload, string Sha256Hex) GenerateBnmIbgPayload(
        SettlementBatch batch,
        IEnumerable<SettlementBatchItem> items)
    {
        var itemList = items.OrderBy(i => i.TransactionId).ToList();
        var malaysiaDate = batch.CreatedAt.AddHours(8).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var totalCents = itemList.Sum(item => (long)Math.Round(item.Amount * 100m, MidpointRounding.AwayFromZero));

        var sb = new StringBuilder();
        sb.AppendLine(string.Join('|',
            "01",
            "PARKJOM",
            batch.BnmFormatVersion,
            SanitizeField(batch.BatchReference),
            SanitizeField(batch.Rail.ToUpperInvariant()),
            batch.Currency,
            malaysiaDate,
            itemList.Count.ToString(CultureInfo.InvariantCulture),
            totalCents.ToString(CultureInfo.InvariantCulture)));

        var seq = 1;
        foreach (var item in itemList)
        {
            var amountCents = (long)Math.Round(item.Amount * 100m, MidpointRounding.AwayFromZero);
            var txRef = item.Transaction?.ReferenceNumber ?? $"TX-{item.TransactionId}";
            var email = item.OwnerUser?.Email ?? $"owner-{item.OwnerUserId}@parkjom.local";
            sb.AppendLine(string.Join('|',
                "02",
                seq.ToString("D5", CultureInfo.InvariantCulture),
                SanitizeField(item.BankCode),
                SanitizeField(item.AccountNumber),
                SanitizeField(item.AccountHolderName),
                amountCents.ToString(CultureInfo.InvariantCulture),
                SanitizeField(txRef),
                SanitizeField(email)));
            seq++;
        }

        sb.AppendLine(string.Join('|',
            "99",
            itemList.Count.ToString(CultureInfo.InvariantCulture),
            totalCents.ToString(CultureInfo.InvariantCulture),
            SanitizeField(batch.BatchReference)));

        var payload = sb.ToString();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        var checksum = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return (payload, checksum);
    }

    private static string SanitizeField(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return raw.Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();
    }
}
