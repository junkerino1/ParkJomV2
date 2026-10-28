using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public class OwnerWithdrawalRequest
{
    [Required]
    [Range(10.0, 100000.0, ErrorMessage = "Minimum withdrawal amount is RM 10.00.")]
    public decimal Amount { get; set; }

    [StringLength(255)]
    public string? Notes { get; set; }
}

public class OwnerWithdrawalResponseDTO
{
    public int TransactionId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal RemainingBalance { get; set; }
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class OwnerWithdrawalResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public OwnerWithdrawalResponseDTO? Data { get; set; }
}
