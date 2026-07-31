using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ParkJomV2.Data;
using ParkJomV2.Models;
using ParkJomV2.Services;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<GoogleAuthService>();

builder.Services.AddScoped<JwtTokenService>();

builder.Services.AddScoped<CloudinaryService>();

builder.Services.AddHttpClient<OsrmService>(client =>
{
    client.BaseAddress = new Uri("https://router.project-osrm.org/");
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
            OnChallenge = context =>
            {
                context.HandleResponse();

                var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();

                var (statusCode, message) = context.AuthenticateFailure switch
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

builder.Services.AddAuthorization();

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