# Automatic IoT Device Secret Provisioning

## Registration endpoint

`POST /api/iot/register`

This endpoint is called by the ESP32 when it starts. It does not require a user token.

Request:

```json
{
  "deviceUuid": "0a00d80a-eaf8-415a-9f80-3453a6ef1067"
}
```

## First registration

The backend:

1. Creates the `IoTDevices` record.
2. Generates a cryptographically random 256-bit secret.
3. Stores the Base64 secret in `IoTDevices.device_secrets`.
4. Returns the secret once so the ESP32 can save it in NVS.

Response: `201 Created`

```json
{
  "code": 201,
  "success": true,
  "message": "Device registered and secret provisioned.",
  "data": {
    "deviceUuid": "0a00d80a-eaf8-415a-9f80-3453a6ef1067",
    "deviceStatus": "Online",
    "isAssigned": false,
    "parkingSpotId": null,
    "deviceSecret": "<one-time generated secret>"
  }
}
```

The firmware saves `data.deviceSecret` under the existing `parkjom/hmacSecret` NVS key. It never prints the secret.

## Later registrations

The existing device, secret, and parking-spot assignment are preserved. The secret is omitted from the JSON response.

Response: `200 OK`

```json
{
  "code": 200,
  "success": true,
  "message": "Device already registered; existing secret and assignment preserved.",
  "data": {
    "deviceUuid": "0a00d80a-eaf8-415a-9f80-3453a6ef1067",
    "deviceStatus": "Online",
    "isAssigned": true,
    "parkingSpotId": 24
  }
}
```

An existing database row with no secret is treated as a legacy device: its next registration generates and returns a secret once.

## BLE verification

The unlock endpoint now reads the secret from the assigned `IoTDevices` row. `IoT__DeviceSecrets__<uuid>` environment variables are no longer used.

The database migration is `AddIoTDeviceSecrets`. Apply it before deploying the updated firmware:

```powershell
dotnet ef database update --project ParkJomV2 --startup-project ParkJomV2
```

## Security limitation

Registration currently identifies hardware only by its UUID. A UUID is not a password, so this first-registration flow is appropriate for controlled setup and demonstrations, but not strong factory-grade device identity. Production hardware should also have a factory enrollment token, certificate, or device public key. Restrict database access and backups because `device_secrets` contains authentication keys.
