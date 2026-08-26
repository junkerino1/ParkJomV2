using CloudinaryDotNet.Core;

namespace ParkJomV2.Models.Enums
{
    public enum AvailabilityStatus
    {
        Available = 1,
        // Approved listing that still needs owner configuration.
        // Keep the existing persisted value/name for database compatibility.
        Pending = 2,
        Inactive = 3,
        Deleted = 4
    }
}
