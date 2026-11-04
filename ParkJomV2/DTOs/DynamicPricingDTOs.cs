using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public class RevenueCurvePointDto
{
    public decimal DailyRateRm { get; set; }
    public int ProjectedOccupancyPercent { get; set; }
    public decimal ProjectedMonthlyNetRevenueRm { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsRecommended { get; set; }
}

public class SpotPricingRecommendationDto
{
    public int ParkingSpotId { get; set; }
    public string ParkingLabel { get; set; } = string.Empty;
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public int StationId { get; set; }
    public string StationName { get; set; } = string.Empty;
    public decimal DistanceToStationKm { get; set; }
    public decimal TimeToStationMinutes { get; set; }

    public decimal? CurrentDailyRateRm { get; set; }
    public decimal? CurrentMonthlyRateRm { get; set; }

    public decimal RecommendedDailyRateRm { get; set; }
    public decimal RecommendedMonthlyRateRm { get; set; }
    public decimal RecommendedOffPeakHourlyRm { get; set; }

    public decimal StationMarketMedianDailyRm { get; set; }
    public decimal StationMarketP25DailyRm { get; set; }
    public decimal StationMarketP75DailyRm { get; set; }
    public int CompetingSpotsCount { get; set; }

    public double PriceElasticity { get; set; }

    public int ProjectedOccupancyNowPercent { get; set; }
    public int ProjectedOccupancyOptimalPercent { get; set; }
    public int OccupancyDeltaPercent { get; set; }

    public decimal CurrentMonthlyNetRevenueRm { get; set; }
    public decimal ProjectedMonthlyNetRevenueRm { get; set; }
    public decimal ExtraMonthlyRevenueRm { get; set; }

    public string StrategyTag { get; set; } = string.Empty;
    public string HeadlineSuggestion { get; set; } = string.Empty;
    public string? EventAlert { get; set; }
    public List<string> Factors { get; set; } = new();
    public List<RevenueCurvePointDto> RevenueCurve { get; set; } = new();
}

public class OwnerPortfolioPricingResponseDto
{
    public int Code { get; set; } = StatusCodes.Status200OK;
    public bool Success { get; set; } = true;
    public string Message { get; set; } = string.Empty;
    public int TotalBaysCount { get; set; }
    public int OptimizableBaysCount { get; set; }
    public decimal TotalCurrentMonthlyRevenueRm { get; set; }
    public decimal TotalProjectedMonthlyRevenueRm { get; set; }
    public decimal TotalExtraMonthlyRevenueRm { get; set; }
    public int AverageOccupancyBoostPercent { get; set; }
    public string PortfolioHeadline { get; set; } = string.Empty;
    public List<SpotPricingRecommendationDto> Recommendations { get; set; } = new();
}

public class SpotPricingResponseWrapperDto
{
    public int Code { get; set; } = StatusCodes.Status200OK;
    public bool Success { get; set; } = true;
    public string Message { get; set; } = string.Empty;
    public SpotPricingRecommendationDto Data { get; set; } = new();
}

public class ApplyDynamicPricingRequest
{
    [Range(1, 500)]
    public decimal DailyRateRm { get; set; }

    [Range(10, 5000)]
    public decimal? MonthlyRateRm { get; set; }
}
