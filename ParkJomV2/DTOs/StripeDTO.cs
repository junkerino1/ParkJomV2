using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public class StripeTopUpRequest
{
    [Required]
    [Range(10, 5000, ErrorMessage = "Amount must be between RM10 and RM5000.")]
    public decimal Amount { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }
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