using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;

namespace ParkJomV2.Controllers;

[ApiController]
[Authorize]
[Route("api/iot/bookings")]
public sealed class IoTCommandController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly CurrentUserService _currentUser;
    private readonly IMqttService _mqtt;
    private readonly AccessLogService _accessLogs;

    public IoTCommandController(
        ApplicationDbContext db,
        CurrentUserService currentUser,
        IMqttService mqtt,
        AccessLogService accessLogs)
    {
        _db = db;
        _currentUser = currentUser;
        _mqtt = mqtt;
        _accessLogs = accessLogs;
    }

    [HttpPost("{bookingId:int}/command")]
    public async Task<IActionResult> SendCommand(
        int bookingId,
        [FromBody] BollardCommandRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        var command = request.Command.Trim().ToUpperInvariant();
        if (command is not ("LOCK" or "UNLOCK"))
        {
            return BadRequest(new { message = "Command must be LOCK or UNLOCK." });
        }

        var booking = await _db.Bookings
            .Include(b => b.ParkingSpot)
                .ThenInclude(p => p.IoTDevice)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.RenterId == userId.Value, cancellationToken);

        if (booking is null)
        {
            return NotFound(new { message = "Booking was not found." });
        }

        if (booking.BookingStatus is not (BookingStatus.Confirmed or BookingStatus.Active))
        {
            return Conflict(new { message = "The booking is not eligible for bollard control." });
        }

        // Resolve the device through this booking's parking spot; never accept a device topic from the caller.
        var device = booking.ParkingSpot.IoTDevice;
        if (device is null || !device.IsAssigned || string.IsNullOrWhiteSpace(device.Esp32Serial))
        {
            return Conflict(new { message = "No assigned IoT device is available for this booking." });
        }

        var now = DateTime.UtcNow;
        if (command == "UNLOCK" && (now < booking.StartDate || now > booking.EndDate))
        {
            return Conflict(new { message = "The booking is outside its bollard access window." });
        }

        try
        {
            await _mqtt.PublishCommandAsync(device.Esp32Serial, command, cancellationToken);
            await _accessLogs.LogAsync(userId.Value, $"Bollard {command} command", true,
                $"Published for device {device.Esp32Serial}", booking.BookingId, device.IoTDeviceId);
        }
        catch (InvalidOperationException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "The device control service is temporarily unavailable." });
        }

        return Accepted(new
        {
            bookingId = booking.BookingId,
            deviceId = device.IoTDeviceId,
            command,
            status = "published"
        });
    }
}

public sealed class BollardCommandRequest
{
    [Required]
    public string Command { get; set; } = string.Empty;
}
