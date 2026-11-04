namespace ParkJomV2.DTOs;

public class ParkingForecastResponseDto
{
    public int Code { get; set; } = 200;
    public bool Success { get; set; } = true;
    public string Message { get; set; } = string.Empty;

    public string TargetType { get; set; } = "spot"; // "spot" or "station"
    public int? ParkingSpotId { get; set; }
    public string StopId { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public string ParkingLabel { get; set; } = string.Empty;

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateTime TargetTime { get; set; } = DateTime.UtcNow;

    public int PredictedOccupancyPercent { get; set; }
    public double CurrentOccupancyPercent { get; set; }
    public double ProbabilityFull { get; set; }
    public string RiskLevel { get; set; } = "Low"; // "Low", "Moderate", "High", "Critical"
    public string? EstimatedFullBy { get; set; }
    public string RecommendedArriveBefore { get; set; } = "07:45 AM";
    public string Recommendation { get; set; } = string.Empty;
    public string HeadlineRecommendation { get; set; } = string.Empty;
    public string ModelSummary { get; set; } = "ML.NET FastTree GBDT (Regression + Binary Classification)";

    public int TotalBaysInCluster { get; set; }
    public int CurrentlyOccupiedBays { get; set; }
    public int ExpectedSpotsLeft { get; set; }
    public int RecentQuoteCount60m { get; set; }
    public double WalkDistanceMeters { get; set; }
    public decimal DailyRateRm { get; set; }

    public WeatherContextDto Weather { get; set; } = new();
    public TransitContextDto Transit { get; set; } = new();
    public List<PredictionFactorDto> RiskFactors { get; set; } = new();
    public List<HorizonForecastDto> Horizons { get; set; } = new();
    public List<HourlyCurvePointDto> HourlyCurve { get; set; } = new();
}

public class WeatherContextDto
{
    public string Condition { get; set; } = "Partly Cloudy";
    public double RainProbability { get; set; }
    public double RainProbabilityPercent
    {
        get => RainProbability;
        set => RainProbability = value;
    }
    public double PrecipitationMm { get; set; }
    public double TemperatureC { get; set; } = 29.5;
    public bool IsRaining { get; set; }
    public string Source { get; set; } = "Open-Meteo Klang Valley Forecast";
}

public class TransitContextDto
{
    public string StopId { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public string RouteName { get; set; } = "Rapid KL Rail";
    public string LineName
    {
        get => RouteName;
        set => RouteName = value;
    }
    public int FrequencyMinutes { get; set; } = 5;
    public int HeadwayMinutes
    {
        get => FrequencyMinutes;
        set => FrequencyMinutes = value;
    }
    public int TrainsPerHour { get; set; } = 12;
    public bool IsPeakHour { get; set; }
    public bool IsPeakHeadway
    {
        get => IsPeakHour;
        set => IsPeakHour = value;
    }
    public int NextTrainMinutes { get; set; } = 3;
}

public class PredictionFactorDto
{
    public string FeatureKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
    public double ImpactPercent { get; set; }
    public string Direction { get; set; } = "up"; // "up", "down", "neutral"
    public string Category { get; set; } = "temporal";
    public string Description { get; set; } = string.Empty;
}

public class HorizonForecastDto
{
    public string HorizonKey { get; set; } = string.Empty;
    public string HorizonLabel { get; set; } = string.Empty;
    public string Label
    {
        get => HorizonLabel;
        set => HorizonLabel = value;
    }
    public int OffsetMinutes { get; set; }
    public string TargetTime { get; set; } = string.Empty;
    public string TargetTimeLabel
    {
        get => TargetTime;
        set => TargetTime = value;
    }
    public int PredictedOccupancyPercent { get; set; }
    public double ProbabilityFull { get; set; }
    public int ExpectedSpotsLeft { get; set; }
    public string RiskLevel { get; set; } = "Low";
}

public class HourlyCurvePointDto
{
    public int Hour { get; set; }
    public string TimeLabel { get; set; } = string.Empty;
    public string Label
    {
        get => TimeLabel;
        set => TimeLabel = value;
    }
    public int PredictedOccupancyPercent { get; set; }
    public double ProbabilityFull { get; set; }
    public int ExpectedSpotsLeft { get; set; }
    public string RiskLevel { get; set; } = "Low";
    public bool IsPeakWindow { get; set; }
    public bool IsCurrentHour { get; set; }
}

public class ModelMetricsResponseDto
{
    public int Code { get; set; } = 200;
    public bool Success { get; set; } = true;
    public string Message { get; set; } = string.Empty;

    public string Algorithm { get; set; } = "ML.NET FastTree (Gradient Boosted Decision Trees)";
    public string ModelArchitecture
    {
        get => Algorithm;
        set => Algorithm = value;
    }
    public int TrainingRows { get; set; }
    public int TrainingRowsCount
    {
        get => TrainingRows;
        set => TrainingRows = value;
    }
    public int SyntheticRowsCount { get; set; } = 3600;
    public int LiveDatabaseRowsCount { get; set; }
    public DateTime LastTrainedAt { get; set; } = DateTime.UtcNow;
    public double TrainingDurationMs { get; set; } = 142.5;

    public RegressionMetricsDto Regression { get; set; } = new();
    public RegressionMetricsDto RegressionMetrics
    {
        get => Regression;
        set => Regression = value;
    }
    public ClassificationMetricsDto BinaryClassification { get; set; } = new();
    public ClassificationMetricsDto ClassificationMetrics
    {
        get => BinaryClassification;
        set => BinaryClassification = value;
    }
    public List<FeatureImportanceDto> FeatureImportances { get; set; } = new();
}

public class RegressionMetricsDto
{
    public double RSquared { get; set; }
    public double MeanAbsoluteError { get; set; }
    public double RootMeanSquaredError { get; set; }
    public double MeanSquaredError { get; set; }
}

public class ClassificationMetricsDto
{
    public double AreaUnderRocCurve { get; set; }
    public double Accuracy { get; set; }
    public double F1Score { get; set; }
    public double PositivePrecision { get; set; }
    public double PositiveRecall { get; set; }
    public double AreaUnderPrecisionRecallCurve { get; set; }
}

public class FeatureImportanceDto
{
    public string FeatureName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public double ImportanceWeight { get; set; }
    public double ImportancePercent
    {
        get => Math.Round(ImportanceWeight * 100.0, 1);
        set => ImportanceWeight = value / 100.0;
    }
    public double RSquaredDelta { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
