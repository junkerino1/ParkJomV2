using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

public class CreateReviewRequest
{
    [Required]
    public int BookingId { get; set; }

    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5.")]
    public int Rating { get; set; }

    [StringLength(1000, ErrorMessage = "Comment cannot exceed 1000 characters.")]
    public string? Comment { get; set; }

    public List<string>? Photos { get; set; }
}

public class UpdateReviewRequest
{
    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5.")]
    public int Rating { get; set; }

    [StringLength(1000, ErrorMessage = "Comment cannot exceed 1000 characters.")]
    public string? Comment { get; set; }

    public List<string>? Photos { get; set; }
}

public class OwnerReplyRequest
{
    [Required(ErrorMessage = "Owner reply is required.")]
    [StringLength(1000, ErrorMessage = "Owner reply cannot exceed 1000 characters.")]
    public string OwnerReply { get; set; } = string.Empty;
}

public class ReviewDTO
{
    public int ReviewId { get; set; }
    public int ParkingSpotId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public List<string> Photos { get; set; } = new();
    public string ReviewerDisplayName { get; set; } = string.Empty;
    public bool IsVerifiedBooking { get; set; }
    public string? OwnerReply { get; set; }
    public DateTime? OwnerReplyAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ReviewResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public ReviewDTO? Data { get; set; }
}

public class DeleteReviewResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ParkingReviewsResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ParkingSpotId { get; set; }
    public int TotalCount { get; set; }
    public double AverageRating { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<ReviewDTO> Data { get; set; } = new();
}

public class ReviewListResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<ReviewDTO> Data { get; set; } = new();
}

public class AllReviewsResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public List<ReviewDTO> Data { get; set; } = new();
}

public class ReportReviewRequest
{
    [Required(ErrorMessage = "Reason is required.")]
    [StringLength(100, ErrorMessage = "Reason cannot exceed 100 characters.")]
    public string Reason { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Details cannot exceed 1000 characters.")]
    public string? Details { get; set; }
}

public class ReviewReportDTO
{
    public int ReviewReportId { get; set; }
    public int ReviewId { get; set; }
    public int ReporterId { get; set; }
    public string ReporterDisplayName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public ReviewDTO? Review { get; set; }
}

public class ReportReviewResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? ReportId { get; set; }
}

public class ReviewReportsResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<ReviewReportDTO> Data { get; set; } = new();
}
