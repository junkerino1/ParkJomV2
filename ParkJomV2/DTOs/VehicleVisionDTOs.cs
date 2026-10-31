using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public class VehicleVisionScanRequest
{
    [Required]
    public string ImageBase64 { get; set; } = string.Empty;

    public string MimeType { get; set; } = "image/jpeg";

    /// <summary>
    /// Optional client-side hint (e.g. from on-device OCR or demo preset metadata)
    /// used as a fallback if cloud vision is offline.
    /// </summary>
    public string? ClientHintPlate { get; set; }
    public string? ClientHintBrand { get; set; }
    public string? ClientHintModel { get; set; }
    public string? ClientHintColor { get; set; }
}

public class PlateBoundingBoxDto
{
    public int YMin { get; set; }
    public int XMin { get; set; }
    public int YMax { get; set; }
    public int XMax { get; set; }
}

public class VehicleVisionScanDataDto
{
    public string NumberPlate { get; set; } = string.Empty;
    public string FormattedPlate { get; set; } = string.Empty;
    public string RawDetectedText { get; set; } = string.Empty;
    public bool IsValidJpjFormat { get; set; }
    public string JpjRegion { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string VehicleType { get; set; } = "Sedan";
    public double Confidence { get; set; }
    public PlateBoundingBoxDto? BoundingBox { get; set; }
    public string EngineUsed { get; set; } = string.Empty;
}

public class VehicleVisionScanResponse
{
    public int Code { get; set; } = StatusCodes.Status200OK;
    public bool Success { get; set; } = true;
    public string Message { get; set; } = string.Empty;
    public VehicleVisionScanDataDto Data { get; set; } = new();
}

public class BarrierPlateVerifyRequest
{
    public int? BookingId { get; set; }

    public string? ExpectedPlate { get; set; }

    [Required]
    public string ImageBase64 { get; set; } = string.Empty;

    public string MimeType { get; set; } = "image/jpeg";

    public string? ClientHintPlate { get; set; }
    public string? ClientHintBrand { get; set; }
    public string? ClientHintModel { get; set; }
    public string? ClientHintColor { get; set; }
}

public class BarrierPlateVerifyDataDto
{
    public bool Verified { get; set; }
    /// <summary>"exact_match" | "near_match" | "mismatch"</summary>
    public string Status { get; set; } = "mismatch";
    public double SimilarityScore { get; set; }
    public string ExpectedPlate { get; set; } = string.Empty;
    public string DetectedPlate { get; set; } = string.Empty;
    public string FormattedDetectedPlate { get; set; } = string.Empty;
    public bool IsValidJpjFormat { get; set; }
    public string JpjRegion { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public PlateBoundingBoxDto? BoundingBox { get; set; }
    public string EngineUsed { get; set; } = string.Empty;
}

public class BarrierPlateVerifyResponse
{
    public int Code { get; set; } = StatusCodes.Status200OK;
    public bool Success { get; set; } = true;
    public string Message { get; set; } = string.Empty;
    public BarrierPlateVerifyDataDto Data { get; set; } = new();
}
