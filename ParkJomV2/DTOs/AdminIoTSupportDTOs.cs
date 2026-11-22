namespace ParkJomV2.DTOs;

public sealed record AdminIoTSupportPage(
    IReadOnlyList<AdminIoTSupportDevice> Items, int Page, int PageSize, bool HasMore);

public sealed record AdminIoTSupportDevice(
    int IoTDeviceId,
    string DeviceUuid,
    string DeviceStatus,
    string FirmwareVersion,
    DateTime? LastHeartbeatAt,
    bool IsAssigned,
    bool CanControl,
    AdminIoTSupportSpot? ParkingSpot,
    AdminIoTSupportBooking? Booking);

public sealed record AdminIoTSupportSpot(
    int ParkingSpotId,
    string? ParkingLabel,
    string AvailabilityStatus,
    bool IsPublished,
    AdminIoTSupportPerson Owner,
    AdminIoTSupportProperty Property);

public sealed record AdminIoTSupportPerson(int UserId, string Name);

public sealed record AdminIoTSupportProperty(
    int PropertyId, string PropertyName, string Address, decimal Latitude, decimal Longitude);

public sealed record AdminIoTSupportBooking(
    int BookingId,
    string BookingReference,
    string BookingStatus,
    DateTime StartDate,
    DateTime EndDate,
    DateTime? CheckedInAt,
    DateTime? ActualExitAt,
    AdminIoTSupportPerson Renter);
