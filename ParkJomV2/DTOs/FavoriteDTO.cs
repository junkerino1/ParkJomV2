namespace ParkJomV2.DTOs;

public class FavoriteDTO
{
    public int FavoriteId { get; set; }
    public int ParkingSpotId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AddFavoriteResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public FavoriteDTO? Data { get; set; }
}

public class UpdateFavoriteDTO
{
    public int ParkingSpotId { get; set; }
    public bool IsFavorite { get; set; }
}

public class UpdateFavoriteResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public UpdateFavoriteDTO? Data { get; set; }
}

public class FavoriteParkingSpotDTO
{
    public int FavoriteId { get; set; }
    public int ParkingSpotId { get; set; }
    public string? ParkingLabel { get; set; }
    public int PropertyId { get; set; }
    public string? PropertyName { get; set; }
    public string? Address { get; set; }
    public string? StationName { get; set; }
    public decimal DistanceToStation { get; set; }
    public decimal TimeToStationInMinutes { get; set; }
    public string AvailabilityStatus { get; set; } = string.Empty;
    public decimal? MonthlyRate { get; set; }
    public decimal? DailyRate { get; set; }
    public string? PrimaryImageUrl { get; set; }
    public DateTime FavoritedAt { get; set; }
}

public class GetFavoritesResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public List<FavoriteParkingSpotDTO> Data { get; set; } = new();
}
