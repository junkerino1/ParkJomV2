using ParkJomV2.DTOs;
using ParkJomV2.Models;

namespace ParkJomV2.Services;

/// <summary>
/// Handles property lookup and creation during parking registration.
/// Checks the DB first; if not found, searches Nominatim and creates a new property.
/// </summary>
public interface IPropertyService
{
    /// <summary>
    /// Returns an existing property matching the request, or creates a new one
    /// via Nominatim lookup + OSRM distance calculation.
    /// Returns null if the property cannot be found or created.
    /// </summary>
    Task<Property?> ResolvePropertyAsync(ParkingRegistrationRequest request);
}