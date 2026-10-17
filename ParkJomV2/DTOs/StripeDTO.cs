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

