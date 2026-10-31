using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ParkJomV2.Data;
using ParkJomV2.Hubs;
using ParkJomV2.Middleware;
using ParkJomV2.Models;
using ParkJomV2.Services;
using ParkJomV2.Services.Support;
using ParkJomV2.Services.Support.Workers;
using ParkJomV2.Controllers;
using ParkJomV2.Services.Copilot;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<GoogleAuthService>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<CloudinaryService>();
builder.Services.AddScoped<StripeService>();

builder.Services.AddHttpClient<OsrmService>(client =>
{
    client.BaseAddress = new Uri("https://router.project-osrm.org/");
    client.DefaultRequestHeaders.Add("User-Agent", "ParkJomV2/1.0");
});

builder.Services.AddHttpClient<NominatimService>(client =>
{
    client.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
    client.DefaultRequestHeaders.Add("User-Agent", "ParkJomV2/1.0");
});

builder.Services.AddScoped<IPropertyService, PropertyService>();
builder.Services.AddSingleton<GtfsService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<GtfsRealtimeService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Add("User-Agent", "ParkJomV2/1.0 (Smart Transit Parking Platform)");
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwt = builder.Configuration.GetSection("JwtSettings");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt["SecretKey"]!))
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"].FirstOrDefault()
                                  ?? context.Request.Query["token"].FirstOrDefault();
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && (path.StartsWithSegments("/hubs/support") || path.StartsWithSegments("/api/support/ws")))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var userIdText = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!int.TryParse(userIdText, out var userId))
                {
                    context.Fail("The authenticated account could not be identified.");
                    return;
                }

                var dbContext = context.HttpContext.RequestServices
                    .GetRequiredService<ApplicationDbContext>();
                var accountStatus = await dbContext.Users
                    .AsNoTracking()
                    .Where(user => user.UserId == userId)
                    .Select(user => user.AccountStatus)
                    .FirstOrDefaultAsync();

                if (accountStatus == null)
                {
                    context.Fail("The authenticated account no longer exists.");
                    return;
                }

                if (string.Equals(
                    accountStatus,
                    "Suspended",
                    StringComparison.OrdinalIgnoreCase))
                {
                    context.HttpContext.Items["SuspendedAccount"] = true;
                    context.Fail("The account is suspended.");
                }
                else if (string.Equals(
                    accountStatus,
                    "Deactivated",
                    StringComparison.OrdinalIgnoreCase))
                {
                    // Allow deactivated users to reach support endpoints to request reactivation.
                    var requestPath = context.HttpContext.Request.Path;
                    var isSupportEndpoint = requestPath.StartsWithSegments("/api/support")
                        || requestPath.StartsWithSegments("/hubs/support");
                    if (!isSupportEndpoint)
                    {
                        context.HttpContext.Items["DeactivatedAccount"] = true;
                        context.Fail("The account is deactivated.");
                    }
                }
            },
            OnChallenge = context =>
            {
                context.HandleResponse();

                var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
                var isSuspendedAccount = context.HttpContext.Items.ContainsKey("SuspendedAccount");
                var isDeactivatedAccount = context.HttpContext.Items.ContainsKey("DeactivatedAccount");

                var (statusCode, message) = isSuspendedAccount
                    ? (StatusCodes.Status403Forbidden, "Your account is suspended. Please contact support.")
                    : isDeactivatedAccount
                        ? (StatusCodes.Status403Forbidden, "Your account is deactivated. Please contact support to reactivate.")
                        : context.AuthenticateFailure switch
                {
                    SecurityTokenExpiredException => (StatusCodes.Status401Unauthorized, "Your session has expired. Please log in again."),
                    SecurityTokenException => (StatusCodes.Status401Unauthorized, "Invalid token. Please provide a valid authentication token."),
                    _ when string.IsNullOrEmpty(authHeader) => (StatusCodes.Status401Unauthorized, "Authentication required. Please provide a valid authentication token."),
                    _ when !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) => (StatusCodes.Status401Unauthorized, "Invalid authorization header format."),
                    _ => (StatusCodes.Status401Unauthorized, "Authentication failed. The provided token could not be validated.")
                };

                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";

                var response = JsonSerializer.Serialize(new
                {
                    Code = statusCode,
                    Success = false,
                    Message = message
                });

                return context.Response.WriteAsync(response);
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("UserType", "Admin");
    });
});

// System-wide audit log service (write an AccessLog row per action from controllers).
builder.Services.AddScoped<AccessLogService>();

// Creates Transaction ledger rows for wallet movements.
builder.Services.AddScoped<TransactionService>();

// Applies wallet balance movements and platform wallet increments.
builder.Services.AddScoped<WalletService>();

// Resolves the authenticated user from the current HTTP context.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUserService>();

// Support Module Services
builder.Services.AddSignalR();
builder.Services.AddSingleton<SupportWebSocketConnectionManager>();
builder.Services.AddScoped<ISupportRealtimeNotifier, SupportRealtimeNotifier>();
builder.Services.AddScoped<SupportAuditService>();
builder.Services.AddScoped<SupportContextService>();
builder.Services.AddScoped<SupportTicketService>();
builder.Services.AddScoped<IncidentService>();
builder.Services.AddScoped<DisputeService>();
builder.Services.AddScoped<SupportOnCallService>();

// AI Transit Copilot (Gemini function calling). The API key comes from configuration
// ("Gemini:ApiKey") — use dotnet user-secrets locally and App Settings in Azure.
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));
builder.Services.AddHttpClient<GeminiClient>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(45);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    ConnectTimeout = TimeSpan.FromSeconds(8),
    ConnectCallback = async (context, cancellationToken) =>
    {
        var addresses = await System.Net.Dns.GetHostAddressesAsync(
            context.DnsEndPoint.Host,
            System.Net.Sockets.AddressFamily.InterNetwork,
            cancellationToken);
        var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp)
        {
            NoDelay = true
        };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
            return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
});
builder.Services.AddScoped<CopilotToolExecutor>();
builder.Services.AddScoped<CopilotService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(CopilotController.RateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

// Background Hosted Services
builder.Services.AddHostedService<IncidentEscalationWorker>();
builder.Services.AddHostedService<SupportSlaWorker>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(x => x.Value?.Errors.Count > 0)
            .ToDictionary(
                x => x.Key,
                x => x.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
            );

        return new BadRequestObjectResult(new
        {
            code = StatusCodes.Status400BadRequest,
            message = "One or more validation errors occurred.",
            errors = errors
        });
    };
});

builder.Services.AddHttpClient();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy =>
        {
            policy.WithOrigins(
                "http://127.0.0.1:5500",
                "http://localhost:3000",
                "https://united-perigee-400000.web.app"
            )
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseWebSockets();
app.UseMiddleware<SupportWebSocketMiddleware>();

app.UseAuthentication();   
app.UseAuthorization();

app.UseRateLimiter();

app.MapControllers();
app.MapHub<SupportHub>("/hubs/support");

app.Run();
