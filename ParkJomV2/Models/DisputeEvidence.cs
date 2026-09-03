using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkJomV2.Models;

public class DisputeEvidence
{
    [Key]
    public int DisputeEvidenceId { get; set; }

    [Required]
    public int DisputeId { get; set; }

    public int? MediaFileId { get; set; }

    [Required]
    [StringLength(50)]
    public string EvidenceType { get; set; } = "Receipt"; // Receipt, BankStatement, Photo, Explanation, Log

    [Required]
    [StringLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    public string FileUrl { get; set; } = string.Empty;

    public int UploadedByUserId { get; set; }

    [Required]
    [StringLength(50)]
    public string UploadedRole { get; set; } = "Customer"; // Customer, Admin

    public bool IsVerified { get; set; } = false;

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    [ForeignKey(nameof(DisputeId))]
    public DisputeInvestigation Dispute { get; set; } = null!;

    [ForeignKey(nameof(MediaFileId))]
    public MediaFile? MediaFile { get; set; }

    [ForeignKey(nameof(UploadedByUserId))]
    public User UploadedByUser { get; set; } = null!;
}
