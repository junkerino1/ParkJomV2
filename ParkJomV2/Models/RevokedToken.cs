using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ParkJomV2.Models;

[Table("RevokedTokens")]
public class RevokedToken
{
    [Key]
    public int RevokedTokenId { get; set; }

    [Required]
    [StringLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public int? UserId { get; set; }

    public DateTime RevokedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAt { get; set; }
}
