namespace ParkJomV2.Models.Enums;

public enum PublicParkingSessionStatus
{
    Parked = 1,          // Vehicle currently parked in the lot
    PaymentPending = 2,  // Checkout started, awaiting Stripe or Wallet confirmation
    Paid = 3,            // Paid successfully, within exit grace period
    Exited = 4,          // Vehicle has departed, spot released
    Cancelled = 5        // Session cancelled or voided by owner/admin
}
