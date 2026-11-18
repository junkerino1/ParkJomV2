using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ParkJomV2.Data;
using ParkJomV2.Hubs;
using ParkJomV2.Middleware;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using ParkJomV2.Services.Support;
using ParkJomV2.Services.Support.Workers;
using ParkJomV2.Controllers;
using ParkJomV2.Services.Copilot;
using ParkJomV2.Services.Notifications;
using ParkJomV2.Services.Notifications.Channels;
using ParkJomV2.Services.Notifications.Workers;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<GoogleAuthService>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<ITokenBlacklistService, TokenBlacklistService>();
builder.Services.AddHttpClient<Sms360Service>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<CloudinaryService>();
builder.Services.AddScoped<StripeService>();
builder.Services.AddScoped<PublicParkingService>();
// Registration, OTP lifecycle, and spot binding share one scoped business service.
builder.Services.AddScoped<IoTDeviceService>();
// Unlock verification challenges intentionally stay in process memory: no database schema change.
builder.Services.AddSingleton<UnlockChallengeService>();

// One backend-owned MQTT connection handles device commands and inbound messages.
builder.Services.AddSingleton<MqttService>();
builder.Services.AddSingleton<IMqttService>(services => services.GetRequiredService<MqttService>());
builder.Services.AddHostedService(services => services.GetRequiredService<MqttService>());

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
                if (!string.IsNullOrEmpty(accessToken) && (path.StartsWithSegments("/hubs/support") || path.StartsWithSegments("/hubs/notifications") || path.StartsWithSegments("/api/support/ws")))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var rawToken = context.Request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase)?.Trim()
                               ?? context.Request.Query["access_token"].FirstOrDefault()
                               ?? context.Request.Query["token"].FirstOrDefault();

                if (!string.IsNullOrEmpty(rawToken))
                {
                    var blacklistService = context.HttpContext.RequestServices.GetRequiredService<ITokenBlacklistService>();
                    if (await blacklistService.IsTokenRevokedAsync(rawToken))
                    {
                        context.Fail("This authentication token has been revoked / logged out.");
                        return;
                    }
                }

                var userIdText = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!int.TryParse(userIdText, out var userId))
                {
                    context.Fail("The authenticated account could not be identified.");
                    return;
                }

                var dbContext = context.HttpContext.RequestServices
                    .GetRequiredService<ApplicationDbContext>();
                var userTypeClaim = context.Principal?.FindFirst("UserType")?.Value;
                if (string.Equals(userTypeClaim, UserType.PropertyManagement.ToString(), StringComparison.Ordinal))
                {
                    if (string.Equals(context.Principal?.FindFirst("purpose")?.Value,
                            "management-invitation", StringComparison.Ordinal))
                    {
                        context.Fail("An invitation token cannot be used as an API access token.");
                        return;
                    }

                    var tokenCredentialVersion = context.Principal?.FindFirst("CredentialVersion")?.Value;
                    if (!int.TryParse(tokenCredentialVersion, out var parsedCredentialVersion))
                    {
                        context.Fail("The property management credential is invalid.");
                        return;
                    }

                    var managementAccount = await dbContext.PropertyManagements
                        .AsNoTracking()
                        .Where(account => account.ManagementAccountId == userId)
                        .Select(account => new { account.CredentialVersion, account.AccountStatus })
                        .FirstOrDefaultAsync();

                    if (managementAccount is null || managementAccount.CredentialVersion != parsedCredentialVersion)
                    {
                        context.Fail("The property management credentials have changed. Please log in again.");
                        return;
                    }

                    if (string.Equals(managementAccount.AccountStatus, "Suspended", StringComparison.OrdinalIgnoreCase))
                    {
                        context.HttpContext.Items["SuspendedAccount"] = true;
                        context.Fail("The account is suspended.");
                    }
                    else if (string.Equals(managementAccount.AccountStatus, "Deactivated", StringComparison.OrdinalIgnoreCase))
                    {
                        context.HttpContext.Items["DeactivatedAccount"] = true;
                        context.Fail("The account is deactivated.");
                    }
                    return;
                }

                var accountState = await dbContext.Users
                    .AsNoTracking()
                    .Where(user => user.UserId == userId)
                    .Select(user => new { user.AccountStatus, user.IsProfileComplete })
                    .FirstOrDefaultAsync();

                if (accountState == null)
                {
                    context.Fail("The authenticated account no longer exists.");
                    return;
                }

                if (string.Equals(
                    accountState.AccountStatus,
                    "Suspended",
                    StringComparison.OrdinalIgnoreCase))
                {
                    context.HttpContext.Items["SuspendedAccount"] = true;
                    context.Fail("The account is suspended.");
                }
                else if (string.Equals(
                    accountState.AccountStatus,
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

                if (accountState.IsProfileComplete == false
                    && !string.Equals(userTypeClaim, UserType.Admin.ToString(), StringComparison.Ordinal)
                    && !IsPhoneOnboardingEndpoint(context.HttpContext.Request.Path))
                {
                    context.HttpContext.Items["IncompleteProfile"] = true;
                    context.Fail("Phone verification is required before using ParkJom.");
                }
            },
            OnChallenge = context =>
            {
                context.HandleResponse();

                var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
                var isSuspendedAccount = context.HttpContext.Items.ContainsKey("SuspendedAccount");
                var isDeactivatedAccount = context.HttpContext.Items.ContainsKey("DeactivatedAccount");
                var isIncompleteProfile = context.HttpContext.Items.ContainsKey("IncompleteProfile");

                var (statusCode, message) = isSuspendedAccount
                    ? (StatusCodes.Status403Forbidden, "Your account is suspended. Please contact support.")
                    : isDeactivatedAccount
                        ? (StatusCodes.Status403Forbidden, "Your account is deactivated. Please contact support to reactivate.")
                        : isIncompleteProfile
                            ? (StatusCodes.Status403Forbidden, "Verify your phone number before using ParkJom.")
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
    options.AddPolicy("PropertyManagementOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("UserType", UserType.PropertyManagement.ToString());
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
builder.Services.AddScoped<ParkJomV2.Services.Vision.VehicleVisionService>();
builder.Services.AddScoped<ParkJomV2.Services.Pricing.DynamicPricingService>();
builder.Services.AddScoped<ParkJomV2.Services.Prediction.WeatherForecastService>();
builder.Services.AddScoped<ParkJomV2.Services.Prediction.ParkingPredictorService>();
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
builder.Services.AddScoped<IncidentEscalationJob>();
builder.Services.AddScoped<SupportSlaJob>();

// Notification Module Services
builder.Services.AddSingleton<INotificationQueue, NotificationQueue>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IWebPushSender, WebPushSender>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddSingleton<NotificationDispatchWorker>();
builder.Services.AddHostedService(services => services.GetRequiredService<NotificationDispatchWorker>());
builder.Services.AddScoped<BookingReminderJob>();
builder.Services.AddScoped<OverstayMonitoringJob>();
builder.Services.AddScoped<NotificationCleanupJob>();
builder.Services.AddScoped<CronMaintenanceJob>();

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

// Swagger Documentation & Automatic Module Grouping
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ParkJom V2 API Documentation",
        Version = "v1",
        Description = "RESTful APIs for ParkJom smart transit and parking platform (organized by business module)."
    });

    // Avoid SchemaId conflicts across controllers with similarly named DTOs
    options.CustomSchemaIds(type => type.FullName?.Replace("+", ".") ?? type.Name);

    var xmlDocumentation = Path.Combine(
        AppContext.BaseDirectory,
        $"{typeof(Program).Assembly.GetName().Name}.xml");
    if (File.Exists(xmlDocumentation))
    {
        options.IncludeXmlComments(xmlDocumentation, includeControllerXmlComments: true);
    }

    // Automatically categorize all controllers into core business modules
    options.TagActionsBy(api =>
    {
        var controller = api.ActionDescriptor.RouteValues["controller"] ?? "Unknown";
        var path = api.RelativePath ?? string.Empty;

        if (path.StartsWith("api/owner", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "02. Owner Portal" };
        }

        if (controller == "PublicParkingLot")
        {
            return new[] { "03. Public Drive-In Parking" };
        }

        var module = controller switch
        {
            // 01. Auth & Users
            "Auth" or "User" or "Vehicle" or "AccessLog" or "Media"
                => "01. Auth & Users",

            // 02. Owner Portal
            var c when c.StartsWith("Owner")
                => "02. Owner Portal",

            // 06. Support & Disputes (prioritize before Admin because of AdminSupport*)
            var c when c.StartsWith("AdminSupport") || c.StartsWith("Support")
                => "06. Support & Disputes",

            // 08. Admin Operations
            var c when c.StartsWith("Admin")
                => "08. Admin Operations",

            // 04. Booking & Checkout (handles CommuterBookings, BookingCheckout, etc.)
            var c when c.Contains("Booking")
                => "04. Booking & Checkout",

            // 05. Wallet & Payment
            var c when c.StartsWith("Wallet") || c is "Payment" or "Refund" or "Stripe"
                => "05. Wallet & Payment",

            // 07. GTFS & Transit
            var c when c.StartsWith("Gtfs")
                => "07. GTFS & Transit",

            // 09. Copilot & Notifications
            "Copilot" or "Notifications"
                => "09. Copilot & Notifications",

            // 03. Parking & Search (handles Parking, ParkingSearch, ParkingPrediction, Property, Station, etc.)
            var c when c.Contains("Parking") || c is "Property" or "Station" or "Favorites" or "Reviews"
                => "03. Parking & Search",

            // 10. IoT & Smart Bollard
            var c when c.StartsWith("IoT", StringComparison.OrdinalIgnoreCase)
                => "10. IoT & Smart Bollard",

            // 11. Property Management
            var c when c.StartsWith("PropertyManagement")
                => "11. Property Management",

            // 12. System Jobs & Webhooks
            "InternalCron" or "Sms360Webhook"
                => "12. System Jobs & Webhooks",

            _ => $"99. Other Endpoints ({controller})"
        };

        return new[] { module };
    });

    // Enforce tag sorting in the generated OpenAPI specification
    options.DocumentFilter<SwaggerTagOrderFilter>();

    options.OrderActionsBy(apiDesc => $"{apiDesc.ActionDescriptor.RouteValues["controller"]}_{apiDesc.HttpMethod}");

    // Integrated JWT Bearer authorization support
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token (no need to include 'Bearer ' prefix)."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy =>
        {
            policy.WithOrigins(
                "http://127.0.0.1:5500",
                "http://localhost:3000",
                "https://localhost:3000",
                "http://127.0.0.1:3000",
                "https://127.0.0.1:3000",
                "https://united-perigee-400000.web.app"
            )
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
});

