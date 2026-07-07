using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Models;

public class User
{
    [Key]
    public int UserId { get; set; }

    [Required]
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [StringLength(20)]
    public string? PhoneNumber { get; set; }

    [Required]
    [StringLength(30)]
    public UserType UserType { get; set; }

    public int? ProfileImageId { get; set; }

    public VerificationStatus VerificationStatus { get; set; }

    [Required]
    [StringLength(20)]
    public string AccountStatus { get; set; } = "Active";

    public string? RefreshToken { get; set; }

    public DateTime? RefreshTokenExpiryTime { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public DateTime? EmailVerifiedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation Properties

    public MediaFile? ProfileImage { get; set; }

    public Wallet? Wallet { get; set; }

    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();

    // Parking Spots owned by this user
    public ICollection<ParkingSpot> OwnedParkingSpots { get; set; } = new List<ParkingSpot>();

    // Bookings made by this user
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();

    public ICollection<Review> Reviews { get; set; } = new List<Review>();

    public ICollection<AccessLog> AccessLogs { get; set; } = new List<AccessLog>();

    public ICollection<ParkingVerificationRequest> SubmittedVerificationRequests { get; set; } = new List<ParkingVerificationRequest>();

}