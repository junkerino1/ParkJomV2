using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using System.Security.Claims;

namespace ParkJomV2.Controllers
{
    [Route("api/vehicle")]
    [ApiController]
    public class VehicleController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly AccessLogService _accessLogService;
        private readonly ILogger<VehicleController> _logger;

        public VehicleController(ApplicationDbContext context, AccessLogService accessLogService, ILogger<VehicleController> logger)
        {
            _context = context;
            _accessLogService = accessLogService;
            _logger = logger;
        }

        /// <summary>
        /// Add a new vehicle for the authenticated user.
        /// </summary>
        [Authorize]
        [HttpPost("add")]
        [ProducesResponseType(typeof(AddVehicleResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<AddVehicleResponse>> AddVehicle([FromBody] AddVehicleRequest request)
        {
            if (!ModelState.IsValid)
            {
                await _accessLogService.LogAsync(User, "AddVehicle", false, "Invalid request");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Invalid request."
                });
            }

            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

                if (user == null)
                {
                    await _accessLogService.LogAsync(User, "AddVehicle", false, "User not found");
                    return NotFound(new ErrorResponse
                    {
                        Code = StatusCodes.Status404NotFound,
                        Success = false,
                        Message = "User not found."
                    });
                }

                var duplicate = await _context.Vehicles.AnyAsync(v =>
                    v.UserId == userId &&
                    v.NumberPlate.ToLower() == request.NumberPlate.Trim().ToLower());

                if (duplicate)
                {
                    await _accessLogService.LogAsync(User, "AddVehicle", false, $"Duplicate number plate '{request.NumberPlate}'");
                    return BadRequest(new ErrorResponse
                    {
                        Code = StatusCodes.Status400BadRequest,
                        Success = false,
                        Message = "A vehicle with this number plate already exists."
                    });
                }

                var vehicle = new Vehicle
                {
                    UserId = userId,
                    NumberPlate = request.NumberPlate.Trim(),
                    VehicleBrand = request.VehicleBrand,
                    VehicleModel = request.VehicleModel,
                    VehicleColor = request.VehicleColor,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.Vehicles.Add(vehicle);
                await _context.SaveChangesAsync();

                await _accessLogService.LogAsync(User, "AddVehicle", true, $"VehicleId={vehicle.VehicleId}");
                return Ok(new AddVehicleResponse
                {
                    Code = StatusCodes.Status200OK,
                    Success = true,
                    Message = "Vehicle added successfully.",
                    Data = MapToDTO(vehicle)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding vehicle");
                await _accessLogService.LogAsync(User, "AddVehicle", false, ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    Code = StatusCodes.Status500InternalServerError,
                    Success = false,
                    Message = "An error occurred while adding the vehicle."
                });
            }
        }

        /// <summary>
        /// Modify an existing vehicle owned by the authenticated user.
        /// </summary>
        [Authorize]
        [HttpPut("modify")]
        [ProducesResponseType(typeof(ModifyVehicleResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ModifyVehicleResponse>> ModifyVehicle([FromBody] ModifyVehicleRequest request)
        {
            if (!ModelState.IsValid)
            {
                await _accessLogService.LogAsync(User, "ModifyVehicle", false, "Invalid request");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Invalid request."
                });
            }

            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

                if (user == null)
                {
                    await _accessLogService.LogAsync(User, "ModifyVehicle", false, "User not found");
                    return NotFound(new ErrorResponse
                    {
                        Code = StatusCodes.Status404NotFound,
                        Success = false,
                        Message = "User not found."
                    });
                }

                var vehicle = await _context.Vehicles.FirstOrDefaultAsync(v => v.VehicleId == request.VehicleId);
                if (vehicle == null)
                {
                    await _accessLogService.LogAsync(User, "ModifyVehicle", false, $"Vehicle not found (id={request.VehicleId})");
                    return NotFound(new ErrorResponse
                    {
                        Code = StatusCodes.Status404NotFound,
                        Success = false,
                        Message = "Vehicle not found."
                    });
                }

                if (vehicle.UserId != userId)
                {
                    await _accessLogService.LogAsync(User, "ModifyVehicle", false, $"Not authorized (id={request.VehicleId})");
                    return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                    {
                        Code = StatusCodes.Status403Forbidden,
                        Success = false,
                        Message = "You are not authorized to modify this vehicle."
                    });
                }

                vehicle.NumberPlate = request.NumberPlate.Trim();
                vehicle.VehicleBrand = request.VehicleBrand;
                vehicle.VehicleModel = request.VehicleModel;
                vehicle.VehicleColor = request.VehicleColor;
                vehicle.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await _accessLogService.LogAsync(User, "ModifyVehicle", true, $"VehicleId={vehicle.VehicleId}");
                return Ok(new ModifyVehicleResponse
                {
                    Code = StatusCodes.Status200OK,
                    Success = true,
                    Message = "Vehicle updated successfully.",
                    Data = MapToDTO(vehicle)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error modifying vehicle {VehicleId}", request.VehicleId);
                await _accessLogService.LogAsync(User, "ModifyVehicle", false, ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    Code = StatusCodes.Status500InternalServerError,
                    Success = false,
                    Message = "An error occurred while modifying the vehicle."
                });
            }
        }

        /// <summary>
        /// Delete a vehicle owned by the authenticated user (or any vehicle if admin).
        /// </summary>
        [Authorize]
        [HttpDelete("delete/{id}")]
        [ProducesResponseType(typeof(DeleteVehicleResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<DeleteVehicleResponse>> DeleteVehicle(int id)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

                if (user == null)
                {
                    await _accessLogService.LogAsync(User, "DeleteVehicle", false, "User not found");
                    return NotFound(new ErrorResponse
                    {
                        Code = StatusCodes.Status404NotFound,
                        Success = false,
                        Message = "User not found."
                    });
                }

                var vehicle = await _context.Vehicles
                    .Include(v => v.Bookings)
                    .FirstOrDefaultAsync(v => v.VehicleId == id);

                if (vehicle == null)
                {
                    await _accessLogService.LogAsync(User, "DeleteVehicle", false, $"Vehicle not found (id={id})");
                    return NotFound(new ErrorResponse
                    {
                        Code = StatusCodes.Status404NotFound,
                        Success = false,
                        Message = "Vehicle not found."
                    });
                }

                if (vehicle.UserId != userId && user.UserType != UserType.Admin)
                {
                    await _accessLogService.LogAsync(User, "DeleteVehicle", false, $"Not authorized (id={id})");
                    return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                    {
                        Code = StatusCodes.Status403Forbidden,
                        Success = false,
                        Message = "You are not authorized to delete this vehicle."
                    });
                }

                var hasActiveBookings = vehicle.Bookings.Any(b =>
                    b.BookingStatus == BookingStatus.Pending || b.BookingStatus == BookingStatus.Confirmed);

                if (hasActiveBookings)
                {
                    await _accessLogService.LogAsync(User, "DeleteVehicle", false, $"Vehicle has active bookings (id={id})");
                    return BadRequest(new ErrorResponse
                    {
                        Code = StatusCodes.Status400BadRequest,
                        Success = false,
                        Message = "Cannot delete a vehicle with active bookings."
                    });
                }

                _context.Vehicles.Remove(vehicle);
                await _context.SaveChangesAsync();

                await _accessLogService.LogAsync(User, "DeleteVehicle", true, $"VehicleId={id}");
                return Ok(new DeleteVehicleResponse
                {
                    Code = StatusCodes.Status200OK,
                    Success = true,
                    Message = "Vehicle deleted successfully."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting vehicle {VehicleId}", id);
                await _accessLogService.LogAsync(User, "DeleteVehicle", false, ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    Code = StatusCodes.Status500InternalServerError,
                    Success = false,
                    Message = "An error occurred while deleting the vehicle."
                });
            }
        }

        /// <summary>
        /// Get all vehicles owned by the authenticated user.
        /// </summary>
        [Authorize]
        [HttpGet("my-vehicle")]
        [ProducesResponseType(typeof(VehicleListResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<VehicleListResponse>> GetMyVehicles()
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

                if (user == null)
                {
                    await _accessLogService.LogAsync(User, "GetMyVehicles", false, "User not found");
                    return NotFound(new ErrorResponse
                    {
                        Code = StatusCodes.Status404NotFound,
                        Success = false,
                        Message = "User not found."
                    });
                }

                var vehicles = await _context.Vehicles
                    .AsNoTracking()
                    .Where(v => v.UserId == userId)
                    .Include(v => v.User)
                    .OrderByDescending(v => v.CreatedAt)
                    .ToListAsync();

                var result = vehicles.Select(MapToDTO).ToList();

                await _accessLogService.LogAsync(User, "GetMyVehicles", true, $"{result.Count} vehicle(s)");
                return Ok(new VehicleListResponse
                {
                    Code = StatusCodes.Status200OK,
                    Success = true,
                    Message = result.Count > 0
                        ? $"Retrieved {result.Count} vehicle(s) successfully"
                        : "No vehicles found",
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user's vehicles");
                await _accessLogService.LogAsync(User, "GetMyVehicles", false, ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    Code = StatusCodes.Status500InternalServerError,
                    Success = false,
                    Message = "An error occurred while retrieving your vehicles."
                });
            }
        }

        /// <summary>
        /// Get all vehicles in the system (Admin only).
        /// </summary>
        [Authorize]
        [HttpGet("all")]
        [ProducesResponseType(typeof(VehicleListResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<VehicleListResponse>> GetAllVehicles()
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

                if (user == null)
                {
                    await _accessLogService.LogAsync(User, "GetAllVehicles", false, "User not found");
                    return NotFound(new ErrorResponse
                    {
                        Code = StatusCodes.Status404NotFound,
                        Success = false,
                        Message = "User not found."
                    });
                }

                if (user.UserType != UserType.Admin)
                {
                    await _accessLogService.LogAsync(User, "GetAllVehicles", false, "Not an admin");
                    return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                    {
                        Code = StatusCodes.Status403Forbidden,
                        Success = false,
                        Message = "Only administrators can view all vehicles."
                    });
                }

                var vehicles = await _context.Vehicles
                    .AsNoTracking()
                    .Include(v => v.User)
                    .OrderByDescending(v => v.CreatedAt)
                    .ToListAsync();

                var result = vehicles.Select(MapToDTO).ToList();

                await _accessLogService.LogAsync(User, "GetAllVehicles", true, $"{result.Count} vehicle(s)");
                return Ok(new VehicleListResponse
                {
                    Code = StatusCodes.Status200OK,
                    Success = true,
                    Message = result.Count > 0
                        ? $"Retrieved {result.Count} vehicle(s) successfully"
                        : "No vehicles found",
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all vehicles");
                await _accessLogService.LogAsync(User, "GetAllVehicles", false, ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    Code = StatusCodes.Status500InternalServerError,
                    Success = false,
                    Message = "An error occurred while retrieving all vehicles."
                });
            }
        }

        private static VehicleResponseDTO MapToDTO(Vehicle vehicle)
        {
            return new VehicleResponseDTO
            {
                VehicleId = vehicle.VehicleId,
                NumberPlate = vehicle.NumberPlate,
                VehicleBrand = vehicle.VehicleBrand,
                VehicleModel = vehicle.VehicleModel,
                VehicleColor = vehicle.VehicleColor,
                CreatedAt = vehicle.CreatedAt,
                UpdatedAt = vehicle.UpdatedAt,
                OwnerEmail = vehicle.User?.Email,
                OwnerName = $"{vehicle.User?.FirstName} {vehicle.User?.LastName}".Trim()
            };
        }
    }
}
