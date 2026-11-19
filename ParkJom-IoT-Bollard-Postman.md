# ParkJom IoT Bollard API — Postman Guide

Import [`ParkJom-IoT-Bollard.postman_collection.json`](./ParkJom-IoT-Bollard.postman_collection.json) into Postman. The collection contains all renter and admin requests and saves the challenge ID, nonce, and device UUID after a successful challenge response.

## 1. Configure the collection

Open the collection's **Variables** tab and set:

| Variable | Value |
| --- | --- |
| `baseUrl` | Your API address, for example `http://localhost:5276` |
| `bookingId` | Booking ID whose parking spot has the assigned ESP32, for example `13` |
| `renterToken` | JWT for the renter who owns that booking |
| `ownerToken` | JWT for the property owner listing their bollards |
| `adminToken` | JWT for an account with the `AdminOnly` policy |
| `latitude`, `longitude` | Current phone GPS coordinates near the property |
| `accuracyMeters` | Phone-reported GPS accuracy |
| `locationTimestamp` | Current ISO-8601 timestamp with timezone |
| `bleChallengeResponse` | Base64 HMAC proof returned by the PWA after BLE communication |

The challenge request updates `challengeId`, `nonce`, and `deviceUuid` automatically. `nonce` is informational for the BLE exchange; the PWA sends the challenge data to the ESP32 and supplies the returned HMAC proof. Never put the device secret in Postman or the PWA.

## Smart bollard inventory

| Request | Bearer token | Inventory |
| --- | --- | --- |
| `GET /api/iot/owner/devices` | `ownerToken` | Bollards linked to bays owned by the caller |
| `GET /api/iot/admin/devices` | `adminToken` | All registered bollards, including unassigned devices |

Both endpoints return `200` with `code`, `success`, `message`, and a `data` array sorted by `iotDeviceId`. The array is empty when no devices match. Each item returns `iotDeviceId`, `deviceUuid`, `deviceStatus` (last recorded status), `firmwareVersion`, `isAssigned`, `parkingSpotId`, `parkingLabel`, `propertyId`, `propertyName`, `ownerId`, `lastHeartbeatAt`, `createdAt`, and `updatedAt`. Unassigned devices have null bay, property, and owner fields. `lastHeartbeatAt` can be null.

Requests without valid authentication return `401`. Authenticated callers without the required role receive `403`.

### Heartbeat status

`GET /api/iot/{uuid}/status` requires the device's owner token or an admin token. It returns `deviceUuid`, `heartbeatStatus`, `lastHeartbeatAt`, `checkedAt`, and `offlineAfterSeconds` inside `data`. Timestamps use UTC. An unknown UUID or a device outside the owner's scope returns `404`.

- `lastDistanceScan` contains `distanceCm` and `receivedAt` for the most recent valid sensor telemetry. It is `null` if no valid reading has been recorded.
- `parkingSpot` contains `parkingSpotId`, `parkingLabel`, `propertyId`, and `propertyName`. It is `null` for an unassigned device.

The heartbeat status is `unknown` if no heartbeat has been recorded, `online` if the last heartbeat is less than 90 seconds old, and `offline` otherwise. Device registration, command acknowledgments, and sensor telemetry do not populate the heartbeat timestamp. Firmware publishing and backend ingestion must be enabled before the status can transition from `unknown`.

### Request a live distance scan

`POST /api/iot/{uuid}/status/scan` requires the assigned owner's token or an admin token. Send no request body. On success, `data` contains `deviceUuid`, `distanceCm`, `receivedAt` (UTC), and `bollardPosition`. The scan reads the sensor without actuating the bollard.

The API publishes `STATUS|-1|<commandId>` to `parkjom/devices/{uuid}/status`. Firmware subscribes to that topic and replies on the existing `ack` topic with the matching command ID. Broker permissions must allow the backend to publish and the device to subscribe to its own status topic. Periodic telemetry still feeds the cached GET endpoint.

| Status | Meaning |
| --- | --- |
| 404 | Device missing or outside the owner's inventory |
| 409 | A scan for this device is already pending on the API instance |
| 502 | Device replied without a valid reading |
| 503 | MQTT request could not be published |
| 504 | No matching reply arrived before the ACK timeout |

### Pair a bollard with a parking bay

`POST /api/iot/bind` requires a property owner token and a six-digit code shown by the device. The owner sends only the bay ID and the code:

```json
{ "parkingSpotId": 24, "otp": "032581" }
```

Keep `otp` as a string to preserve leading zeroes. The server resolves the device UUID and returns it in `data.deviceUuid` after binding. Codes expire after five minutes and are consumed on success. An invalid, expired, or ambiguous code returns `400`. A bay outside the owner's inventory returns `404`. An assignment conflict returns `409`. More than five attempts per minute per owner on one API instance can return `429`.

## 2. Renter unlock flow

### Step 1 — Request a challenge

**POST** `{{baseUrl}}/api/iot/{{bookingId}}/unlock/challenge`

Authorization: **Bearer Token** `{{renterToken}}`

```json
{
  "location": {
    "latitude": 3.0738,
    "longitude": 101.5183,
    "accuracyMeters": 8.5,
    "timestamp": "2026-11-03T16:30:10+08:00"
  }
}
```

Example success response (**200 OK**):

