using ParkJomV2.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public class CreatePropertyRequest
{
    [Required(ErrorMessage = "Property name is required")]
    [StringLength(100, ErrorMessage = "Property name cannot exceed 100 characters")]
    public string PropertyName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Property type is required")]
    public PropertyType PropertyType { get; set; }

    [Required(ErrorMessage = "Address is required")]
    [StringLength(255, ErrorMessage = "Address cannot exceed 255 characters")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "Latitude is required")]
    [Range(-90, 90, ErrorMessage = "Latitude must be between -90 and 90")]
    public decimal Latitude { get; set; }

    [Required(ErrorMessage = "Longitude is required")]
    [Range(-180, 180, ErrorMessage = "Longitude must be between -180 and 180")]
    public decimal Longitude { get; set; }

    [Required(ErrorMessage = "Nearest Station is required")]
    public int NearestStationId { get; set; }

    [Required(ErrorMessage = "OSM ID is required")]
    public long OsmId { get; set; }

    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters")]
    public string? Description { get; set; }
}

public class UpdatePropertyRequest
{
    [StringLength(100, ErrorMessage = "Property name cannot exceed 100 characters")]
    public string? PropertyName { get; set; }

    public PropertyType? PropertyType { get; set; }

    [StringLength(255, ErrorMessage = "Address cannot exceed 255 characters")]
    public string? Address { get; set; }

    [Range(-90, 90, ErrorMessage = "Latitude must be between -90 and 90")]
    public decimal? Latitude { get; set; }

    [Range(-180, 180, ErrorMessage = "Longitude must be between -180 and 180")]
    public decimal? Longitude { get; set; }

    public int? NearestStationId { get; set; }

    public long? OsmId { get; set; }

    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters")]
    public string? Description { get; set; }
}

public class PropertyDTO
{
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public PropertyType PropertyType { get; set; }
    public string Address { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public int NearestStationId { get; set; }
    public decimal DistanceToStation { get; set; }
    public decimal TimeToStation { get; set; }
    public long? OsmId { get; set; }
    public string? Description { get; set; }
    public VerificationStatus VerificationStatus { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}