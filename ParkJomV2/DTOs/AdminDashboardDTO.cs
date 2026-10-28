namespace ParkJomV2.DTOs;

public class AdminDashboardStatsDTO
{
    public decimal TotalRevenue { get; set; }
    public decimal PlatformCommission { get; set; }
    public decimal GrossBookingValue { get; set; }
    public decimal OwnerPayoutTotal { get; set; }
    public int ActiveBookings { get; set; }
    public int TotalUsers { get; set; }
    public int TotalOwners { get; set; }
    public int TotalCommuters { get; set; }
    public int OnlineBollardsRate { get; set; }
    public int TotalBollards { get; set; }
    public int OnlineBollards { get; set; }
    public int PendingListingsCount { get; set; }
    public int OpenDisputesCount { get; set; }
    public int ActiveOverstaysCount { get; set; }
    public int OpenTicketsCount { get; set; }
}

public class AdminDashboardBookingCountsDTO
{
    public int Pending { get; set; }
    public int Confirmed { get; set; }
    public int Active { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int Expired { get; set; }
    public int Total { get; set; }
}

public class DashboardDailyPointDTO
{
    public string Day { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public int Bookings { get; set; }
    public decimal Revenue { get; set; }
    public decimal Commission { get; set; }
}

public class DashboardStationPointDTO
{
    public string Name { get; set; } = string.Empty;
    public int Bookings { get; set; }
    public decimal Revenue { get; set; }
}

public class DashboardHourlyPointDTO
{
    public string Hour { get; set; } = string.Empty;
    public int Bookings { get; set; }
}

public class DashboardUserGrowthPointDTO
{
    public string Month { get; set; } = string.Empty;
    public int Drivers { get; set; }
    public int Owners { get; set; }
}

public class DashboardRevenueTypePointDTO
{
    public string Type { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int Bookings { get; set; }
}

public class AdminDashboardDataDTO
{
    public AdminDashboardStatsDTO Stats { get; set; } = new();
    public AdminDashboardBookingCountsDTO BookingCounts { get; set; } = new();
    public List<DashboardDailyPointDTO> Weekly { get; set; } = new();
    public List<DashboardStationPointDTO> Stations { get; set; } = new();
    public List<DashboardHourlyPointDTO> Hourly { get; set; } = new();
    public List<DashboardUserGrowthPointDTO> UserGrowth { get; set; } = new();
    public List<DashboardRevenueTypePointDTO> RevenueByType { get; set; } = new();
    public string GeneratedAt { get; set; } = string.Empty;
    public string TimeZone { get; set; } = "Asia/Kuala_Lumpur";
}

public class AdminDashboardResponse
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public AdminDashboardDataDTO? Data { get; set; }
}
