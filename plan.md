# ParkJom owner listing and period-booking API plan

## Summary

Use a private listing lifecycle:

```text
Verification approved
→ PendingConfiguration
→ Configured
→ Published
→ Searchable / bookable
```

A `ParkingSpot` is never returned by public APIs until it is approved, configured, and explicitly published. Full-day bookings use inclusive customer dates: `2026-01-01` to `2026-01-20` reserves 20 days, stored internally as `[2026-01-01 00:00, 2026-01-21 00:00)` in `Asia/Kuala_Lumpur`.

## Owner APIs

| Method / endpoint | Input | Behaviour |
|---|---|---|
| `PUT /api/owner/parking/{spotId}/configuration` | `description`, `instructions`, `dailyRate`, `monthlyRate`, image add/remove/reorder data | Owner-only; requires approved verification. Saves listing content and pricing but does not publish. Require at least one image, valid rates, description, and instructions before marking configuration complete. |
| `POST /api/owner/parking/{spotId}/images` | Multipart image files | Upload public listing images and create `ParkingSpotImage` rows. Verification documents remain separate and private. |
| `PUT /api/owner/parking/{spotId}/images/{imageId}` | `displayOrder`, `isPrimary` | Reorder images or set the one primary image. |
| `DELETE /api/owner/parking/{spotId}/images/{imageId}` | — | Delete an unneeded listing image; reject removal of the last image if already published. |
| `POST /api/owner/parking/{spotId}/availability-rules` | Bulk `rules[]` containing `fromDate`, `toDate`, `fromTime`, `toTime`, `dayPattern` (`Weekdays` or `Everyday`) | Creates one or more recurring/date-bounded availability rules. This is preferable to “setup”: it describes the resource and supports later editing. |
| `PUT /api/owner/parking/{spotId}/availability-rules/{ruleId}` | Same fields as rule creation | Update an unbooked rule. Reject a change that removes coverage from confirmed bookings. |
| `DELETE /api/owner/parking/{spotId}/availability-rules/{ruleId}` | — | Remove a rule only when it does not make an existing confirmed booking invalid. |
| `GET /api/owner/parking/{spotId}/availability-calendar?month=YYYY-MM` | Calendar month | Returns a calculated calendar, not rows stored per day: each date has configured hours and `available`, `booked`, or `unavailable` state. Do not return renter identity here. |
| `GET /api/owner/bookings?spotId={id}&month=YYYY-MM&status={status}` | Filters | Returns booking summaries for the owner’s spots only. |
| `GET /api/owner/bookings/{bookingId}` | — | Returns authorised booking detail, including renter-visible vehicle information and financial status. |
| `POST /api/owner/parking/{spotId}/publish` | — | Validates verification, configuration, image, pricing, and at least one future availability rule; then sets `IsPublished = true`, `AvailabilityStatus = Available`. |
| `POST /api/owner/parking/{spotId}/unpublish` | — | Removes the listing from future public searches while preserving confirmed bookings. |

Use ISO date values in APIs (`YYYY-MM-DD` and `YYYY-MM`), not `DD-MM-YYYY`; ISO is unambiguous and standard across booking platforms.

### Availability calendar response

Do not persist a nested `spot → date → hour → booking` structure. That duplicates data and becomes unreliable when rules change. Generate it when requested from:

```text
Availability rules
− owner date blocks, if added later
− confirmed/active bookings
```

Example response shape:

```json
{
  "parkingSpotId": 12,
  "month": "2026-01",
  "timeZone": "Asia/Kuala_Lumpur",
  "days": [
    {
      "date": "2026-01-05",
      "configuredHours": [{"from": "07:00", "to": "21:00"}],
      "status": "available"
    },
    {
      "date": "2026-01-06",
      "configuredHours": [{"from": "07:00", "to": "21:00"}],
      "status": "booked"
    }
  ]
}
```

The booking list/detail endpoints are the separate source of renter and vehicle details. This mirrors mature platforms: availability and reservation details are related, but they are not exposed through one oversized calendar payload.

## Public discovery, quote, and booking APIs

| Method / endpoint | Input | Behaviour |
|---|---|---|
| `GET /api/public/parking/search?startDate=YYYY-MM-DD&endDate=YYYY-MM-DD` | Inclusive dates | Returns only published, approved spots whose configured hours cover every requested date and have no confirmed/active overlap. |
| `GET /api/public/parking/{spotId}?startDate=YYYY-MM-DD&endDate=YYYY-MM-DD` | Inclusive dates | Returns listing details, images, instructions, applicable hours for the requested period, and a current availability result. |
| `POST /api/public/parking/{spotId}/booking-quotes` | `vehicleId`, `startDate`, `endDate`, optional future `voucherCode` | Authenticated user only. Rechecks ownership, lead time, availability, and vehicle. Returns a short-lived server-generated quote. |
| `POST /api/bookings/confirm` | `quoteId`, `vehicleId` | Authenticated user only. Creates and pays for the booking atomically from wallet balance. Require an `Idempotency-Key` request header so a double tap/retry cannot produce two bookings. |
| `GET /api/bookings/my` | Optional status/date filters | Renter’s booking history and upcoming bookings. |
| `GET /api/bookings/{bookingId}` | — | Booking detail for renter, owner, or admin only. |
| `POST /api/bookings/{bookingId}/cancel` | Optional cancellation reason | Calculates and executes the relevant refund from the server-side booking snapshot. |

