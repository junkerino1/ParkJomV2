namespace ParkJomV2.DTOs;

public class OwnerEarningsSummaryResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Month { get; set; }
    public string TimeZone { get; set; } = "Asia/Kuala_Lumpur";

    // All-time totals. Total = Available + Held + Paid.
    public decimal TotalEarnings { get; set; }
    public decimal AvailableEarnings { get; set; }
    public decimal HeldEarnings { get; set; }
    public decimal PaidEarnings { get; set; }

    // Provided only when a month (YYYY-MM) is requested.
    public decimal? MonthlyEarnings { get; set; }

    // Lifetime booking totals for the owner's parking spots.
    public int TotalBookings { get; set; }
    public decimal GrossEarnings { get; set; }
    public BookingStatusCountsResponse BookingCounts { get; set; } = new();
}

public class BookingStatusCountsResponse
{
    public int Pending { get; set; }
    public int Confirmed { get; set; }
    public int Active { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int Expired { get; set; }
}

public class OwnerEarningsTransactionListResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Month { get; set; }
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<OwnerEarningsTransactionResponse> Data { get; set; } = new();
}

public class OwnerEarningsTransactionResponse
{
    public int BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public int ParkingSpotId { get; set; }
    public string? ParkingLabel { get; set; }
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public string BookingStatus { get; set; } = string.Empty;
    public decimal Payout { get; set; }
    public decimal Commission { get; set; }
    public decimal Cancellation { get; set; }
    public decimal Penalty { get; set; }
    public DateTime CreatedAt { get; set; }
}
