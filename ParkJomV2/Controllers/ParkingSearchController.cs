using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/parking")]
public class ParkingSearchController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<ParkingSearchController> _logger;
    private readonly OsrmService _osrmService;

    public ParkingSearchController(ApplicationDbContext context, AccessLogService accessLogService, ILogger<ParkingSearchController> logger, OsrmService osrmService)
    {
        _context = context;
        _accessLogService = accessLogService;
        _logger = logger;
        _osrmService = osrmService;
    }

    /// <summary>
    /// Search parking spots by places name, station name, or property name
    /// </summary>
    [AllowAnonymous]
    [HttpGet("search")]
    [ProducesResponseType(typeof(SearchParkingResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SearchParkingResponse>> SearchParking(
        [FromQuery] string? query,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var queryable = _context.ParkingSpots
                .Include(ps => ps.Property)
                    .ThenInclude(p => p.Station)
                .Include(ps => ps.VerificationRequests.Where(vr => vr.IsCurrent))
                .Include(ps => ps.ParkingSpotImages.Where(psi => psi.IsPrimary))
                    .ThenInclude(psi => psi.MediaFile)
                .Where(ps =>
                        ps.IsPublished &&
                        ps.AvailabilityStatus == AvailabilityStatus.Available &&
                        !ps.IsSuspensionLocked &&
                        ps.Owner.AccountStatus != "Suspended")
                .AsQueryable();

            // Filter by text query — searches Property table for property name
            // and Station table for station name, then finds matching parking spots
            if (!string.IsNullOrWhiteSpace(query))
            {
                var q = query.Trim().ToLower();

                // 1) Find property IDs matching the property name
                var propertyIdsByName = await _context.Properties
                    .Where(p => p.PropertyName.ToLower().Contains(q))
                    .Select(p => p.PropertyId)
                    .ToListAsync();

                // 2) Find station IDs matching the station name
                var stationIdsByName = await _context.Stations
                    .Where(s => s.StationName.ToLower().Contains(q))
                    .Select(s => s.StationId)
                    .ToListAsync();

                // 3) Find property IDs whose nearest station matches
                var propertyIdsByStation = await _context.Properties
                    .Where(p => stationIdsByName.Contains(p.NearestStationId))
                    .Select(p => p.PropertyId)
                    .ToListAsync();

                // 4) Combine all matching property IDs
                var matchingPropertyIds = propertyIdsByName
                    .Union(propertyIdsByStation)
                    .ToList();


                // 5) Filter parking spots by those property IDs
                queryable = queryable.Where(ps => matchingPropertyIds.Contains(ps.PropertyId));
            }

            var totalCount = await queryable.CountAsync();

            var spots = await queryable
                .OrderByDescending(ps => ps.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var result = spots.Select((ps, idx) => new ParkingSearchResultDTO
            {
                ParkingSpotId = ps.ParkingSpotId,
                ParkingLabel = ps.ParkingLabel,
                PropertyId = ps.PropertyId,
                PropertyName = ps.Property?.PropertyName,
                Address = ps.Property?.Address,
                Latitude = ps.Property?.Latitude ?? 0,
                Longitude = ps.Property?.Longitude ?? 0,
                StationName = ps.Property?.Station?.StationName,
                DistanceToStation = ps.Property?.DistanceToStation ?? 0,
                TimeToStationInMinutes = ps.Property?.TimeToStation ?? 0,
                AvailabilityStatus = ps.AvailabilityStatus.ToString(),
                MonthlyRate = ps.MonthlyRate,
                DailyRate = ps.DailyRate,
                PrimaryImageUrl = ps.ParkingSpotImages
                    .Where(psi => psi.IsPrimary)
                    .Select(psi => psi.MediaFile?.SecureUrl)
                    .FirstOrDefault()
            }).ToList();

            _logger.LogInformation("Search returned {Count} results (total {TotalCount})", result.Count, totalCount);

            return Ok(new SearchParkingResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = $"Found {totalCount} parking spot(s)",
                Data = result,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while searching parking spots"
            });
        }
    }

    /// <summary>
    /// Find parking spots near a geographic location using OSRM walking distance
    /// </summary>
    [AllowAnonymous]
    [HttpGet("nearby")]
    [ProducesResponseType(typeof(SearchParkingResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SearchParkingResponse>> GetNearbyParking(
        [FromQuery] decimal latitude,
        [FromQuery] decimal longitude,
        [FromQuery] double radiusKm = 2.0,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            // Step 1: Rough bounding box to get initial candidates
            double latDelta = radiusKm / 111.0;
            double lonDelta = radiusKm / (111.0 * Math.Cos((double)latitude * Math.PI / 180.0));

            decimal minLat = latitude - (decimal)latDelta;
            decimal maxLat = latitude + (decimal)latDelta;
            decimal minLon = longitude - (decimal)lonDelta;
            decimal maxLon = longitude + (decimal)lonDelta;

            var candidates = await _context.ParkingSpots
                .Include(ps => ps.Property)
                .ThenInclude(p => p.Station)
                .Include(ps => ps.VerificationRequests.Where(vr => vr.IsCurrent))
                .Include(ps => ps.ParkingSpotImages.Where(psi => psi.IsPrimary))
                    .ThenInclude(psi => psi.MediaFile)
                .Where(ps =>
                        ps.IsPublished &&
                        ps.AvailabilityStatus == AvailabilityStatus.Available &&
                        !ps.IsSuspensionLocked &&
                        ps.Owner.AccountStatus != "Suspended")
                .Where(ps => ps.Property != null
                    && ps.Property.Latitude >= minLat
                    && ps.Property.Latitude <= maxLat
                    && ps.Property.Longitude >= minLon
                    && ps.Property.Longitude <= maxLon)
                .ToListAsync();

            _logger.LogInformation("Found {Count} candidate parking spots in bounding box", candidates.Count);

            if (candidates.Count == 0)
            {
                return Ok(new SearchParkingResponse
                {
                    Code = StatusCodes.Status200OK,
                    Success = true,
                    Message = "Found 0 parking spot(s) nearby",
                    Data = new List<ParkingSearchResultDTO>(),
                    TotalCount = 0,
                    Page = page,
                    PageSize = pageSize
                });
            }

            var totalCount = candidates.Count;

            // paginate
            var pagedSpots = candidates
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var result = pagedSpots.Select(spot => new ParkingSearchResultDTO
            {
                ParkingSpotId = spot.ParkingSpotId,
                ParkingLabel = spot.ParkingLabel,
                PropertyId = spot.PropertyId,
                PropertyName = spot.Property?.PropertyName,
                Address = spot.Property?.Address,
                Latitude = spot.Property?.Latitude ?? 0,
                Longitude = spot.Property?.Longitude ?? 0,
                StationName = spot.Property?.Station?.StationName,
                DistanceToStation = spot.Property?.DistanceToStation ?? 0,
                TimeToStationInMinutes = spot.Property?.TimeToStation ?? 0,
                AvailabilityStatus = spot.AvailabilityStatus.ToString(),
                MonthlyRate = spot.MonthlyRate,
                DailyRate = spot.DailyRate,
                PrimaryImageUrl = spot.ParkingSpotImages
                    .Where(psi => psi.IsPrimary)
                    .Select(psi => psi.MediaFile?.SecureUrl)
                    .FirstOrDefault()
            }).ToList();

            return Ok(new SearchParkingResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = $"Found {totalCount} parking spot(s) nearby",
                Data = result,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding nearby parking spots");
            await _accessLogService.LogAsync(User, "GetNearbyParking", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while finding nearby parking spots"
            });
        }
    }

    /// <summary>
    /// Filter parking spots by price, station, property type, or IoT device availability
    /// </summary>
    [AllowAnonymous]
    [HttpGet("filter")]
    [ProducesResponseType(typeof(SearchParkingResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SearchParkingResponse>> FilterParking(
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] int? stationId,
        [FromQuery] PropertyType? propertyType,
        [FromQuery] DayType? dayType,
        [FromQuery] bool? hasIotDevice,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var queryable = _context.ParkingSpots
                .Include(ps => ps.Property)
                    .ThenInclude(p => p.Station)
                .Include(ps => ps.VerificationRequests.Where(vr => vr.IsCurrent))
                .Include(ps => ps.ParkingSpotImages.Where(psi => psi.IsPrimary))
                    .ThenInclude(psi => psi.MediaFile)
                .Include(ps => ps.IoTDevice)
                .Include(ps => ps.ParkingAvailabilities)
                .Where(ps =>
                        ps.IsPublished &&
                        ps.AvailabilityStatus == AvailabilityStatus.Available &&
                        !ps.IsSuspensionLocked &&
                        ps.Owner.AccountStatus != "Suspended")
                .AsQueryable();

            // Filter by price
            if (minPrice.HasValue)
                queryable = queryable.Where(ps => ps.DailyRate >= minPrice.Value || ps.MonthlyRate >= minPrice.Value);
            if (maxPrice.HasValue)
                queryable = queryable.Where(ps => ps.DailyRate <= maxPrice.Value || ps.MonthlyRate <= maxPrice.Value);

            // Filter by station
            if (stationId.HasValue)
                queryable = queryable.Where(ps => ps.Property != null && ps.Property.NearestStationId == stationId.Value);

            // Filter by property type
            if (propertyType.HasValue)
                queryable = queryable.Where(ps => ps.Property != null && ps.Property.PropertyType == propertyType.Value);

            // Filter by day type availability
            if (dayType.HasValue)
                queryable = queryable.Where(ps => ps.ParkingAvailabilities.Any(a => a.DayType == dayType.Value));

            // Filter by IoT device
            if (hasIotDevice.HasValue)
            {
                if (hasIotDevice.Value)
                    queryable = queryable.Where(ps => ps.IoTDevice != null);
                else
                    queryable = queryable.Where(ps => ps.IoTDevice == null);
            }

            var totalCount = await queryable.CountAsync();

            var spots = await queryable
                .OrderByDescending(ps => ps.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Calculate OSRM walking distances from station to property for each result
            var osrmTasks = spots.Select(async ps =>
            {
                var station = ps.Property?.Station;
                if (station != null)
                {
                    var (distKm, timeMin) = await _osrmService.GetWalkingDistanceAsync(
                        (double)station.Latitude, (double)station.Longitude,
                        (double)(ps.Property?.Latitude ?? 0), (double)(ps.Property?.Longitude ?? 0));
                    return (DistanceKm: distKm, TimeMinutes: timeMin);
                }
                return (DistanceKm: (double?)null, TimeMinutes: (double?)null);
            }).ToList();

            var osrmResults = await Task.WhenAll(osrmTasks);

            var result = spots.Select((ps, idx) => new ParkingSearchResultDTO
            {
                ParkingSpotId = ps.ParkingSpotId,
                ParkingLabel = ps.ParkingLabel,
                PropertyId = ps.PropertyId,
                PropertyName = ps.Property?.PropertyName,
                Address = ps.Property?.Address,
                Latitude = ps.Property?.Latitude ?? 0,
                Longitude = ps.Property?.Longitude ?? 0,
                StationName = ps.Property?.Station?.StationName,
                DistanceToStation = (decimal)(osrmResults[idx].DistanceKm ?? (double)(ps.Property?.DistanceToStation ?? 0)),
                TimeToStationInMinutes = (decimal)(osrmResults[idx].TimeMinutes ?? 0),
                AvailabilityStatus = ps.AvailabilityStatus.ToString(),
                MonthlyRate = ps.MonthlyRate,
                DailyRate = ps.DailyRate,
                PrimaryImageUrl = ps.ParkingSpotImages
                    .Where(psi => psi.IsPrimary)
                    .Select(psi => psi.MediaFile?.SecureUrl)
                    .FirstOrDefault()
            }).ToList();

            _logger.LogInformation("Filter returned {Count} results (total {TotalCount})", result.Count, totalCount);

            await _accessLogService.LogAsync(User, "FilterParking", true, $"total={totalCount}");

            return Ok(new SearchParkingResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = $"Found {totalCount} parking spot(s)",
                Data = result,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error filtering parking spots");
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while filtering parking spots"
            });
        }
    }
}
