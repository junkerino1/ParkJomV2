namespace ParkJomV2.DTOs;

public class OwnerBookingSummaryResponse
{
    public int BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public int ParkingSpotId { get; set; }
    public string? ParkingLabel { get; set; }
    public int RenterId { get; set; }
    public string RenterName { get; set; } = string.Empty;
    public int VehicleId { get; set; }
    public string VehicleNumberPlate { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public int BookedDays { get; set; }
    public string BookingStatus { get; set; } = string.Empty;
    public decimal RenterTotal { get; set; }
    public decimal OwnerPayoutAmount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class OwnerBookingListResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? ParkingSpotId { get; set; }
    public string? Month { get; set; }
    public string? Status { get; set; }
    public string TimeZone { get; set; } = "Asia/Kuala_Lumpur";
    public List<OwnerBookingSummaryResponse> Data { get; set; } = new();
}
