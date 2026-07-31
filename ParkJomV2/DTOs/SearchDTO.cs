using ParkJomV2.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public class SearchParkingQuery
{
    public string? Query { get; set; }
    public int? StationId { get; set; }
    public decimal? MinLatitude { get; set; }
    public decimal? MaxLatitude { get; set; }
    public decimal? MinLongitude { get; set; }
    public decimal? MaxLongitude { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public DayType? DayType { get; set; }
    public int? Page { get; set; } = 1;
    public int? PageSize { get; set; } = 20;
}

public class NearbyParkingQuery
{
    [Required]
    public decimal Latitude { get; set; }

    [Required]
    public decimal Longitude { get; set; }

    public double RadiusKm { get; set; } = 2.0;

    public int? Page { get; set; } = 1;
    public int? PageSize { get; set; } = 20;
}

public class FilterParkingQuery
{
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public int? StationId { get; set; }
    public PropertyType? PropertyType { get; set; }
    public DayType? DayType { get; set; }
    public bool? HasIotDevice { get; set; }
    public int? Page { get; set; } = 1;
    public int? PageSize { get; set; } = 20;
}

public class ParkingSearchResultDTO
{
    public int ParkingSpotId { get; set; }
    public string? ParkingLabel { get; set; }
    public int PropertyId { get; set; }
    public string? PropertyName { get; set; }
    public string? Address { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string? StationName { get; set; }
    public decimal DistanceToStation { get; set; }
    public decimal TimeToStationInMinutes { get; set; }
    public string AvailabilityStatus { get; set; } = string.Empty;
    public decimal? MonthlyRate { get; set; }
    public decimal? DailyRate { get; set; }
    public string? PrimaryImageUrl { get; set; }
}

public class SearchParkingResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<ParkingSearchResultDTO> Data { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class CreateParkingSpotRequest
{
    [Required]
    public int PropertyId { get; set; }

    [Required]
    [StringLength(50)]
    public string BayNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Level { get; set; } = string.Empty;
}

public class UpdateParkingSpotRequest
{
    [StringLength(50)]
    public string? ParkingLabel { get; set; }

    public decimal? MonthlyRate { get; set; }

    public decimal? DailyRate { get; set; }

    public bool? IsPublished { get; set; }

    public AvailabilityStatus? AvailabilityStatus { get; set; }
}

public class ParkingSpotDetailDTO
{
    public int ParkingSpotId { get; set; }
    public int PropertyId { get; set; }
    public string? PropertyName { get; set; }
    public string? Address { get; set; }
    public int OwnerId { get; set; }
    public string? OwnerName { get; set; }
    public string? ParkingLabel { get; set; }
    public AvailabilityStatus AvailabilityStatus { get; set; }
    public VerificationStatus VerificationStatus { get; set; }
    public decimal? MonthlyRate { get; set; }
    public decimal? DailyRate { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ParkingSpotImageDTO> Images { get; set; } = new();
}

public class ParkingSpotDetailResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public ParkingSpotDetailDTO? Data { get; set; }
}

public class CreateParkingSpotResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? ParkingSpotId { get; set; }
}

public class UpdateParkingSpotResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ParkingSpotId { get; set; }
}

public class DeleteParkingSpotResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
