using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class ParkingVerificationRequest
{
    [Key]
    public int VerificationRequestId { get; set; }

    [Required]
    public int ParkingSpotId { get; set; }

    [Required]
    public int SubmittedByUserId { get; set; }

    [Required]
    public VerificationStatus VerificationStatus { get; set; }

    public bool IsCurrent { get; set; }

    public DateTime SubmittedAt { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation Properties

    [ForeignKey(nameof(ParkingSpotId))]
    public ParkingSpot ParkingSpot { get; set; } = null!;

    [ForeignKey(nameof(SubmittedByUserId))]
    public User SubmittedByUser { get; set; } = null!;


    public ICollection<VerificationDocument> VerificationDocuments { get; set; } = new List<VerificationDocument>();
}