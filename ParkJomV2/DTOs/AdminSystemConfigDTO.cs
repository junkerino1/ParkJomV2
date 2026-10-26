namespace ParkJomV2.DTOs;

public class SystemConfigDTO
{
    public decimal CommissionRate { get; set; }
    public int GracePeriodMinutes { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public string? UpdatedByName { get; set; }
}

public class UpdateSystemConfigRequest
{
    public decimal? CommissionRate { get; set; }
    public int? GracePeriodMinutes { get; set; }
}

public class SystemConfigResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public SystemConfigDTO? Data { get; set; }
}
