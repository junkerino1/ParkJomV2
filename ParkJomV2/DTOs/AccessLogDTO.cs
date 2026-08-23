namespace ParkJomV2.DTOs;

public class AccessLogDTO
{
    public int AccessLogId { get; set; }
    public int? BookingId { get; set; }
    public int? UserId { get; set; }
    public int? IoTDeviceId { get; set; }
    public string Actions { get; set; } = string.Empty;
    public DateTime AccessedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UserEmail { get; set; }
    public string? UserName { get; set; }
}

public class AccessLogListResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<AccessLogDTO> Data { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }
    public string Sort { get; set; } = "accessedAt:desc";
    public string? Type { get; set; }
    public string? Search { get; set; }
}
