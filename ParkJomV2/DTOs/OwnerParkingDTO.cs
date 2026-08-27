using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

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

public class CreateOwnerAvailabilityRulesRequest
{
    [Required]
    public List<CreateOwnerAvailabilityRuleRequest> Rules { get; set; } = new();
}

public class CreateOwnerAvailabilityRuleRequest
{
    [Required]
    [RegularExpression("^\\d{4}-\\d{2}-\\d{2}$", ErrorMessage = "fromDate must use YYYY-MM-DD format.")]
    public string FromDate { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^\\d{4}-\\d{2}-\\d{2}$", ErrorMessage = "toDate must use YYYY-MM-DD format.")]
    public string ToDate { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(?:[01]\\d|2[0-3]):[0-5]\\d$", ErrorMessage = "fromTime must use HH:mm format.")]
    public string FromTime { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(?:[01]\\d|2[0-3]):[0-5]\\d$", ErrorMessage = "toTime must use HH:mm format.")]
    public string ToTime { get; set; } = string.Empty;

    [Required]
    public OwnerAvailabilityDayPattern? DayPattern { get; set; }
}

public class UpdateOwnerAvailabilityRuleRequest : CreateOwnerAvailabilityRuleRequest
{
}

[JsonConverter(typeof(JsonStringEnumConverter<OwnerAvailabilityDayPattern>))]
public enum OwnerAvailabilityDayPattern
{
    Weekdays = 1,
    Everyday = 2
}

public class OwnerAvailabilityRuleResponse
{
    public int AvailabilityRuleId { get; set; }
    public string FromDate { get; set; } = string.Empty;
    public string ToDate { get; set; } = string.Empty;
    public string FromTime { get; set; } = string.Empty;
    public string ToTime { get; set; } = string.Empty;
    public OwnerAvailabilityDayPattern DayPattern { get; set; }
}

public class OwnerAvailabilityRulesResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ParkingSpotId { get; set; }
    public string TimeZone { get; set; } = "Asia/Kuala_Lumpur";
    public List<OwnerAvailabilityRuleResponse> Data { get; set; } = new();
}

public class OwnerAvailabilityTimeRangeResponse
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class OwnerAvailabilityCalendarDayResponse
{
    public string Date { get; set; } = string.Empty;
    public List<OwnerAvailabilityTimeRangeResponse> ConfiguredHours { get; set; } = new();
    public string Status { get; set; } = string.Empty;
}

public class OwnerAvailabilityCalendarResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ParkingSpotId { get; set; }
    public string Month { get; set; } = string.Empty;
    public string TimeZone { get; set; } = "Asia/Kuala_Lumpur";
    public List<OwnerAvailabilityCalendarDayResponse> Days { get; set; } = new();
}
