using Google.GenAI;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ---------- Add Controllers support (AuthController etc.) ----------
builder.Services.AddControllers();

// ---------- Configure JWT Bearer authentication ----------
var jwtKey = "ParkJom_SuperSecret_Key_2026_AtLeast_32_Characters!!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "ParkJom",
            ValidAudience = "ParkJom",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        };
    });
builder.Services.AddAuthorization();

// ---------- CORS (allow Vite dev server and Firebase Hosting) ----------
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });

    options.AddPolicy("FirebaseCors", policy =>
    {
        policy.WithOrigins("https://united-perigee-400000.web.app")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Configure the Google GenAI Client
string apiKey = builder.Configuration["GoogleGenAI:ApiKey"] ?? "AIzaSyDcRgkswx6PgPxJQuZxVw-JOsvfUYGRHRA";
var client = new Client(apiKey: apiKey);

// Register the AI client as a singleton service so it's accessible throughout MVC controllers and Minimal APIs
builder.Services.AddSingleton(client);

// ---------- Persistent user store (file-based JSON) ----------
builder.Services.AddSingleton<ParkJomV2.Web.Services.UserStoreService>();

var app = builder.Build();

// ---------- Middleware Pipeline ----------
if (app.Environment.IsDevelopment())
{
    app.UseCors("DevCors");
}
else
{
    app.UseCors("FirebaseCors");
}

app.UseAuthentication();
app.UseAuthorization();

// Serve static files from wwwroot (where Vite builds the React SPA)
app.UseDefaultFiles();
app.UseStaticFiles();

// ---------- Map Controllers ----------
app.MapControllers();

// --- API Endpoints ---
// Example Minimal API endpoint using the registered client
app.MapGet("/api/hello", (Client genAiClient) =>
    Results.Ok(new { message = "Hello from ParkJom Backend!" }));

// --- Parking Spots Nearby API ---
// Haversine formula to calculate distance between two GPS coordinates (in meters)
static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
{
    const double R = 6371000; // Earth's radius in meters
    double dLat = (lat2 - lat1) * Math.PI / 180;
    double dLon = (lon2 - lon1) * Math.PI / 180;
    double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
               Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
               Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
    double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    return R * c;
}

// GET /api/parkingspots/nearby?lat=3.0849&lng=101.5873&radius=500
app.MapGet("/api/parkingspots/nearby", (double lat, double lng, double radius = 500) =>
{
    // Simulated parking spots database (in production, this would query a real database)
    var allSpots = new[]
    {
        new { Id = "SJ-01", Name = "Casa Subang Condominium - Bay 12", Lat = 3.0836, Lng = 101.5882, PricePerHour = 3.50, Type = "Condo Bay", Owner = "Lim K. H.", Station = "Subang Jaya LRT", Available = true },
        new { Id = "SJ-02", Name = "Casa Subang Condominium - Bay 45", Lat = 3.0839, Lng = 101.5886, PricePerHour = 3.50, Type = "Condo Bay", Owner = "Lim K. H.", Station = "Subang Jaya LRT", Available = true },
        new { Id = "SJ-03", Name = "Jalan SS15 Landed Driveway #4", Lat = 3.0818, Lng = 101.5880, PricePerHour = 4.00, Type = "Landed Driveway", Owner = "John Tan", Station = "Subang Jaya LRT", Available = true },
        new { Id = "SJ-04", Name = "Subang Park Homes - Block B-5", Lat = 3.0805, Lng = 101.5865, PricePerHour = 3.00, Type = "Condo Bay", Owner = "Ravi S.", Station = "Subang Jaya LRT", Available = true },
        new { Id = "KJ-01", Name = "Kelana Puteri Condo - Bay 211", Lat = 3.1138, Lng = 101.6029, PricePerHour = 3.00, Type = "Condo Bay", Owner = "Yong S. M.", Station = "Kelana Jaya LRT", Available = true },
        new { Id = "KJ-02", Name = "Kelana Puteri Condo - Driveway A", Lat = 3.1142, Lng = 101.6035, PricePerHour = 4.00, Type = "Landed Driveway", Owner = "Siti Aminah", Station = "Kelana Jaya LRT", Available = true },
        new { Id = "KJ-03", Name = "Jalan SS7 Terrace Driveway", Lat = 3.1118, Lng = 101.6055, PricePerHour = 4.50, Type = "Landed Driveway", Owner = "Chaw Chun Jia", Station = "Kelana Jaya LRT", Available = true },
        new { Id = "WM-01", Name = "PV9 Residences - Parking L6-102", Lat = 3.2052, Lng = 101.7325, PricePerHour = 3.00, Type = "Condo Bay", Owner = "Ooi Jun Kang", Station = "Wangsa Maju LRT", Available = true },
        new { Id = "WM-02", Name = "PV9 Residences - Parking L4-22", Lat = 3.2050, Lng = 101.7327, PricePerHour = 3.00, Type = "Condo Bay", Owner = "Ooi Jun Kang", Station = "Wangsa Maju LRT", Available = true },
        new { Id = "WM-03", Name = "Jalan Wangsa Melawati 3 - Driveway", Lat = 3.2040, Lng = 101.7278, PricePerHour = 3.50, Type = "Landed Driveway", Owner = "Chung W. F.", Station = "Wangsa Maju LRT", Available = true },
        new { Id = "TC-01", Name = "Cheras Hartamas - Driveway Lane 2", Lat = 3.0782, Lng = 101.7472, PricePerHour = 3.50, Type = "Landed Driveway", Owner = "Michelle S.", Station = "Taman Connaught MRT", Available = true },
        new { Id = "TC-02", Name = "Altitude 236 Condominium - L1-4", Lat = 3.0805, Lng = 101.7435, PricePerHour = 3.00, Type = "Condo Bay", Owner = "Leong S. K.", Station = "Taman Connaught MRT", Available = true },
    };

    var nearby = allSpots
        .Select(spot => new
        {
            spot.Id,
            spot.Name,
            spot.Lat,
            spot.Lng,
            spot.PricePerHour,
            spot.Type,
            spot.Owner,
            spot.Station,
            spot.Available,
            Distance = Math.Round(HaversineDistance(lat, lng, spot.Lat, spot.Lng), 1)
        })
        .Where(spot => spot.Distance <= radius && spot.Available)
        .OrderBy(spot => spot.Distance)
        .ToList();

    return Results.Ok(new
    {
        center = new { lat, lng },
        radius,
        totalSpots = nearby.Count,
        spots = nearby
    });
});

// --- SPA Fallback ---
// For any non-API route that doesn't match a static file,
// serve index.html so react-router-dom can handle client-side routing.
// This prevents 404 errors when refreshing /admin, /owner, or /commuter.
app.MapFallbackToFile("index.html");

app.Run();
