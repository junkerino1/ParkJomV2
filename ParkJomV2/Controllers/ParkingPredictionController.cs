using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.Services.Prediction;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/prediction")]
public class ParkingPredictionController : ControllerBase
{
    private readonly ParkingPredictorService _predictor;

    public ParkingPredictionController(ParkingPredictorService predictor)
    {
        _predictor = predictor;
    }

    /// <summary>
    /// Predicts multi-horizon occupancy, probability of filling, weather/transit impact,
    /// and 06:00–22:00 demand curve for a specific parking spot.
    /// </summary>
    [HttpGet("spot/{parkingSpotId:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> PredictSpot(
        int parkingSpotId,
        [FromQuery] DateTime? targetTime,
        CancellationToken ct)
    {
        var forecast = await _predictor.PredictSpotCongestionAsync(parkingSpotId, targetTime, ct);
        if (forecast == null)
        {
            return NotFound(new
            {
                code = 404,
                success = false,
                message = $"Parking spot #{parkingSpotId} was not found."
            });
        }

        return Ok(new
        {
            code = 200,
            success = true,
            data = forecast
        });
    }

    /// <summary>
    /// Predicts station-wide Park-and-Ride cluster congestion and hourly curve for a GTFS station stopId.
    /// </summary>
    [HttpGet("station/{stopId}")]
    [AllowAnonymous]
    public async Task<IActionResult> PredictStation(
        string stopId,
        [FromQuery] DateTime? targetTime,
        CancellationToken ct)
    {
        var forecast = await _predictor.PredictStationCongestionAsync(stopId, targetTime, ct);
        if (forecast == null)
        {
            return NotFound(new
            {
                code = 404,
                success = false,
                message = $"Transit station '{stopId}' was not found."
            });
        }

        return Ok(new
        {
            code = 200,
            success = true,
            data = forecast
        });
    }

    /// <summary>
    /// Returns ML.NET FastTree Regression &amp; Binary Classification evaluation metrics
    /// (R², MAE, RMSE, ROC-AUC, Accuracy, F1 Score, Feature Importances) for FYP evaluation.
    /// </summary>
    [HttpGet("metrics")]
    [AllowAnonymous]
    public IActionResult GetMetrics()
    {
        var metrics = _predictor.GetModelMetrics();
        return Ok(new
        {
            code = 200,
            success = true,
            data = metrics
        });
    }

    /// <summary>
    /// Triggers on-demand ML.NET FastTree model retraining blending live database bookings with calibrated Klang Valley data.
    /// </summary>
    [HttpPost("retrain")]
    [AllowAnonymous]
    public async Task<IActionResult> RetrainModel(CancellationToken ct)
    {
        var metrics = await _predictor.RetrainModelAsync(ct);
        return Ok(new
        {
            code = 200,
            success = true,
            message = "ML.NET FastTree regression and binary classification models retrained successfully.",
            data = metrics
        });
    }
}
