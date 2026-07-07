using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;   

namespace ParkJomV2.Models;

public class VerificationDocument
{
    [Key]
    public int VerificationDocumentId { get; set; }

    [Required]
    public int VerificationRequestId { get; set; }

    [Required]
    public int MediaFileId { get; set; }

    [Required]
    public VerificationDocumentType DocumentType { get; set; }

    public DateTime UploadedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation Properties

    [ForeignKey(nameof(VerificationRequestId))]
    public ParkingVerificationRequest VerificationRequest { get; set; } = null!;

    [ForeignKey(nameof(MediaFileId))]
    public MediaFile MediaFile { get; set; } = null!;
}