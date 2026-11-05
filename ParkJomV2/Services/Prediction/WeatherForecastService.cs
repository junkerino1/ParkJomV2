using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using ParkJomV2.DTOs;

namespace ParkJomV2.Services.Prediction;

/// <summary>
/// Queries the free, keyless Open-Meteo API for Klang Valley station coordinates
/// to obtain live rainfall, precipitation probability, and temperature for ML inference.
/// Cached for 30 minutes per rounded coordinate grid to prevent rate-limiting.
/// </summary>
public class WeatherForecastService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WeatherForecastService> _logger;

    public WeatherForecastService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<WeatherForecastService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    public Task<WeatherContextDto> GetWeatherContextAsync(
        double latitude,
        double longitude,
        CancellationToken ct = default)
    {
        return GetWeatherForCoordinateAsync(latitude, longitude, DateTime.UtcNow.AddHours(8), ct);
    }

    public async Task<WeatherContextDto> GetWeatherForCoordinateAsync(
        double latitude,
        double longitude,
        DateTime targetLocalTime,
        CancellationToken ct = default)
    {
        // Default to central Kuala Lumpur if coordinates are unconfigured (0,0)
        if (Math.Abs(latitude) < 0.01 && Math.Abs(longitude) < 0.01)
        {
            latitude = 3.1478;
            longitude = 101.6953;
        }

        var gridLat = Math.Round(latitude, 2);
        var gridLng = Math.Round(longitude, 2);
        var cacheKey = $"openmeteo:{gridLat.ToString(CultureInfo.InvariantCulture)}:{gridLng.ToString(CultureInfo.InvariantCulture)}:{targetLocalTime:yyyyMMddHH}";

        if (_cache.TryGetValue(cacheKey, out WeatherContextDto? cached) && cached != null)
        {
            return cached;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3.5));

            var client = _httpClientFactory.CreateClient();
            var url = string.Format(
                CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0:F4}&longitude={1:F4}&current=temperature_2m,precipitation,weather_code&hourly=precipitation_probability,precipitation&timezone=Asia%2FKuala_Lumpur&forecast_days=2",
                gridLat,
                gridLng);

            using var response = await client.GetAsync(url, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
                var root = doc.RootElement;

                double tempC = 29.5;
                double currentPrecip = 0.0;
                int weatherCode = 1;

                if (root.TryGetProperty("current", out var currentEl))
                {
                    if (currentEl.TryGetProperty("temperature_2m", out var tEl) && tEl.TryGetDouble(out var t))
                        tempC = t;
                    if (currentEl.TryGetProperty("precipitation", out var pEl) && pEl.TryGetDouble(out var p))
                        currentPrecip = p;
                    if (currentEl.TryGetProperty("weather_code", out var wEl) && wEl.TryGetInt32(out var w))
                        weatherCode = w;
                }

                double rainProb = 20.0;
                double hourlyPrecip = currentPrecip;

                if (root.TryGetProperty("hourly", out var hourlyEl) &&
                    hourlyEl.TryGetProperty("time", out var timesEl) &&
                    timesEl.ValueKind == JsonValueKind.Array)
                {
                    var targetHourPrefix = targetLocalTime.ToString("yyyy-MM-ddTHH", CultureInfo.InvariantCulture);
                    int matchedIndex = -1;
                    int idx = 0;
                    foreach (var timeItem in timesEl.EnumerateArray())
                    {
                        var timeStr = timeItem.GetString();
                        if (timeStr != null && timeStr.StartsWith(targetHourPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedIndex = idx;
                            break;
                        }
                        idx++;
                    }

                    if (matchedIndex >= 0)
                    {
                        if (hourlyEl.TryGetProperty("precipitation_probability", out var probArr) &&
                            probArr.ValueKind == JsonValueKind.Array &&
                            matchedIndex < probArr.GetArrayLength() &&
                            probArr[matchedIndex].TryGetDouble(out var probVal))
                        {
                            rainProb = probVal;
                        }

                        if (hourlyEl.TryGetProperty("precipitation", out var precArr) &&
                            precArr.ValueKind == JsonValueKind.Array &&
                            matchedIndex < precArr.GetArrayLength() &&
                            precArr[matchedIndex].TryGetDouble(out var precVal))
                        {
                            hourlyPrecip = Math.Max(currentPrecip, precVal);
                        }
                    }
                }

                var isRaining = hourlyPrecip >= 0.4 || rainProb >= 65 || weatherCode >= 51;
                var result = new WeatherContextDto
                {
                    Condition = DescribeWmoCode(weatherCode, rainProb, hourlyPrecip),
                    RainProbabilityPercent = Math.Round(Math.Clamp(rainProb, 0, 100), 0),
                    PrecipitationMm = Math.Round(Math.Max(0, hourlyPrecip), 1),
                    TemperatureC = Math.Round(tempC, 1),
                    IsRaining = isRaining,
                    Source = "Open-Meteo Live Telemetry"
                };

                _cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Open-Meteo weather lookup timed out or failed; using Klang Valley diurnal baseline.");
        }

        var fallback = BuildDiurnalKlangValleyBaseline(targetLocalTime);
        _cache.Set(cacheKey, fallback, TimeSpan.FromMinutes(10));
        return fallback;
    }

    private static WeatherContextDto BuildDiurnalKlangValleyBaseline(DateTime targetLocalTime)
    {
        // Tropical Klang Valley climatology: afternoon convective showers (15:00 - 19:00) and warm mornings
        var hour = targetLocalTime.Hour;
        double rainProb = hour switch
        {
            >= 15 and <= 18 => 55.0,
            >= 7 and <= 9 => 25.0,
            _ => 20.0
        };
        double temp = hour switch
        {
            >= 12 and <= 16 => 32.2,
            >= 7 and <= 11 => 28.4,
            _ => 26.8
        };

        return new WeatherContextDto
        {
            Condition = rainProb >= 50 ? "Scattered Afternoon Clouds" : "Warm & Partly Cloudy",
            RainProbabilityPercent = rainProb,
            PrecipitationMm = rainProb >= 55 ? 0.6 : 0.0,
            TemperatureC = temp,
            IsRaining = false,
            Source = "Klang Valley Climatology Baseline"
        };
    }

    private static string DescribeWmoCode(int code, double rainProb, double precipMm)
    {
        if (code >= 95) return "Thunderstorm";
        if (code >= 80 || precipMm >= 2.5) return "Heavy Rain Showers";
        if (code >= 61 || precipMm >= 0.5) return "Moderate Rain";
        if (code >= 51 || rainProb >= 60) return "Light Drizzle / Rain Risk";
        if (code is 2 or 3) return "Partly Cloudy";
        return "Clear / Fair";
    }
}
