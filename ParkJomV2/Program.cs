using Microsoft.EntityFrameworkCore;
using ParkJomV2.Models;
using ParkJomV2.Data;
using ParkJomV2.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<GoogleAuthService>();

builder.Services.AddScoped<JwtTokenService>(); 

builder.Services.AddControllers();

var app = builder.Build();

app.UseHttpsRedirection();

app.MapControllers();

app.Run();