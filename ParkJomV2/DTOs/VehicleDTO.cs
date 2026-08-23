using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public class AddVehicleRequest
{
    [Required(ErrorMessage = "Number plate is required")]
    [StringLength(20)]
    public string NumberPlate { get; set; } = string.Empty;

    [StringLength(50)]
    public string? VehicleBrand { get; set; }

    [StringLength(50)]
    public string? VehicleModel { get; set; }

    [StringLength(30)]
    public string? VehicleColor { get; set; }
}

public class ModifyVehicleRequest
{
    [Required]
    public int VehicleId { get; set; }

    [Required(ErrorMessage = "Number plate is required")]
    [StringLength(20)]
    public string NumberPlate { get; set; } = string.Empty;

    [StringLength(50)]
    public string? VehicleBrand { get; set; }

    [StringLength(50)]
    public string? VehicleModel { get; set; }

    [StringLength(30)]
    public string? VehicleColor { get; set; }
}

public class VehicleResponseDTO
{
    public int VehicleId { get; set; }
    public string NumberPlate { get; set; } = string.Empty;
    public string? VehicleBrand { get; set; }
    public string? VehicleModel { get; set; }
    public string? VehicleColor { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? OwnerEmail { get; set; }
    public string? OwnerName { get; set; }
}

public class VehicleListResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<VehicleResponseDTO> Data { get; set; } = new();
}

public class AddVehicleResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public VehicleResponseDTO? Data { get; set; }
}

public class ModifyVehicleResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public VehicleResponseDTO? Data { get; set; }
}

public class DeleteVehicleResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
