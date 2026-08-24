using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public class UpdateOwnerParkingConfigurationRequest
{
    [Required]
    [StringLength(2000)]
    public string Description { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.01", "9999.99")]
    public decimal DailyRate { get; set; }

    [Range(typeof(decimal), "0.01", "99999.99")]
    public decimal MonthlyRate { get; set; }
}

public class OwnerParkingConfigurationResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ParkingSpotId { get; set; }
    public bool IsConfigurationComplete { get; set; }
    public List<string> MissingRequirements { get; set; } = new();
    public DateTime UpdatedAt { get; set; }
}

public class UpdateOwnerParkingImageRequest
{
    [Range(1, int.MaxValue)]
    public int DisplayOrder { get; set; }

    public bool IsPrimary { get; set; }
}

public class OwnerParkingImageResponse
{
    public int ParkingSpotImageId { get; set; }
    public int MediaFileId { get; set; }
    public string SecureUrl { get; set; } = string.Empty;
    public string? OriginalFileName { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPrimary { get; set; }
}

public class OwnerParkingImagesResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ParkingSpotId { get; set; }
    public List<OwnerParkingImageResponse> Data { get; set; } = new();
}
