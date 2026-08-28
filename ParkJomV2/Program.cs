using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ParkJomV2.Data;
using ParkJomV2.Models;
using ParkJomV2.Services;
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
            },
            OnChallenge = context =>
            {
                context.HandleResponse();

                var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
                var isSuspendedAccount = context.HttpContext.Items.ContainsKey("SuspendedAccount");

                var (statusCode, message) = isSuspendedAccount
                    ? (StatusCodes.Status403Forbidden, "Your account is suspended. Please contact support.")
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
app.UseAuthentication();   
app.UseAuthorization();
app.MapControllers();

app.Run();
