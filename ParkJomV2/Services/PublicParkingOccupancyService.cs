using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;

namespace ParkJomV2.Services;

public sealed class PublicParkingOccupancyService(ApplicationDbContext db)
{
    public async Task<bool> RecordAsync(PublicParkingOccupancyReading reading, CancellationToken ct = default)
    {
        var lot = await db.PublicParkingLots.SingleOrDefaultAsync(
            lot => lot.IotDeviceUuid == reading.IotDeviceUuid, ct);

        if (lot is null)
            throw new InvalidOperationException("No public parking lot is linked to this device UUID.");

        if (!PublicParkingOccupancy.Apply(lot, reading))
            return false;

        await db.SaveChangesAsync(ct);
        return true;
    }
}