```json
{
  "success": true,
  "message": "Unlock verification challenge generated.",
  "data": {
    "challengeId": "CHL-8f2a91",
    "nonce": "BASE64_ENCODED_RANDOM_NONCE",
    "expiresAt": "2026-11-03T08:30:40Z",
    "deviceUuid": "7d9f2c41-8a63-4b72-9e15-6c2a0f84b731",
    "bleServiceUuid": "6e400001-b5a3-f393-e0a9-e50e24dcca9e",
    "locationVerified": true,
    "distanceMeters": 18.6
  }
}
```

The challenge is short-lived (30 seconds). Complete the BLE exchange in your PWA and copy its base64 `challengeResponse` into the Postman collection variable `bleChallengeResponse` if you are using Postman to submit the final call. Postman itself does not perform BLE.

### Step 2 — Submit the BLE proof and unlock

**POST** `{{baseUrl}}/api/iot/{{bookingId}}/unlock`

Authorization: **Bearer Token** `{{renterToken}}`

```json
{
  "challengeId": "{{challengeId}}",
  "location": {
    "latitude": 3.0738,
    "longitude": 101.5183,
    "accuracyMeters": 8.5,
    "timestamp": "2026-11-03T16:30:20+08:00"
  },
  "bleVerification": {
    "deviceUuid": "{{deviceUuid}}",
    "challengeResponse": "{{bleChallengeResponse}}",
    "rssi": -58
  }
}
```

Example accepted response (**202 Accepted**):

```json
{
  "success": true,
  "message": "Location verified. Bollard unlock command sent.",
  "data": {
    "bookingId": 13,
    "deviceUuid": "7d9f2c41-8a63-4b72-9e15-6c2a0f84b731",
    "locationVerified": true,
    "bleVerified": true,
    "distanceMeters": 18.6,
    "unlockCommandSent": true,
    "executionStatus": "awaiting_device_ack"
  }
}
```

`202 Accepted` means MQTT accepted the command; check the device ACK/logs for the physical result.

## 3. Renter lock

**POST** `{{baseUrl}}/api/iot/{{bookingId}}/lock`

Authorization: **Bearer Token** `{{renterToken}}`. No request body is required.

The backend requires a telemetry reading no older than six minutes. Lock is rejected if the HC-SR04 reports **50 cm or less**. The ESP32 also checks the sensor when it receives `LOCK`.

Example accepted response (**202 Accepted**):

```json
{
  "success": true,
  "message": "Bollard lock command sent.",
  "data": {
    "bookingId": 13,
    "deviceUuid": "7d9f2c41-8a63-4b72-9e15-6c2a0f84b731",
    "command": "LOCK",
    "executionStatus": "awaiting_device_ack"
  }
}
```

Obstacle response (**409 Conflict**):

```json
{
  "success": false,
  "errorCode": "OBSTACLE_DETECTED",
  "message": "Obstacle detected within 50 cm. Move it clear before locking the bollard.",
  "data": {
    "distanceCm": 32.4,
    "thresholdCm": 50
  }
}
```

If telemetry is missing, stale, or unreadable, the endpoint returns **409 Conflict** with `errorCode: "SENSOR_READING_UNAVAILABLE"` and does not send the command.

## 4. Admin commands

Both endpoints require an authenticated admin JWT. They bypass renter booking eligibility and GPS/BLE checks. They still need the booking to resolve to an assigned ESP32. **Admin lock retains the HC-SR04 obstacle interlock**; admin unlock has no GPS/BLE check.

### Admin lock

**POST** `{{baseUrl}}/api/admin/iot/{{bookingId}}/lock`

Authorization: **Bearer Token** `{{adminToken}}`. No request body.

It returns the same `OBSTACLE_DETECTED` and `SENSOR_READING_UNAVAILABLE` errors as renter lock. If clear, it returns **202 Accepted** with the command and `validationOverride: true`.

### Admin unlock

**POST** `{{baseUrl}}/api/admin/iot/{{bookingId}}/unlock`

Authorization: **Bearer Token** `{{adminToken}}`. No request body.

Example accepted response (**202 Accepted**):

```json
{
  "success": true,
  "message": "Admin bollard unlock command sent.",
  "data": {
    "bookingId": 13,
    "deviceUuid": "7d9f2c41-8a63-4b72-9e15-6c2a0f84b731",
    "command": "UNLOCK",
    "validationOverride": true,
    "executionStatus": "awaiting_device_ack"
  }
}
```

## Common errors

| HTTP status | `errorCode` | Meaning |
| --- | --- | --- |
| 401 | — | Missing or invalid bearer token |
| 403 | — | Authenticated account is not an admin for admin routes |
| 404 | `BOOKING_NOT_FOUND` | Booking was not found or is not owned by this renter |
| 409 | `BOOKING_NOT_ELIGIBLE` | Renter booking is not eligible or outside the access window |
| 409 | `DEVICE_NOT_ASSIGNED` | Booking has no assigned ESP32 |
| 409 | `OBSTACLE_DETECTED` | HC-SR04 distance is 50 cm or less; lock is blocked |
| 409 | `SENSOR_READING_UNAVAILABLE` | No fresh, readable HC-SR04 telemetry is available |
| 410 | `CHALLENGE_EXPIRED` | Unlock challenge expired before submission |
| 409 | `CHALLENGE_ALREADY_USED` | Challenge was already consumed |
| 403 | `BLE_VERIFICATION_FAILED` | BLE HMAC proof did not validate |
| 503 | `MQTT_UNAVAILABLE` | Backend cannot publish to the configured broker |

All device actions and their outcomes are recorded in the existing `IoTStatusLogs` table. The broker and device remain behind the ASP.NET API; the PWA must not connect to MQTT.