“Public” means discoverable application functionality; quote and confirmation must still require authentication.

### Quote rules

The quote endpoint is necessary. It makes price calculation authoritative, supports future vouchers/rewards, and freezes the price the renter saw.

Pricing policy:

```text
1–9 days:     dailyRate × numberOfDays
10–20 days:   dailyRate × 90% × numberOfDays
21+ days:     (monthlyRate ÷ 30) × numberOfDays
```

Example with `dailyRate = RM10`, `monthlyRate = RM150`:

```text
5 days  = RM50
10 days = RM90
20 days = RM180
21 days = RM105
```

A quote returns:

```text
quoteId
expiresAt
startDate / endDate / bookedDays
pricing tier and rate snapshot
rentalSubtotal
platformCommissionRate = 10%
platformCommissionAmount
ownerPayoutAmount
renterTotal
```

The renter pays `renterTotal`; ParkJom retains 10% of the retained rental amount, and the owner receives 90%.

## Confirmation and financial flow

On `POST /api/bookings/confirm`, execute one database transaction:

1. Load the quote and verify it is unexpired and belongs to the user.
2. Recalculate requested-date availability; never rely on the earlier quote alone.
3. Lock the affected spot/booking range during the check so concurrent confirmation requests cannot both reserve it.
4. Verify the vehicle belongs to the user and wallet balance covers the total.
5. Create the `Booking` directly as `Confirmed`.
6. Decrease renter wallet `Balance` by the full renter total.
7. Create a completed renter payment transaction linked to `BookingId`.
8. Increase owner wallet `OnHold` by the owner payout (90%).
9. Record the 10% commission in a platform ledger/transaction.
10. Commit the transaction and return booking reference plus financial summary.

A confirmed booking blocks its selected date range. It does not globally set the spot to `Occupied`; the spot remains searchable for other non-overlapping dates.

At checkout:

```text
Confirmed → Active → Completed
```

When completed, move the owner amount from `OnHold` to owner `Balance`. Calculate overstay from actual exit time:

```text
penaltyHours = ceiling(overstayMinutes / 60)
penalty = penaltyHours × RM5
```

Charge the renter wallet, record a separate penalty transaction, and add the owner’s 90% portion to their held payout.

Cancellation:

```text
At least 3 days before start: 100% refund
Within 3 days of start:       50% refund
```

For a late cancellation, calculate commission from the retained half only; refund the other half to the renter and release the owner’s 90% share when settlement rules allow.

## Model and state changes

- Add `PendingConfiguration` to `AvailabilityStatus`; after admin verification approval, set the spot to this state with `IsPublished = false`.
- Keep `Available`, `Inactive`, and `Deleted` for global listing state. Add `Active` to `BookingStatus`.
- Continue using `Availability` as the availability-rule table; its `DayType`, times, and effective dates cover the requested owner form.
- Add a `ParkingSpotBlock` model later for one-off owner date blocks; it is separate from recurring rules.
- Extend `Booking` with immutable pricing and settlement fields: `BookedDays`, `RateType`, `RatePerDaySnapshot`, `RentalSubtotal`, `PlatformCommissionAmount`, `OwnerPayoutAmount`, `QuoteId`, `CheckedInAt`, `ActualExitAt`, `OverstayHours`, and `OverstayPenaltyAmount`.
- Extend the transaction/ledger model to represent platform commission, owner held earnings, refund, and overstay payment. A booking’s financial rows must always reference `BookingId`.
- Add a unique booking reference and a unique idempotency record keyed by renter plus `Idempotency-Key`.

## Test plan

- Reject public search/detail results for unapproved, pending-configuration, unpublished, inactive, and deleted spots.
- Confirm that dates are inclusive: 1–20 January equals 20 booked days and overlaps 20 January.
- Verify weekday-only and whole-week availability over weekdays, weekends, and month boundaries.
- Verify a confirmed booking removes only its own dates from search/calendar availability.
- Send two simultaneous confirmations for the same spot/date range; exactly one succeeds.
- Reject a booking starting today; accept one starting tomorrow.
- Verify each pricing tier, 10% commission, owner 90% held payout, and immutable rate snapshot.
- Verify full and 50% refunds, with correct reversal of held owner funds and commission.
- Verify image, configuration, availability, and verification prerequisites for publish.
- Verify owner calendar contains no renter identity, while owner booking-detail access works only for their own spot.
