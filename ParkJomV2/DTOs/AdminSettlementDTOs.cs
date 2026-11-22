using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public sealed class AdminSettlementSummaryDTO
{
    public decimal CommissionRate { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal PlatformWalletBalance { get; set; }
    public decimal PlatformTotalRevenue { get; set; }
    public decimal PlatformTotalRefunded { get; set; }
    public decimal TotalOwnerAvailable { get; set; }
    public decimal TotalEscrowOnHold { get; set; }
    public int UnreleasedCompletedBookingsCount { get; set; }
    public decimal UnreleasedCompletedBookingsAmount { get; set; }
    public int PendingWithdrawalsCount { get; set; }
    public decimal PendingWithdrawalsAmount { get; set; }
    public int ClearedWithdrawalsCount { get; set; }
    public decimal ClearedWithdrawalsAmount { get; set; }
    public int EligibleAutoWithdrawOwnersCount { get; set; }
    public decimal EligibleAutoWithdrawAmount { get; set; }
    public decimal TotalGrossCommissions { get; set; }
    public decimal TotalCommissionTaxSst { get; set; }
    public decimal TotalOverstayPenaltiesCollected { get; set; }
    public decimal TotalStripeTopUps { get; set; }
    public string IbgScheduleLabel { get; set; } = "Twice Weekly (Tue & Fri, 14:00 MYT)";
    public DateTime NextScheduledIbgCutoffUtc { get; set; }
    public SettlementBatchSummaryDTO? LatestBatch { get; set; }
    public string ReconciliationHealth { get; set; } = "Matched";
    public DateTime GeneratedAtUtc { get; set; }
}

public sealed class AdminSettlementSummaryResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public AdminSettlementSummaryDTO? Data { get; set; }
}

public sealed class AdminWithdrawalItemDTO
{
    public int TransactionId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public int WalletId { get; set; }
    public int OwnerUserId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BankCode { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public bool AutoWithdraw { get; set; }
    public int? BatchId { get; set; }
    public string? BatchReference { get; set; }
    public string? BatchItemStatus { get; set; }
    public string? ClearingReference { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AdminWithdrawalListResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<AdminWithdrawalItemDTO> Data { get; set; } = new();
}

public sealed class WithdrawalDecisionRequest
{
    [Required]
    public bool Approve { get; set; }

    [StringLength(100)]
    public string? BankReference { get; set; }

    [StringLength(300)]
    public string? Reason { get; set; }
}

public sealed class WithdrawalDecisionResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public AdminWithdrawalItemDTO? Data { get; set; }
}

public sealed class GenerateSettlementBatchRequest
{
    [StringLength(20)]
    public string Rail { get; set; } = "IBG";

    public List<int>? TransactionIds { get; set; }

    public bool IncludeAutoWithdrawOwners { get; set; } = true;

    [StringLength(100)]
    public string? ScheduleWindow { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}

public sealed class SettlementBatchSummaryDTO
{
    public int SettlementBatchId { get; set; }
    public string BatchReference { get; set; } = string.Empty;
    public string Rail { get; set; } = "IBG";
    public string ScheduleWindow { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "MYR";
    public string BnmFormatVersion { get; set; } = "BNM-IBG-v2.1";
    public string FileChecksum { get; set; } = string.Empty;
    public string? BankReference { get; set; }
    public int? CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ClearedAt { get; set; }
    public string? Notes { get; set; }
}

public sealed class SettlementBatchItemDTO
{
    public int SettlementBatchItemId { get; set; }
    public int TransactionId { get; set; }
    public string TransactionReference { get; set; } = string.Empty;
    public int OwnerUserId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BankCode { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ClearingReference { get; set; }
    public string? FailureReason { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public sealed class SettlementBatchDetailDTO
{
    public SettlementBatchSummaryDTO Batch { get; set; } = new();
    public List<SettlementBatchItemDTO> Items { get; set; } = new();
    public string IbgPreview { get; set; } = string.Empty;
}

public sealed class SettlementBatchListResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<SettlementBatchSummaryDTO> Data { get; set; } = new();
}

public sealed class SettlementBatchDetailResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public SettlementBatchDetailDTO? Data { get; set; }
}

public sealed class BankClearingItemUpdateRequest
{
    public int? SettlementBatchItemId { get; set; }
    public int? TransactionId { get; set; }
    public string? TransactionReference { get; set; }

