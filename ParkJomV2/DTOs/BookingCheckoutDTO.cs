using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public class BookingAvailabilityTimeRangeData
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class BookingAvailabilityDateData
{
    public string Date { get; set; } = string.Empty;
    public List<BookingAvailabilityTimeRangeData> TimeRanges { get; set; } = new();
}

public class BookingAvailabilityResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ParkingSpotId { get; set; }
    public string Month { get; set; } = string.Empty;
    public string TimeZone { get; set; } = "Asia/Kuala_Lumpur";
    public string MinimumBookingDate { get; set; } = string.Empty;
    public int TotalAvailableDates { get; set; }
    public List<BookingAvailabilityDateData> AvailableDates { get; set; } = new();
}

public class CreateBookingQuoteRequest
{
    [Required]
    [RegularExpression("^\\d{4}-\\d{2}-\\d{2}$", ErrorMessage = "startDate must use YYYY-MM-DD format.")]
    public string StartDate { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^\\d{4}-\\d{2}-\\d{2}$", ErrorMessage = "endDate must use YYYY-MM-DD format.")]
    public string EndDate { get; set; } = string.Empty;

    [StringLength(100)]
    public string? VoucherCode { get; set; }
}

public class BookingQuoteResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public BookingQuoteData? Data { get; set; }
}

public class BookingQuoteData
{
    public Guid QuoteId { get; set; }
    public int ParkingSpotId { get; set; }
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public int BookedDays { get; set; }
    public string RateType { get; set; } = string.Empty;
    public decimal RatePerDay { get; set; }
    public decimal RentalSubtotal { get; set; }
    public decimal PlatformFee { get; set; }
    public decimal RenterTotal { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class ConfirmBookingRequest
{
    [Required]
    public Guid QuoteId { get; set; }

    [Range(1, int.MaxValue)]
    public int VehicleId { get; set; }
}

public class ConfirmBookingResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public ConfirmedBookingData? Data { get; set; }
}

public class ConfirmedBookingData
{
    public int BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public Guid QuoteId { get; set; }
    public int ParkingSpotId { get; set; }
    public int VehicleId { get; set; }
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public int BookedDays { get; set; }
    public string BookingStatus { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
