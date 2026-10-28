using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public class OwnerPayoutAccountDTO
{
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public bool AutoWithdraw { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class SaveOwnerPayoutAccountRequest
{
    [Required]
    [StringLength(100)]
    public string BankName { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string AccountNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string AccountHolderName { get; set; } = string.Empty;

    public bool AutoWithdraw { get; set; } = false;
}

public class OwnerPayoutAccountResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public OwnerPayoutAccountDTO? Data { get; set; }
}