var app = builder.Build();

// Ensure property names and addresses in DB are consistent (fixes PV18 Residences having PV9 Residences address)
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pv18 = await db.Properties.FirstOrDefaultAsync(p => p.PropertyId == 31);
        if (pv18 != null && pv18.PropertyName.Contains("PV18", StringComparison.OrdinalIgnoreCase) &&
            pv18.Address != null && pv18.Address.Contains("PV9", StringComparison.OrdinalIgnoreCase))
        {
            pv18.Address = "PV18 Residences, Jalan Langkawi, Setapak, Kuala Lumpur, Malaysia";
            pv18.UpdatedAt = DateTime.UtcNow;
        }

        var pv18Test = await db.Properties.FirstOrDefaultAsync(p => p.PropertyId == 26);
        if (pv18Test != null && pv18Test.Address == "testing")
        {
            pv18Test.PropertyName = "PV18 Residences";
            pv18Test.Address = "PV18 Residences, Jalan Langkawi, Setapak, Kuala Lumpur, Malaysia";
            pv18Test.UpdatedAt = DateTime.UtcNow;
        }

        var pv128 = await db.Properties.FirstOrDefaultAsync(p => p.PropertyId == 33);
        if (pv128 != null && pv128.Address == "Jalan Langkawi")
        {
            pv128.Address = "PV128, Jalan Genting Kelang, Setapak, Kuala Lumpur, Malaysia";
            pv128.UpdatedAt = DateTime.UtcNow;
        }

        // Recalculate realistic pedestrian walking times (12.5 min/km = 80 m/min)
        var allProps = await db.Properties.ToListAsync();
        foreach (var p in allProps)
        {
            var distKm = (double)p.DistanceToStation;
            if (distKm > 0)
            {
                var walkMin = (decimal)Math.Round(distKm * 12.5, 1);
                if (p.TimeToStation != walkMin)
                {
                    p.TimeToStation = walkMin;
                }
            }
        }

        await db.SaveChangesAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Property address normalization check skipped.");
    }
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseDefaultFiles();
app.UseStaticFiles();

