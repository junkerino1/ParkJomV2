using ParkJomV2.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public class CreateBookingRequest
{
    [Required]
    public int ParkingSpotId { get; set; }

    [Required]
    public int VehicleId { get; set; }

    [Required]
    public DateTime StartDate { get; set; }

    [Required]
    public DateTime EndDate { get; set; }
}

public class CancelBookingRequest
{
    [Required]
    public int BookingId { get; set; }

    [StringLength(500)]
    public string? CancellationReason { get; set; }
}

public class BookingResponseDTO
{
    public int BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public int RenterId { get; set; }
    public int ParkingSpotId { get; set; }
    public string? ParkingLabel { get; set; }
    public int VehicleId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string BookingStatus { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BookingListResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<BookingResponseDTO> Data { get; set; } = new();
}

public class BookingHistoryItemDTO
{
    public BookingResponseDTO Booking { get; set; } = new();
    public bool CanReview { get; set; }
    public ReviewDTO? Review { get; set; }
}

public class BookingHistoryResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<BookingHistoryItemDTO> Data { get; set; } = new();
}

public class BookingDetailResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public BookingResponseDTO? Data { get; set; }
}

public class CancelBookingResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int BookingId { get; set; }
    public string BookingStatus { get; set; } = string.Empty;
    public DateTime? CancelledAt { get; set; }
    public decimal RefundAmount { get; set; }
}

public class CommuterBookingListResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public string? Status { get; set; }
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public List<BookingResponseDTO> Data { get; set; } = new();
}


public class ActiveBookingSpotDTO
{
    public int ParkingSpotId { get; set; }
    public string? ParkingLabel { get; set; }
    public int PropertyId { get; set; }
    public string? PropertyName { get; set; }
    public string? Address { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string? StationName { get; set; }
    public decimal DistanceToStation { get; set; }
    public decimal TimeToStationInMinutes { get; set; }
    public string AvailabilityStatus { get; set; } = string.Empty;
    public decimal? MonthlyRate { get; set; }
    public decimal? DailyRate { get; set; }
    public string? PrimaryImageUrl { get; set; }
    public string? OwnerName { get; set; }
}

public class ActiveBookingDTO
{
    public int BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string VehiclePlate { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public decimal TotalPaid { get; set; }
    public ActiveBookingSpotDTO Spot { get; set; } = new();
}

public class ActiveBookingResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public ActiveBookingDTO? Data { get; set; }
    public List<ActiveBookingDTO> UpcomingBookings { get; set; } = new();
}