using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.Models;

public class MediaFile
{
    [Key]
    public int MediaFileId { get; set; }

    [Required]
    [StringLength(255)]
    public string PublicId { get; set; } = string.Empty;

    [Required]
    public string SecureUrl { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string MimeType { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string FileExtension { get; set; } = string.Empty;

    public long FileSize { get; set; }

    [Required]
    [StringLength(30)]
    public string ResourceType { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Folder { get; set; }

    public int UploadedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation

    public User UploadedByUser { get; set; } = null!;

    public ICollection<ParkingSpotImage> ParkingSpotImages { get; set; } = new List<ParkingSpotImage>();

    public ICollection<VerificationDocument> VerificationDocuments { get; set; } = new List<VerificationDocument>();

}