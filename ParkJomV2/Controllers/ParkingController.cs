using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using System.Security.Claims;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/parking")]
public class ParkingController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly CloudinaryService _cloudinaryService;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<ParkingController> _logger;
    private readonly IPropertyService _propertyService;

    public ParkingController(ApplicationDbContext context, CloudinaryService cloudinaryService,
        AccessLogService accessLogService,
        ILogger<ParkingController> logger, IPropertyService propertyService)
    {
        _context = context;
        _cloudinaryService = cloudinaryService;
        _accessLogService = accessLogService;
        _logger = logger;
        _propertyService = propertyService;
    }

    /// <summary>
    /// Get a parking spot by ID
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ParkingSpotDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ParkingSpotDetailResponse>> GetParkingSpot(int id)
    {
        try
        {
            var spot = await _context.ParkingSpots
                .Include(ps => ps.Property)
                .Include(ps => ps.Owner)
                .Include(ps => ps.VerificationRequests.Where(vr => vr.IsCurrent))
                .Include(ps => ps.ParkingSpotImages.OrderBy(psi => psi.DisplayOrder))
                    .ThenInclude(psi => psi.MediaFile)
                .FirstOrDefaultAsync(ps => ps.ParkingSpotId == id);

            if (spot == null)
            {
                await _accessLogService.LogAsync(User, "GetParkingSpot", false, $"Parking spot not found (id={id})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Parking spot not found"
                });
            }

            var dto = new ParkingSpotDetailDTO
            {
                ParkingSpotId = spot.ParkingSpotId,
                PropertyId = spot.PropertyId,
                PropertyName = spot.Property?.PropertyName,
                Address = spot.Property?.Address,
                OwnerId = spot.OwnerId,
                OwnerName = $"{spot.Owner?.FirstName} {spot.Owner?.LastName}".Trim(),
                ParkingLabel = spot.ParkingLabel,
                AvailabilityStatus = spot.AvailabilityStatus,
                VerificationStatus = spot.VerificationRequests.FirstOrDefault()?.VerificationStatus ?? VerificationStatus.Pending,
                MonthlyRate = spot.MonthlyRate,
                DailyRate = spot.DailyRate,
                IsPublished = spot.IsPublished,
                CreatedAt = spot.CreatedAt,
                UpdatedAt = spot.UpdatedAt,
                Images = spot.ParkingSpotImages.Select(psi => new ParkingSpotImageDTO
                {
                    ParkingSpotId = psi.ParkingSpotId,
                    MediaFileId = psi.MediaFileId,
                    ResourceType = psi.MediaFile?.ResourceType,
                    Format = psi.MediaFile?.Format,
                    OriginalFileName = psi.MediaFile?.OriginalFileName,
                    UploadedAt = psi.CreatedAt
                }).ToList()
            };

            await _accessLogService.LogAsync(User, "GetParkingSpot", true, $"ParkingSpotId={id}");

            return Ok(new ParkingSpotDetailResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Parking spot retrieved successfully",
                Data = dto
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving parking spot {ParkingSpotId}", id);
            await _accessLogService.LogAsync(User, "GetParkingSpot", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving the parking spot"
            });
        }
    }
}   
