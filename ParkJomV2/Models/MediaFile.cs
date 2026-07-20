using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.Models;

public class MediaFile
{
    [Key]
    public int MediaFileId { get; set; }

    // ---------- Cloudinary ----------

    [Required]
    [StringLength(255)]
    public string PublicId { get; set; } = string.Empty;

    [Required]
    public string SecureUrl { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string ResourceType { get; set; } = string.Empty;   // image, video, raw

    [Required]
    [StringLength(20)]
    public string Format { get; set; } = string.Empty;         // jpg, png, pdf

    [StringLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    [StringLength(255)]
    public string? Folder { get; set; }

    // ---------- Application ----------

    public int UploadedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ---------- Navigation ----------

    public User UploadedByUser { get; set; } = null!;

    public ICollection<ParkingSpotImage> ParkingSpotImages { get; set; } = new List<ParkingSpotImage>();

    public ICollection<VerificationDocument> VerificationDocuments { get; set; } = new List<VerificationDocument>();
}