    [Required]
    [StringLength(30)]
    public string Status { get; set; } = "Cleared";

    [StringLength(100)]
    public string? ClearingReference { get; set; }

    [StringLength(300)]
    public string? FailureReason { get; set; }
}

public sealed class AdminBatchClearingRequest
{
    [StringLength(100)]
    public string? BankReference { get; set; }

    public List<BankClearingItemUpdateRequest>? Items { get; set; }
}

public sealed class BankClearingWebhookRequest
{
    [Required]
    [StringLength(50)]
    public string BatchReference { get; set; } = string.Empty;

    [StringLength(100)]
    public string? BankReference { get; set; }

    public List<BankClearingItemUpdateRequest>? Items { get; set; }
}

public sealed class SettlementLedgerEntryDTO
{
    public int TransactionId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string LedgerAccount { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public string? UserEmail { get; set; }
    public int? BookingId { get; set; }
    public string? BookingReference { get; set; }
    public string? SpotLabel { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal NetAmount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class SettlementLedgerResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public decimal TaxRatePercent { get; set; }
    public decimal TotalGrossVolume { get; set; }
    public decimal TotalPlatformCommission { get; set; }
    public decimal TotalTaxAmount { get; set; }
    public decimal TotalNetCommission { get; set; }
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<SettlementLedgerEntryDTO> Data { get; set; } = new();
}

public sealed class EscrowSyncResultDTO
{
    public int ReleasedBookingsCount { get; set; }
    public decimal ReleasedAmount { get; set; }
    public int SkippedOrAlreadySettledCount { get; set; }
    public int FlaggedMismatchCount { get; set; }
    public DateTime SyncedAtUtc { get; set; }
}

public sealed class EscrowSyncResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public EscrowSyncResultDTO? Data { get; set; }
}

public sealed class ReconciliationSectionDTO
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal ExpectedAmount { get; set; }
    public decimal RecordedAmount { get; set; }
    public decimal VarianceAmount { get; set; }
    public string Status { get; set; } = "Matched";
    public string Notes { get; set; } = string.Empty;
}

public sealed class ReconciliationReceiptRowDTO
{
    public string Source { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public decimal StripeOrBookingAmount { get; set; }
    public decimal LedgerAmount { get; set; }
    public decimal Variance { get; set; }
    public string MatchStatus { get; set; } = "Matched";
    public string Detail { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
}

public sealed class ReconciliationReportDTO
{
    public string OverallStatus { get; set; } = "Matched";
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public int StripeCompletedPaymentsCount { get; set; }
    public decimal StripeCompletedPaymentsTotal { get; set; }
    public int StripeTopUpTransactionsCount { get; set; }
    public decimal StripeTopUpTransactionsTotal { get; set; }
    public int PublicParkingStripeCount { get; set; }
    public decimal PublicParkingStripeTotal { get; set; }
    public int BookingSplitExceptionsCount { get; set; }
    public decimal PlatformWalletRecordedBalance { get; set; }
    public decimal PlatformWalletExpectedBalance { get; set; }
    public decimal TotalOwnerHeldEscrow { get; set; }
    public decimal TotalOwnerAvailable { get; set; }
    public decimal TotalClearedWithdrawals { get; set; }
    public decimal TotalPendingWithdrawals { get; set; }
    public List<ReconciliationSectionDTO> Sections { get; set; } = new();
    public List<ReconciliationReceiptRowDTO> ExceptionsAndRecentReceipts { get; set; } = new();
    public DateTime GeneratedAtUtc { get; set; }
}

public sealed class ReconciliationReportResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public ReconciliationReportDTO? Data { get; set; }
}
