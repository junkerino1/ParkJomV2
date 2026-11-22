using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public sealed class OverstayWorkerStatusDTO
{
    public bool Enabled { get; set; }
    public bool IsRunning { get; set; }
    public int IntervalSeconds { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? LastScanStartedAtUtc { get; set; }
    public DateTime? LastScanCompletedAtUtc { get; set; }
    public int LastScanDetectedCount { get; set; }
    public int LastScanWarningsSent { get; set; }
    public int LastScanPenaltiesDebited { get; set; }
    public long TotalScansCompleted { get; set; }
    public string? LastError { get; set; }
}

public sealed class OverstaySummaryDTO
{
    public int GracePeriodMinutes { get; set; }
    public decimal PenaltyMultiplier { get; set; }
    public decimal MaxDailyPenalty { get; set; }
    public bool HardwareExitLockEnabled { get; set; }
    public int ActiveOverstaysCount { get; set; }
    public int WarningsSentCount { get; set; }
    public int PaymentDueCount { get; set; }
    public int DisputedCount { get; set; }
    public int HardwareLockedCount { get; set; }
    public int WaivedOrResolvedCount { get; set; }
    public decimal TotalCalculatedPenalties { get; set; }
    public decimal TotalCollectedPenalties { get; set; }
    public decimal TotalOutstandingPenalties { get; set; }
    public decimal TotalWaivedPenalties { get; set; }
    public OverstayWorkerStatusDTO WorkerStatus { get; set; } = new();
    public DateTime GeneratedAtUtc { get; set; }
}

public sealed class OverstaySummaryResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public OverstaySummaryDTO? Data { get; set; }
}

public sealed class OverstayCaseDTO
{
    public int OverstayCaseId { get; set; }
    public string CaseReference { get; set; } = string.Empty;
    public int BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public int RenterId { get; set; }
    public string RenterName { get; set; } = string.Empty;
    public string RenterEmail { get; set; } = string.Empty;
    public string? RenterPhone { get; set; }
    public string VehiclePlate { get; set; } = string.Empty;
    public int ParkingSpotId { get; set; }
    public string SpotLabel { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public int? IoTDeviceId { get; set; }
    public string? Esp32Serial { get; set; }
    public string? DeviceStatus { get; set; }
    public string WindowDate { get; set; } = string.Empty;
    public DateTime ScheduledEndAtUtc { get; set; }
    public DateTime GraceEndsAtUtc { get; set; }
    public DateTime DetectedAtUtc { get; set; }
    public DateTime? DepartedAtUtc { get; set; }
    public int OverstayMinutes { get; set; }
    public int BillableHours { get; set; }
    public decimal BaseHourlyRate { get; set; }
    public decimal PenaltyMultiplier { get; set; }
    public decimal HourlyPenaltyTariff { get; set; }
    public decimal CalculatedPenaltyAmount { get; set; }
    public decimal DebitedAmount { get; set; }
    public decimal WaivedAmount { get; set; }
    public decimal RemainingDueAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public DateTime? PushWarningSentAtUtc { get; set; }
    public DateTime? SmsWarningSentAtUtc { get; set; }
    public string? SmsProviderRef { get; set; }
    public string? SmsDeliveryStatus { get; set; }
    public DateTime? LedgerDebitedAtUtc { get; set; }
    public int? PenaltyTransactionId { get; set; }
    public bool HardwareLockActive { get; set; }
    public DateTime? HardwareLockEngagedAtUtc { get; set; }
    public DateTime? HardwareLockReleasedAtUtc { get; set; }
    public int? HardwareLockReleasedByUserId { get; set; }
    public int? DisputeInvestigationId { get; set; }
    public string? ResolutionNotes { get; set; }
    public int? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class OverstayCaseListResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<OverstayCaseDTO> Data { get; set; } = new();
}

public sealed class OverstayCaseActionResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public OverstayCaseDTO? Data { get; set; }
}

public sealed class OverstayScanResultDTO
{
    public int ActiveBookingsScanned { get; set; }
    public int CasesDetectedOrUpdated { get; set; }
    public int WarningsDispatched { get; set; }
    public int PenaltiesDebited { get; set; }
    public int CasesAutoResolved { get; set; }
    public DateTime ScannedAtUtc { get; set; }
}

public sealed class OverstayScanResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public OverstayScanResultDTO? Data { get; set; }
}

public sealed class OverstayWarnRequest
{
    public bool ForceResend { get; set; } = true;
}

public sealed class OverstayWaiveRequest
{
    [Range(0.01, 10000.0)]
    public decimal? WaiverAmount { get; set; }

    [Required]
    [StringLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class OverstayDisputeReviewRequest
{
    /// <summary>"flag" | "uphold" | "partial_waive" | "waive"</summary>
    [Required]
    [StringLength(30)]
    public string Action { get; set; } = "flag";

    [Range(0.0, 10000.0)]
    public decimal? WaiverAmount { get; set; }

    [Required]
    [StringLength(1000)]
    public string Notes { get; set; } = string.Empty;
}

public sealed class OverstaySecurityReleaseRequest
{
    [Required]
    [StringLength(500)]
    public string Reason { get; set; } = string.Empty;

    public bool TriggerBollardUnlock { get; set; } = false;

    public bool MarkResolved { get; set; } = true;
}

public sealed class OverstaySecurityReleaseResultDTO
{
    public OverstayCaseDTO Case { get; set; } = new();
    public bool BollardUnlockAttempted { get; set; }
    public bool BollardUnlockSucceeded { get; set; }
    public string? BollardOutcome { get; set; }
}

public sealed class OverstaySecurityReleaseResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public OverstaySecurityReleaseResultDTO? Data { get; set; }
}

public sealed class UpdateOverstayPolicyRequest
{
    [Range(0, 1440)]
    public int? GracePeriodMinutes { get; set; }

    [Range(1.0, 5.0)]
    public decimal? OverstayPenaltyMultiplier { get; set; }

    [Range(0.0, 500.0)]
    public decimal? OverstayMaxDailyPenalty { get; set; }

    public bool? HardwareExitLockEnabled { get; set; }
}
