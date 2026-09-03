using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ParkJomV2.Middleware;

public class SupportWebSocketMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SupportWebSocketConnectionManager _manager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SupportWebSocketMiddleware> _logger;

    public SupportWebSocketMiddleware(
        RequestDelegate next,
        SupportWebSocketConnectionManager manager,
        IConfiguration configuration,
        ILogger<SupportWebSocketMiddleware> logger)
    {
        _next = next;
        _manager = manager;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path == "/api/support/ws")
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                // Authenticate token from query string ?access_token=... or ?token=...
                var token = context.Request.Query["access_token"].FirstOrDefault()
                            ?? context.Request.Query["token"].FirstOrDefault();

                int? userId = null;
                string? role = null;

                if (!string.IsNullOrEmpty(token))
                {
                    try
                    {
                        var jwt = _configuration.GetSection("JwtSettings");
                        var tokenHandler = new JwtSecurityTokenHandler();
                        var validationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidateAudience = true,
                            ValidateLifetime = true,
                            ValidateIssuerSigningKey = true,
                            ValidIssuer = jwt["Issuer"],
                            ValidAudience = jwt["Audience"],
                            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["SecretKey"]!))
                        };

                        var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
                        var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                        if (int.TryParse(userIdStr, out var parsedId))
                        {
                            userId = parsedId;
                        }
                        role = principal.FindFirst("UserType")?.Value;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("WebSocket token validation failed: {Message}", ex.Message);
                    }
                }

                using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                var socketId = _manager.AddSocket(webSocket, userId, role);
                _logger.LogInformation("Native WebSocket connected: {SocketId}, User: {UserId}, Role: {Role}", socketId, userId, role);

                var buffer = new byte[1024 * 4];
                try
                {
                    while (webSocket.State == WebSocketState.Open)
                    {
                        var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("WebSocket exception: {Message}", ex.Message);
                }
                finally
                {
                    await _manager.RemoveSocketAsync(socketId);
                    _logger.LogInformation("Native WebSocket disconnected: {SocketId}", socketId);
                }
                return;
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Expected WebSocket request");
                return;
            }
        }

        await _next(context);
    }
}
