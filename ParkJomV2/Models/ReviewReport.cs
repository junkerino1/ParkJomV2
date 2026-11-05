using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkJomV2.Models;

public class ReviewReport
{
    [Key]
    public int ReviewReportId { get; set; }

    [Required]
    public int ReviewId { get; set; }

    [Required]
    public int ReporterId { get; set; }

    [Required]
    [StringLength(100)]
    public string Reason { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Details { get; set; }

    [Required]
    [StringLength(50)]
    public string Status { get; set; } = "Pending";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ResolvedAt { get; set; }

    public int? ResolvedByAdminId { get; set; }

    // Navigation Properties

    [ForeignKey(nameof(ReviewId))]
    public Review Review { get; set; } = null!;

    [ForeignKey(nameof(ReporterId))]
    public User Reporter { get; set; } = null!;

    [ForeignKey(nameof(ResolvedByAdminId))]
    public User? ResolvedByAdmin { get; set; }
}
