using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using ParkJomV2.Services.Copilot;
using ParkJomV2.Services.Vision;

namespace ParkJomV2.Controllers
{
    [Route("api/vehicle")]
    [ApiController]
    public class VehicleController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;
        private readonly AccessLogService _accessLogService;
        private readonly VehicleVisionService _visionService;
        private readonly ILogger<VehicleController> _logger;

        public VehicleController(
            ApplicationDbContext context,
            CurrentUserService currentUser,
            AccessLogService accessLogService,
            VehicleVisionService visionService,
            ILogger<VehicleController> logger)
        {
            _context = context;
            _currentUser = currentUser;
            _accessLogService = accessLogService;
            _visionService = visionService;
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
                var user = await _currentUser.GetCurrentUserAsync();

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
                    v.UserId == user.UserId &&
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
                    UserId = user.UserId,
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
                var user = await _currentUser.GetCurrentUserAsync();

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

                if (vehicle.UserId != user.UserId)
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
                var user = await _currentUser.GetCurrentUserAsync();

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

                if (vehicle.UserId != user.UserId && user.UserType != UserType.Admin)
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
                    b.BookingStatus == BookingStatus.Pending ||
                    b.BookingStatus == BookingStatus.Confirmed ||
                    b.BookingStatus == BookingStatus.Active);

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

                if (vehicle.Bookings.Count > 0)
                {
                    await _accessLogService.LogAsync(User, "DeleteVehicle", false, $"Vehicle referenced by booking history (id={id})");
                    return BadRequest(new ErrorResponse
                    {
                        Code = StatusCodes.Status400BadRequest,
                        Success = false,
                        Message = "Cannot delete a vehicle that is referenced in your booking history."
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
                var user = await _currentUser.GetCurrentUserAsync();

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
                    .Where(v => v.UserId == user.UserId)
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
                var user = await _currentUser.GetCurrentUserAsync();

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

        /// <summary>
        /// AI-3 Vision ALPR: Extracts license plate, brand, model, color, and JPJ region from a vehicle/road-tax photo.
        /// </summary>
        [Authorize]
        [HttpPost("vision-scan")]
        [ProducesResponseType(typeof(VehicleVisionScanResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<VehicleVisionScanResponse>> VisionScanVehicle(
            [FromBody] VehicleVisionScanRequest request,
            CancellationToken ct)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ImageBase64))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Please provide a vehicle photo to scan."
                });
            }

            try
            {
                var result = await _visionService.ScanVehicleAsync(request, ct);
                await _accessLogService.LogAsync(
                    User,
                    "ALPR_VisionScan",
                    true,
                    $"Plate={result.NumberPlate}, Make={result.Brand} {result.Model}, Color={result.Color}, Conf={result.Confidence:P0}");

                return Ok(new VehicleVisionScanResponse
                {
                    Code = StatusCodes.Status200OK,
                    Success = true,
                    Message = $"Detected {result.FormattedPlate} ({result.Brand} {result.Model} · {result.Color})",
                    Data = result
                });
            }
            catch (GeminiException ex)
            {
                await _accessLogService.LogAsync(User, "ALPR_VisionScan", false, ex.Message);
                return StatusCode(ex.StatusCode, new ErrorResponse
                {
                    Code = ex.StatusCode,
                    Success = false,
                    Message = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running ALPR vision scan");
                await _accessLogService.LogAsync(User, "ALPR_VisionScan", false, ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    Code = StatusCodes.Status500InternalServerError,
                    Success = false,
                    Message = "Unable to analyze the vehicle image right now."
                });
            }
        }

        /// <summary>
        /// AI-3 Smart Bollard Check-In: Cross-verifies the physical car's license plate against the booked vehicle.
        /// </summary>
        [Authorize]
        [HttpPost("verify-plate")]
        [ProducesResponseType(typeof(BarrierPlateVerifyResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<BarrierPlateVerifyResponse>> VerifyBarrierPlate(
            [FromBody] BarrierPlateVerifyRequest request,
            CancellationToken ct)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ImageBase64))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Please capture or upload a vehicle photo for barrier verification."
                });
            }

            try
            {
                var user = await _currentUser.GetCurrentUserAsync();
                string? expectedPlate = request.ExpectedPlate?.Trim();
                int? iotDeviceId = null;
                Booking? booking = null;

                if (request.BookingId.HasValue && request.BookingId.Value > 0)
                {
                    booking = await _context.Bookings
                        .Include(b => b.Vehicle)
                        .Include(b => b.ParkingSpot)
                        .ThenInclude(p => p.IoTDevice)
                        .FirstOrDefaultAsync(b => b.BookingId == request.BookingId.Value, ct);

                    if (booking != null)
                    {
                        if (string.IsNullOrWhiteSpace(expectedPlate))
                            expectedPlate = booking.Vehicle?.NumberPlate;
                        iotDeviceId = booking.ParkingSpot?.IoTDevice?.IoTDeviceId;
                    }
                }

                if (string.IsNullOrWhiteSpace(expectedPlate) && user != null)
                {
                    var latestVehicle = await _context.Vehicles
                        .AsNoTracking()
                        .Where(v => v.UserId == user.UserId)
                        .OrderByDescending(v => v.UpdatedAt)
                        .FirstOrDefaultAsync(ct);
                    expectedPlate = latestVehicle?.NumberPlate;
                }

                if (string.IsNullOrWhiteSpace(expectedPlate))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Code = StatusCodes.Status400BadRequest,
                        Success = false,
                        Message = "No registered vehicle plate found to verify against."
                    });
                }

                var result = await _visionService.VerifyBarrierPlateAsync(request, expectedPlate, ct);

                if (result.Verified && booking != null && !booking.CheckedInAt.HasValue)
                {
                    booking.CheckedInAt = DateTime.UtcNow;
                    booking.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync(ct);
                }

                await _accessLogService.LogAsync(
                    User,
                    "ALPR_BarrierVerify",
                    result.Verified,
                    $"Expected={result.ExpectedPlate}, Detected={result.DetectedPlate}, Similarity={result.SimilarityScore:P0}, Status={result.Status}",
                    request.BookingId,
                    iotDeviceId);

                var message = result.Status switch
                {
                    "exact_match" => $"Exact ALPR match confirmed: {result.FormattedDetectedPlate} ({result.Brand} {result.Model}). Barrier ready to release.",
                    "near_match" => $"Verified with OCR tolerance ({result.SimilarityScore:P0} match): {result.FormattedDetectedPlate} matches {result.ExpectedPlate}.",
                    _ => $"Vehicle mismatch: Camera detected {result.FormattedDetectedPlate} ({result.Brand} {result.Model}), but reservation expects {result.ExpectedPlate}."
                };

                return Ok(new BarrierPlateVerifyResponse
                {
                    Code = StatusCodes.Status200OK,
                    Success = true,
                    Message = message,
                    Data = result
                });
            }
            catch (GeminiException ex)
            {
                await _accessLogService.LogAsync(User, "ALPR_BarrierVerify", false, ex.Message, request.BookingId);
                return StatusCode(ex.StatusCode, new ErrorResponse
                {
                    Code = ex.StatusCode,
                    Success = false,
                    Message = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying vehicle plate at barrier");
                await _accessLogService.LogAsync(User, "ALPR_BarrierVerify", false, ex.Message, request.BookingId);
                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    Code = StatusCodes.Status500InternalServerError,
                    Success = false,
                    Message = "Unable to verify vehicle plate right now."
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
