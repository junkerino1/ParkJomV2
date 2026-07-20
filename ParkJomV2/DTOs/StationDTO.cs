using ParkJomV2.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public class GetPropertyByStationIdRequest
{
    [Required(ErrorMessage = "Station ID is required")]
    public int StationId { get; set; }
}

public class GetPropertyResponse
{
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public PropertyType PropertyType { get; set; }
    public string Address { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public decimal DistanceToStation { get; set; }

}