// Swagger Documentation Middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ParkJom V2 API v1");
    c.RoutePrefix = "swagger";
    c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None); // 默认折叠所有模块，方便快速翻阅
    c.DisplayRequestDuration(); // 显示接口响应耗时
    c.ConfigObject.AdditionalItems["tagsSorter"] = "alpha";
});

app.UseWebSockets();
app.UseMiddleware<SupportWebSocketMiddleware>();

app.UseAuthentication();   
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var isManagementEndpoint = path.StartsWithSegments("/api/management") || path.StartsWithSegments("/api/auth/management");
    if (!isManagementEndpoint)
    {
        await next();
        return;
    }

    async Task WriteManagementAuditAsync(bool succeeded, string? detail = null)
    {
        if (context.Items.ContainsKey("PropertyManagementAuditRecorded")) return;

        var managerIdText = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? context.User.FindFirstValue("sub");
        var isManagerToken = string.Equals(context.User.FindFirst("UserType")?.Value,
            UserType.PropertyManagement.ToString(), StringComparison.Ordinal);
        int? managerId = isManagerToken && int.TryParse(managerIdText, out var parsedId) ? parsedId : null;
        int? propertyId = null;
        if (managerId.HasValue)
        {
            var db = context.RequestServices.GetRequiredService<ApplicationDbContext>();
            propertyId = await db.PropertyManagements.AsNoTracking()
                .Where(account => account.ManagementAccountId == managerId.Value)
                .Select(account => (int?)account.PropertyId)
                .FirstOrDefaultAsync(context.RequestAborted);
        }

        var statusCode = context.Response.StatusCode;
        var action = $"{context.Request.Method} {path}";
        var auditDetails = $"httpStatus={statusCode}; traceId={context.TraceIdentifier}";
        if (!string.IsNullOrWhiteSpace(detail)) auditDetails += $"; {detail}";
        var audit = context.RequestServices.GetRequiredService<AccessLogService>();
        await audit.LogManagementAsync(managerId, propertyId, action, succeeded, auditDetails);
    }

    try
    {
        await next();
        var recordedResult = context.Items.TryGetValue("PropertyManagementAuditSuccess", out var outcome)
            ? outcome is true
            : context.Response.StatusCode < StatusCodes.Status400BadRequest;
        var detail = context.Items.TryGetValue("PropertyManagementAuditDetail", out var auditDetail)
            ? auditDetail as string
            : null;
        await WriteManagementAuditAsync(recordedResult, detail);
    }
    catch (Exception)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await WriteManagementAuditAsync(false, "request failed with an unhandled server exception");
        throw;
    }
});
app.UseAuthorization();

app.UseRateLimiter();

app.MapControllers();
app.MapHub<SupportHub>("/hubs/support");
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();

static bool IsPhoneOnboardingEndpoint(PathString path)
    => path.StartsWithSegments("/api/auth/complete-profile")
       || path.StartsWithSegments("/api/auth/phone/otp/status")
       || path.StartsWithSegments("/api/auth/logout");

/// <summary>
/// Orders OpenAPI tags alphabetically (e.g. 01, 02, 03...) across the document.
/// </summary>
public class SwaggerTagOrderFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        swaggerDoc.Tags = swaggerDoc.Paths
            .SelectMany(p => p.Value.Operations.Values)
            .SelectMany(op => op.Tags)
            .Select(t => t.Name)
            .Distinct()
            .OrderBy(name => name)
            .Select(name => new OpenApiTag { Name = name })
            .ToList();
    }
}
