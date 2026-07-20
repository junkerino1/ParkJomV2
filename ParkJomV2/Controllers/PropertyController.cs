using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.DTOs;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.Controllers
{
    [ApiController]
    [Route("api/property")]
    public class PropertyController : ControllerBase
    {

        private readonly ApplicationDbContext _context;
        private readonly ILogger<PropertyController> _logger;

        public PropertyController(ApplicationDbContext context, ILogger<PropertyController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Create a new property
        /// </summary>
        [HttpPost("create-property")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<PropertyDTO>> CreateProperty([FromBody] CreatePropertyRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Invalid request."
                });
            }

            var property = new Property
            {
                PropertyName = request.PropertyName,
                PropertyType = request.PropertyType,
                Address = request.Address,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                NearestStationId = request.NearestStationId,
                DistanceToStation = request.DistanceToStation,
                Description = request.Description,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            try
            {
                _context.Properties.Add(property);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Property created successfully with ID: {PropertyId}", property.PropertyId);

                return CreatedAtAction(nameof(GetProperty), new { id = property.PropertyId },
                    MapToDTO(property));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating property");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorResponse
                    {
                        Code = StatusCodes.Status500InternalServerError,
                        Success = false,
                        Message = "An error occurred while creating the property"
                    });
            }
        }

        /// <summary>
        /// Get property by ID
        /// </summary>
        [HttpGet("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PropertyDTO>> GetProperty(int id)
        {
            var property = await _context.Properties.FindAsync(id);
            if (property == null)
            {
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Property not found"
                });
            }

            return Ok(MapToDTO(property));
        }

        [HttpGet("stations")]
        public async Task<ActionResult<PropertyDTO>> getAllStations()
        {
            var property = await _context.Stations.ToListAsync();
            if (property == null)
            {
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Property not found"
                });
            }

            return Ok(property);
        }

        /// <summary>
        /// Get all properties
        /// </summary>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<PropertyDTO>>> GetAllProperties()
        {
            var properties = await _context.Properties.ToListAsync();
            return Ok(properties.Select(MapToDTO));
        }

        /// <summary>
        /// Update an existing property
        /// </summary>
        [HttpPut("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<PropertyDTO>> UpdateProperty(int id, [FromBody] UpdatePropertyRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Invalid request."
                });
            }

            var property = await _context.Properties.FindAsync(id);
            if (property == null)
            {
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Property not found"
                });
            }

            property.PropertyName = request.PropertyName ?? property.PropertyName;
            property.PropertyType = request.PropertyType ?? property.PropertyType;
            property.Address = request.Address ?? property.Address;
            property.Latitude = request.Latitude ?? property.Latitude;
            property.Longitude = request.Longitude ?? property.Longitude;
            property.NearestStationId = request.NearestStationId;
            property.DistanceToStation = request.DistanceToStation;
            property.Description = request.Description ?? property.Description;
            property.UpdatedAt = DateTime.UtcNow;

            try
            {
                _context.Properties.Update(property);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Property updated successfully with ID: {PropertyId}", property.PropertyId);

                return Ok(MapToDTO(property));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating property");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorResponse
                    {
                        Code = StatusCodes.Status500InternalServerError,
                        Success = false,
                        Message = "An error occurred while updating the property"
                    });
            }
        }

        /// <summary>
        /// Delete a property
        /// </summary>
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteProperty(int id)
        {
            var property = await _context.Properties.FindAsync(id);
            if (property == null)
            {
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Property not found"
                });
            }

            try
            {
                _context.Properties.Remove(property);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Property deleted successfully with ID: {PropertyId}", id);

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting property");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorResponse
                    {
                        Code = StatusCodes.Status500InternalServerError,
                        Success = false,
                        Message = "An error occurred while deleting the property"
                    });
            }
        }
        private static PropertyDTO MapToDTO(Property property)
        {
            return new PropertyDTO
            {
                PropertyId = property.PropertyId,
                PropertyName = property.PropertyName,
                PropertyType = property.PropertyType,
                Address = property.Address,
                Latitude = property.Latitude,
                Longitude = property.Longitude,
                NearestStationId = property.NearestStationId,
                DistanceToStation = property.DistanceToStation,
                Description = property.Description,
                CreatedAt = property.CreatedAt,
                UpdatedAt = property.UpdatedAt
            };
        }
    }
}
