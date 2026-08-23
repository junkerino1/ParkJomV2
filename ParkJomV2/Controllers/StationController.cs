using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.Controllers
{
    [ApiController]
    [Route("api/station")]
    public class StationController : ControllerBase
    {

        private readonly ApplicationDbContext _context;
        private readonly AccessLogService _accessLogService;
        private readonly ILogger<StationController> _logger;

        public StationController(ApplicationDbContext context, AccessLogService accessLogService, ILogger<StationController> logger)
        {
            _context = context;
            _accessLogService = accessLogService;
            _logger = logger;
        }

        /// <summary>
        /// get all property with available parking spot by station id
        /// </summary>
        [HttpGet("get-property/{stationId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<GetPropertyResponse>> GetPropertyByStationId(int stationId)
        {
            var station = await _context.Stations.FindAsync(stationId);

            // return Ok(station);

            if (station == null)
            {
                await _accessLogService.LogAsync(User, "GetPropertyByStationId", false, $"Station not found (id={stationId})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Station not found."
                });
            }

            var properties = await _context.Properties
                .Where(p => p.NearestStationId == stationId)
                .Select(p => new GetPropertyResponse
                {
                    PropertyId = p.PropertyId,
                    PropertyName = p.PropertyName,
                    PropertyType = p.PropertyType,
                    Address = p.Address,
                    Latitude = p.Latitude,
                    Longitude = p.Longitude,
                    DistanceToStation = p.DistanceToStation
                })
                .ToListAsync();

            if (properties.Count == 0)
            {
                await _accessLogService.LogAsync(User, "GetPropertyByStationId", false, $"No properties (stationId={stationId})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "No properties found for the given station."
                });
            }
            else
            {
                await _accessLogService.LogAsync(User, "GetPropertyByStationId", true, $"StationId={stationId}");
                return Ok(new
                {
                    Code = StatusCodes.Status200OK,
                    Success = true,
                    Message = "Properties retrieved successfully.",
                    Properties = properties
                });
            }

        }

        [HttpGet("get-parking-spot/{propertyId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<GetPropertyResponse>> GetParkingSpotByPropertyId(int propertyId)
        {
            var property = await _context.Properties.FindAsync(propertyId);

            //return Ok(property);

            if (property == null)
            {
                await _accessLogService.LogAsync(User, "GetParkingSpotByPropertyId", false, $"Property not found (id={propertyId})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Property not found."
                });
            }

            if (property.ParkingSpots == null || property.ParkingSpots.Count == 0)
            {
                await _accessLogService.LogAsync(User, "GetParkingSpotByPropertyId", false, $"No parking spots (propertyId={propertyId})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "No parking spots found for the given property."
                });
            }

            List<ParkingSpot> availableParkingSpots = property.ParkingSpots
                .Where(ps => ps.AvailabilityStatus == AvailabilityStatus.Available)
                .Where(ps => ps.IsPublished == true)
                .ToList();

            await _accessLogService.LogAsync(User, "GetParkingSpotByPropertyId", true, $"PropertyId={propertyId}");

            return Ok(new
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Parking spots retrieved successfully.",
                ParkingSpots = availableParkingSpots.Select(ps => new
                {
                    ps.ParkingSpotId,
                    ps.ParkingLabel,
                    ps.AvailabilityStatus,
                    ps.MonthlyRate,
                    ps.DailyRate,
                })
            });
        }
    }
}