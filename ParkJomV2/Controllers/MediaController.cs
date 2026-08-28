using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;

namespace ParkJomV2.Controllers
{
    [ApiController]
    [Route("api/media")]
    public class MediaController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;
        private readonly CloudinaryService _cloudinaryService;
        private readonly AccessLogService _accessLogService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<MediaController> _logger;

        public MediaController(
            ApplicationDbContext context,
            CurrentUserService currentUser,
            CloudinaryService cloudinaryService,
            AccessLogService accessLogService,
            IHttpClientFactory httpClientFactory,
            ILogger<MediaController> logger)
        {
            _context = context;
            _currentUser = currentUser;
            _cloudinaryService = cloudinaryService;
            _accessLogService = accessLogService;
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
                var user = await _currentUser.GetCurrentUserAsync();

                if (user == null)
                {
                    await _accessLogService.LogAsync(User, "ViewMedia", false, $"Unauthorized (mediaFileId={mediaFileId})");
                    return Unauthorized(new ErrorResponse
                    {
                        Code = StatusCodes.Status401Unauthorized,
                        Success = false,
                        Message = "Unauthorized."
                    });
                }

                if (user.UserType != UserType.Admin)
                {
                    await _accessLogService.LogAsync(User, "ViewMedia", false, $"Forbidden (mediaFileId={mediaFileId})");
                    return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                    {
                        Code = StatusCodes.Status403Forbidden,
                        Success = false,
                        Message = "Forbidden."
                    });
                }

                var media = await _context.MediaFiles.FirstOrDefaultAsync(m => m.MediaFileId == mediaFileId);

                // return Ok(media);

                if (media == null)
                {
                    await _accessLogService.LogAsync(User, "ViewMedia", false, $"Media not found (mediaFileId={mediaFileId})");
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

                    if(media.ResourceType == "image"){
                        cloudinaryUrl = cloudinaryUrl + "." + media.Format;
                    }

                // return Ok(cloudinaryUrl);

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

                    await _accessLogService.LogAsync(User, "ViewMedia", false, $"Cloudinary status {(int)response.StatusCode}");
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

                await _accessLogService.LogAsync(User, "ViewMedia", true, $"MediaFileId={mediaFileId}");

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

                await _accessLogService.LogAsync(User, "ViewMedia", false, ex.Message);
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
                await _accessLogService.LogAsync(User, "ViewImage", false, $"Media not found (mediaFileId={mediaFileId})");
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
                await _accessLogService.LogAsync(User, "ViewImage", false, $"Cloudinary status {(int)response.StatusCode}");
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
            await _accessLogService.LogAsync(User, "ViewImage", true, $"MediaFileId={mediaFileId}");
            return File(
                stream,
                resourceType + "/" + media.Format,
                enableRangeProcessing: true);
        }
    }
}