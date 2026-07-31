using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;

namespace ParkJomV2.Services;

public class PropertyService : IPropertyService
{
    private readonly ApplicationDbContext _context;
    private readonly NominatimService _nominatimService;
    private readonly OsrmService _osrmService;
    private readonly ILogger<PropertyService> _logger;

    public PropertyService(
        ApplicationDbContext context,
        NominatimService nominatimService,
        OsrmService osrmService,
        ILogger<PropertyService> logger)
    {
        _context = context;
        _nominatimService = nominatimService;
        _osrmService = osrmService;
        _logger = logger;
    }

    public async Task<Property?> ResolvePropertyAsync(ParkingRegistrationRequest request)
    {
        // Check DB for existing property by OsmId
        var existing = await _context.Properties
            .FirstOrDefaultAsync(p => p.OsmId == request.osmId);

        if (existing != null)
        {
            // property exist, return the existing property
            return existing;
        }

        // 2) Not in DB — search Nominatim for the property
        var nominatimResults = await _nominatimService.SearchAsync(request.PropertyName, limit: 1);
        var best = nominatimResults?.FirstOrDefault();

        if (best == null)
        {
            _logger.LogWarning("Nominatim returned no results for '{Name}'", request.PropertyName);
            return null;
        }

        // validate cordinates to ensure they are valid decimal values
        if (!decimal.TryParse(best.Lat, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var latitude) ||
            !decimal.TryParse(best.Lon, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var longitude))
        {
            _logger.LogWarning("Invalid coordinates from Nominatim for '{Name}'", request.PropertyName);
            return null;
        }

        // verify the nearest station exists in db
        var station = await _context.Stations.FirstOrDefaultAsync(s => s.StationName == request.NearestStationName);
        if (station == null)
        {
            _logger.LogWarning("Station {StationName} not found during property creation", request.NearestStationName);
            return null;
        }

        // Calculate walking distance/time via OSRM
        var (distKm, timeMin) = await _osrmService.GetWalkingDistanceAsync(
            (double)station.Latitude, (double)station.Longitude,
            (double)latitude, (double)longitude);

        // 6) Create the property
        var property = new Property
        {
            PropertyName = best.PropertyName,
            PropertyType = request.PropertyType,
            Address = best.Address,
            Latitude = latitude,
            Longitude = longitude,
            NearestStationId = station.StationId,
            DistanceToStation = (decimal)(distKm ?? 0),
            TimeToStation = (decimal)(timeMin ?? 0),
            OsmId = best.OsmId,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Properties.Add(property);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Property created from Nominatim: PropertyId={PropertyId}, OsmId={OsmId}, Name={Name}",
            property.PropertyId, best.OsmId, property.PropertyName);

        return property;
    }
}