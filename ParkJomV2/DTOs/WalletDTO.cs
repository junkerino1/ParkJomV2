namespace ParkJomV2.DTOs;

public class WalletTopUpResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int PaymentId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string CheckoutUrl { get; set; } = string.Empty;
}

public class WalletSummaryResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int WalletId { get; set; }
    public decimal Balance { get; set; }
    public decimal OnHold { get; set; }
    public string Currency { get; set; } = "MYR";
    public string Status { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public class WalletTopUpStatusResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int PaymentId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "MYR";
    public string State { get; set; } = "unavailable";
    public string PaymentStatus { get; set; } = string.Empty;
    public string CheckoutStatus { get; set; } = string.Empty;
    public string StripePaymentStatus { get; set; } = string.Empty;
    public bool IsCredited { get; set; }
    public bool CanContinue { get; set; }
    public string? CheckoutUrl { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public decimal WalletBalance { get; set; }
    public DateTime WalletUpdatedAt { get; set; }
}

public class WalletTopUpHistoryResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<WalletTopUpListItemResponse> Data { get; set; } = new();
}

public class WalletTopUpListItemResponse
{
    public int PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "MYR";
    public string Status { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class WalletHistoryResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Month { get; set; }
    public string? Type { get; set; }
    public string? Status { get; set; }
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<WalletHistoryItemResponse> Data { get; set; } = new();
}

public class WalletHistoryItemResponse
{
    public int TransactionId { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string ReferenceNumber { get; set; } = string.Empty;
    public int? BookingId { get; set; }
    public DateTime CreatedAt { get; set; }
}
