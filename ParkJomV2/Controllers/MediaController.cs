using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using System.Security.Claims;

namespace ParkJomV2.Controllers
{
    [ApiController]
    [Route("api/media")]
    public class MediaController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly CloudinaryService _cloudinaryService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<MediaController> _logger;

        public MediaController(
            ApplicationDbContext context,
            CloudinaryService cloudinaryService,
            IHttpClientFactory httpClientFactory,
            ILogger<MediaController> logger)
        {
            _context = context;
            _cloudinaryService = cloudinaryService;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <summary>
        /// View a private media file (Admin only)
        /// </summary>
        [Authorize]
        [HttpGet("view/document/{mediaFileId}")]
        public async Task<IActionResult> ViewMedia(int mediaFileId)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserId == userId);

                if (user == null)
                {
                    return Unauthorized(new ErrorResponse
                    {
                        Code = StatusCodes.Status401Unauthorized,
                        Success = false,
                        Message = "Unauthorized."
                    });
                }

                if (user.UserType != UserType.Admin)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                    {
                        Code = StatusCodes.Status403Forbidden,
                        Success = false,
                        Message = "Forbidden."
                    });
                }

                var media = await _context.MediaFiles.FirstOrDefaultAsync(m => m.MediaFileId == mediaFileId);

                //return Ok(media);

                if (media == null)
                {
                    return NotFound(new ErrorResponse
                    {
                        Code = StatusCodes.Status404NotFound,
                        Success = false,
                        Message = "Media file not found."
                    });
                }

                var cloudinaryUrl = _cloudinaryService.GeneratePrivateUrl(
                    media.PublicId,
                    media.ResourceType);

                //return Ok(cloudinaryUrl);

                var client = _httpClientFactory.CreateClient();

                var response = await client.GetAsync(
                    cloudinaryUrl,
                    HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Cloudinary returned {StatusCode} for MediaFileId={MediaFileId}",
                        response.StatusCode,
                        mediaFileId);

                    return StatusCode((int)response.StatusCode, new ErrorResponse
                    {
                        Code = (int)response.StatusCode,
                        Success = false,
                        Message = "Failed to retrieve media from storage."
                    });
                }

                var stream = await response.Content.ReadAsStreamAsync();

                var resourceType = media.ResourceType == "raw" ? "application" : media.ResourceType;

                // prevent MIME sniffing
                // prevent web caching of sensitive media
                Response.Headers.Append(
                    "Content-Disposition",
                    $"inline; filename=\"{media.OriginalFileName}\"");

                Response.Headers.Append(
                    "Cache-Control",
                    "no-store");

                Response.Headers.Append(
                    "X-Content-Type-Options",
                    "nosniff");

                return File(
                    stream,
                    resourceType + "/" + media.Format,
                    enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to retrieve media {MediaFileId}",
                    mediaFileId);

                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    Code = StatusCodes.Status500InternalServerError,
                    Success = false,
                    Message = "Unable to retrieve media."
                });
            }
        }

        [HttpGet("view/image/{mediaFileId}")]
        public async Task<IActionResult> ViewImage(int mediaFileId)
        {
            var media = await _context.MediaFiles.FirstOrDefaultAsync(m => m.MediaFileId == mediaFileId);
            if (media == null)
            {
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Media file not found."
                });
            }
            var cloudinaryUrl = _cloudinaryService.GeneratePrivateUrl(media.PublicId, media.ResourceType);
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync(
                cloudinaryUrl,
                HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Cloudinary returned {StatusCode} for MediaFileId={MediaFileId}",
                    response.StatusCode,
                    mediaFileId);
                return StatusCode((int)response.StatusCode, new ErrorResponse
                {
                    Code = (int)response.StatusCode,
                    Success = false,
                    Message = "Failed to retrieve media from storage."
                });
            }
            var stream = await response.Content.ReadAsStreamAsync();
            var resourceType = media.ResourceType == "raw" ? "application" : media.ResourceType;
            // prevent MIME sniffing
            // prevent web caching of sensitive media
            Response.Headers.Append(
                "Content-Disposition",
                $"inline; filename=\"{media.OriginalFileName}\"");
            Response.Headers.Append(
                "Cache-Control",
                "no-store");
            Response.Headers.Append(
                "X-Content-Type-Options",
                "nosniff");
            return File(
                stream,
                resourceType + "/" + media.Format,
                enableRangeProcessing: true);
        }
    }
}