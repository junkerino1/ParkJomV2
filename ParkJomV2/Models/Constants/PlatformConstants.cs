namespace ParkJomV2.Models.Constants;

/// <summary>
/// Global platform-wide constants shared across the codebase.
/// </summary>
public static class PlatformConstants
{
    /// <summary>
    /// Platform commission rate (as a percentage) retained by ParkJom
    /// from every booking rental subtotal. Owner receives (100 - rate)%.
    /// </summary>
    public const decimal CommissionRate = 10m;
}
