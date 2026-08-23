using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public class StripeTopUpRequest
{
    [Required]
    [Range(10, 5000, ErrorMessage = "Amount must be between RM10 and RM5000.")]
    public decimal Amount { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    [RegularExpression("^(web|native)$", ErrorMessage = "ReturnTarget must be either 'web' or 'native'.")]
    public string? ReturnTarget { get; set; }
}

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
