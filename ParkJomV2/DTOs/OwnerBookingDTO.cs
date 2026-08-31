namespace ParkJomV2.DTOs;

public class OwnerBookingSummaryResponse
{
    public int BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public int ParkingSpotId { get; set; }
    public string? ParkingLabel { get; set; }
    public int RenterId { get; set; }
    public string RenterName { get; set; } = string.Empty;
    public string RenterEmail { get; set; } = string.Empty;
    public string? RenterPhoneNumber { get; set; }
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
    public int TotalCount { get; set; }
    public List<OwnerBookingSummaryResponse> Data { get; set; } = new();
}

public class OwnerBookingDetailResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string TimeZone { get; set; } = "Asia/Kuala_Lumpur";
    public OwnerBookingDetailDataResponse? Data { get; set; }
}

public class OwnerBookingDetailDataResponse
{
    public int BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public int ParkingSpotId { get; set; }
    public string? ParkingLabel { get; set; }
    public OwnerBookingRenterResponse Renter { get; set; } = new();
    public OwnerBookingVehicleResponse Vehicle { get; set; } = new();
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public int BookedDays { get; set; }
    public string BookingStatus { get; set; } = string.Empty;
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public DateTime? ActualExitAt { get; set; }
    public OwnerBookingFinancialResponse Financial { get; set; } = new();
    public List<OwnerBookingTransactionResponse> Transactions { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class OwnerBookingRenterResponse
{
    public int RenterId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
}

public class OwnerBookingVehicleResponse
{
    public int VehicleId { get; set; }
    public string NumberPlate { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Color { get; set; }
}

public class OwnerBookingFinancialResponse
{
    public string RateType { get; set; } = string.Empty;
    public decimal RatePerDaySnapshot { get; set; }
    public decimal RentalSubtotal { get; set; }
    public decimal RenterTotal { get; set; }
    public decimal PlatformCommissionRate { get; set; }
    public decimal PlatformCommissionAmount { get; set; }
    public decimal OwnerPayoutAmount { get; set; }
    public decimal RefundAmount { get; set; }
    public int OverstayHours { get; set; }
    public decimal OverstayPenaltyAmount { get; set; }
}

public class OwnerBookingTransactionResponse
{
    public int TransactionId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string TransactionStatus { get; set; } = string.Empty;
    public string ReferenceNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
