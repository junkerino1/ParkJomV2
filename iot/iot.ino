#include <WiFi.h>
#include <PubSubClient.h>
#include <ESP32Servo.h>
#include <Preferences.h>
#include <HTTPClient.h>
#include <NimBLEDevice.h>
#include <mbedtls/md.h>
#include <mbedtls/base64.h>
#include <ctype.h>
#include <errno.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

// =============================================
// 1. WI-FI CONFIGURATION
// =============================================

const char* ssid = "FBIVan@unifi";
const char* password = "Pota+0CHIP";

// =============================================
// 2. MQTT CONFIGURATION
// =============================================

const char* mqttServer = "dns-my.samsam123.name.my";
const int mqttPort = 48881;
const char* apiBaseUrl = "https://parkjom-api-gbgcbycbcjghczgu.malaysiawest-01.azurewebsites.net";

// =============================================
// 3. DEVICE IDENTITY
// =============================================

// Device UUID command Serial Monitor: SET_UUID=<uuid>.
// Serial Monitor command SET_SECRET=<secret>.
// Keep the matching server value in IoT:DeviceSecrets:<device UUID>;
const char* BLE_SERVICE_UUID = "6e400001-b5a3-f393-e0a9-e50e24dcca9e";
const char* BLE_CHALLENGE_UUID = "6e400002-b5a3-f393-e0a9-e50e24dcca9e";
const char* BLE_PROOF_UUID = "6e400003-b5a3-f393-e0a9-e50e24dcca9e";
const char* BLE_SECRET_PREF_KEY = "hmacSecret";

Preferences preferences;

String deviceId;
String deviceHmacSecret;
String mqttUsername;
String mqttPassword;
NimBLECharacteristic* bleProofCharacteristic = nullptr;
NimBLEAdvertising* bleAdvertising = nullptr;
String activeBleChallengeId;
String activeBleNonce;
volatile bool bleChallengeActive = false;
unsigned long bleChallengeStopAt = 0;
const unsigned long bleChallengeWindowMs = 30000;
void stopBleChallenge();

String commandTopic;
String ackTopic;
String telemetryTopic;
String statusTopic;

// =============================================
// 4. HARDWARE CONFIGURATION
// =============================================

constexpr uint8_t SERVO_PIN = 18;
constexpr uint8_t TRIG_PIN = 5;
constexpr uint8_t ECHO_PIN = 19;
constexpr uint8_t OTP_BUTTON_PIN = 23;

Servo bollardServo;

// =============================================
// 5. NETWORK CLIENTS
// =============================================

WiFiClient espClient;
PubSubClient client(espClient);

// =============================================
// 6. DEVICE STATE
// =============================================

bool bollardUnlocked = false;

unsigned long lastSensorUpdate = 0;

const unsigned long sensorInterval = 60000UL;
const unsigned long mqttReconnectInterval = 5000;
const unsigned long wifiReconnectInterval = 10000;
const unsigned long registrationRetryInitialDelay = 15000;
const unsigned long registrationRetryMaxDelay = 300000;
const unsigned long buttonDebounceInterval = 50;

bool deviceRegistered = false;
bool registrationBlocked = false;
unsigned long registrationRetryDelay = registrationRetryInitialDelay;
unsigned long lastMqttReconnectAttempt = 0;
unsigned long lastWifiReconnectAttempt = 0;
unsigned long lastRegistrationAttempt = 0;
uint8_t lastButtonReading = HIGH;
uint8_t stableButtonState = HIGH;
unsigned long lastButtonDebounceAt = 0;

// =============================================
// 7. LOAD DEVICE UUID FROM NVS
// =============================================

bool isValidDeviceUuid(const String& value) {
  if (value.length() != 36) return false;
  for (unsigned int i = 0; i < 36; i++) {
    if (i == 8 || i == 13 || i == 18 || i == 23) {
      if (value[i] != '-') return false;
    } else if (!isxdigit(static_cast<unsigned char>(value[i]))) {
      return false;
    }
  }
  return true;
}

void loadDeviceUuid() {
  preferences.begin("parkjom", false);
  deviceId = preferences.getString("deviceUuid", "");
  preferences.end();

  if (!isValidDeviceUuid(deviceId)) {
    deviceId = "";
    Serial.println(F("[DEVICE] UUID not provisioned. Enter SET_UUID=<uuid> in Serial Monitor."));
    return;
  }
  deviceId.toLowerCase();
  Serial.println(F("Existing device UUID loaded from NVS."));

  // Generate MQTT topics dynamically
  commandTopic = "parkjom/devices/" + deviceId + "/commands";

  ackTopic = "parkjom/devices/" + deviceId + "/ack";

  telemetryTopic = "parkjom/devices/" + deviceId + "/telemetry";
  statusTopic = "parkjom/devices/" + deviceId + "/status";

  // Display device identity
  Serial.print(F("[DEVICE] UUID: "));
  Serial.println(deviceId);
}

void loadMqttCredentials() {
  preferences.begin("parkjom", true);
  mqttUsername = preferences.getString("mqttUser", "");
  mqttPassword = preferences.getString("mqttPass", "");
  preferences.end();
  Serial.println(mqttUsername.length() > 0 && mqttPassword.length() > 0
    ? F("[MQTT] Device credentials loaded from NVS.")
    : F("[MQTT][WARNING] Device credentials are not provisioned."));
}

// The phone writes: challengeId|base64Nonce|deviceUuid. The HMAC input matches
// the backend exactly: base64Nonce + lowercaseDeviceUuid + challengeId.
class BleChallengeCallbacks : public NimBLECharacteristicCallbacks {
  void onWrite(NimBLECharacteristic* characteristic, NimBLEConnInfo& connInfo) override {
    (void)connInfo;
    String request = characteristic->getValue().c_str();
    Serial.println(F("[BLE][CHALLENGE] Challenge request received."));

    const int firstSeparator = request.indexOf('|');
    const int secondSeparator = request.indexOf('|', firstSeparator + 1);
    if (firstSeparator <= 0 || secondSeparator <= firstSeparator + 1) {
      Serial.println(F("[BLE][ERROR] Invalid challenge format; expected challengeId|nonce|deviceUuid."));
      return;
    }

    const String challengeId = request.substring(0, firstSeparator);
    const String nonce = request.substring(firstSeparator + 1, secondSeparator);
    const String requestedDevice = request.substring(secondSeparator + 1);
    if (!bleChallengeActive) {
      Serial.println(F("[BLE][ERROR] No active backend challenge; BLE proof was not generated."));
      return;
    }
    if (!requestedDevice.equalsIgnoreCase(deviceId) || nonce.length() == 0 || challengeId.indexOf('|') >= 0) {
      Serial.println(F("[BLE][ERROR] Challenge device UUID or fields did not validate."));
      return;
    }
    if (challengeId != activeBleChallengeId || nonce != activeBleNonce) {
      Serial.println(F("[BLE][ERROR] BLE challenge does not match the active backend challenge."));
      return;
    }
    if (deviceHmacSecret.length() < 32) {
      Serial.println(F("[BLE][ERROR] Device secret is not provisioned. Send SET_SECRET=<base64-secret> over USB Serial."));
      return;
    }

    String canonicalDeviceId = deviceId;
    canonicalDeviceId.toLowerCase();
    const String message = nonce + canonicalDeviceId + challengeId;
    const mbedtls_md_info_t* md = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
    unsigned char digest[32];
    const int hmacResult = mbedtls_md_hmac(
      md,
      reinterpret_cast<const unsigned char*>(deviceHmacSecret.c_str()),
      deviceHmacSecret.length(),
      reinterpret_cast<const unsigned char*>(message.c_str()),
      message.length(),
      digest
    );
    if (hmacResult != 0) {
      Serial.println(F("[BLE][ERROR] HMAC-SHA256 calculation failed."));
      return;
    }

    unsigned char encoded[48];
    size_t encodedLength = 0;
    const int encodeResult = mbedtls_base64_encode(encoded, sizeof(encoded), &encodedLength, digest, sizeof(digest));
    if (encodeResult != 0) {
      Serial.println(F("[BLE][ERROR] Could not encode HMAC response."));
      return;
    }

    bleProofCharacteristic->setValue(encoded, encodedLength);
    bleProofCharacteristic->notify();
    Serial.println(F("[BLE][CHALLENGE] HMAC proof generated and notified (proof omitted from logs)."));
    stopBleChallenge();
  }
};

void loadDeviceHmacSecret() {
  preferences.begin("parkjom", true);
  deviceHmacSecret = preferences.getString(BLE_SECRET_PREF_KEY, "");
  preferences.end();
  Serial.println(deviceHmacSecret.length() >= 32
    ? "[BLE] Device HMAC secret loaded from NVS."
    : "[BLE][WARNING] No device HMAC secret in NVS; BLE verification will fail until provisioned.");
}

void startBleChallengeService() {
  String bleName = "ParkJom-" + deviceId.substring(max(0, (int)deviceId.length() - 6));
  NimBLEDevice::init(bleName.c_str());
  Serial.printf("[BLE] BLE address: %s\n",NimBLEDevice::getAddress().toString().c_str());
  NimBLEServer* server = NimBLEDevice::createServer();
  NimBLEService* service = server->createService(BLE_SERVICE_UUID);
  NimBLECharacteristic* challengeCharacteristic = service->createCharacteristic(
    BLE_CHALLENGE_UUID, NIMBLE_PROPERTY::WRITE | NIMBLE_PROPERTY::WRITE_NR);
  bleProofCharacteristic = service->createCharacteristic(
    BLE_PROOF_UUID, NIMBLE_PROPERTY::READ | NIMBLE_PROPERTY::NOTIFY);
  challengeCharacteristic->setCallbacks(new BleChallengeCallbacks());
  bleProofCharacteristic->setValue("");
  service->start();
  bleAdvertising = NimBLEDevice::getAdvertising();
  bleAdvertising->addServiceUUID(BLE_SERVICE_UUID);
  // Put the friendly name in the scan response so phone/browser pickers can show it.
  // The name is only a discovery hint; backend HMAC validation remains the authentication.
  bleAdvertising->setName(bleName.c_str());
  bleAdvertising->enableScanResponse(true);
  Serial.print(F("[BLE] Device name: "));
  Serial.println(bleName);
  Serial.print(F("[BLE] GATT challenge service UUID: "));
  Serial.println(BLE_SERVICE_UUID);
  Serial.println(F("[BLE] GATT service initialized; advertising stays off until backend creates a challenge."));
  Serial.println(F("[BLE] Device name is for discovery only; authentication uses the HMAC proof."));
}

bool activateBleForChallenge(const char* challengeId, const char* nonce) {
  if (bleAdvertising == nullptr || *challengeId == '\0' || *nonce == '\0') {
    Serial.println(F("[BLE][ERROR] Cannot activate BLE: service or challenge data is missing."));
    return false;
  }

  bleChallengeActive = false;
  if (bleAdvertising->isAdvertising()) bleAdvertising->stop();
  activeBleChallengeId = challengeId;
  activeBleNonce = nonce;
  bleChallengeStopAt = millis() + bleChallengeWindowMs;

  if (!bleAdvertising->start()) {
    Serial.println(F("[BLE][ERROR] Advertising failed to start for the challenge."));
    return false;
  }

  bleChallengeActive = true;
  Serial.print(F("[BLE] Advertising started for challenge "));
  Serial.println(challengeId);
  return true;
}

void stopBleChallenge() {
  bleChallengeActive = false;
  if (bleAdvertising != nullptr && bleAdvertising->isAdvertising()) {
    bleAdvertising->stop();
  }
  Serial.println(F("[BLE] Challenge window ended; advertising stopped."));
}

void handleSecretProvisioning() {
  constexpr char uuidPrefix[] = "SET_UUID=";
  constexpr char userPrefix[] = "SET_MQTT_USER=";
  constexpr char passwordPrefix[] = "SET_MQTT_PASSWORD=";
  constexpr char secretPrefix[] = "SET_SECRET=";
  if (!Serial.available()) return;
  String line = Serial.readStringUntil('\n');
  line.trim();
  if (line == "SHOW_UUID") {
    Serial.print(F("[DEVICE] UUID: "));
    Serial.println(deviceId.length() > 0 ? deviceId : String("not provisioned"));
    return;
  }
  if (line.startsWith(uuidPrefix)) {
    String value = line.substring(sizeof(uuidPrefix) - 1);
    value.trim();
    if (!isValidDeviceUuid(value)) {
      Serial.println(F("[DEVICE][ERROR] Expected a UUID in xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx format. Nothing was changed."));
      return;
    }
    value.toLowerCase();
    if (value == deviceId) {
      Serial.println(F("[DEVICE] This UUID is already configured."));
      return;
    }
    preferences.begin("parkjom", false);
    const size_t saved = preferences.putString("deviceUuid", value);
    preferences.end();
    if (saved == 0) {
      Serial.println(F("[DEVICE][ERROR] UUID save failed. Existing identity was preserved."));
      return;
    }
    Serial.print(F("[DEVICE] UUID saved: "));
    Serial.println(value);
    Serial.println(F("[DEVICE] Restarting to apply the new UUID to registration, MQTT and BLE."));
    Serial.flush();
    delay(100);
    ESP.restart();
    return;
  }
  if (line.startsWith(userPrefix)) {
    const String value = line.substring(sizeof(userPrefix) - 1);
    if (value.length() == 0 || value.length() > 128) {
      Serial.println(F("[MQTT][PROVISION][ERROR] Username length is invalid."));
      return;
    }
    preferences.begin("parkjom", false);
    const size_t saved = preferences.putString("mqttUser", value);
    preferences.end();
    if (saved > 0) {
      loadMqttCredentials();
      Serial.println(F("[MQTT][PROVISION] Username saved to NVS; value was not logged."));
    } else {
      Serial.println(F("[MQTT][PROVISION][ERROR] Username save failed."));
    }
    return;
  }
  if (line.startsWith(passwordPrefix)) {
    const String value = line.substring(sizeof(passwordPrefix) - 1);
    if (value.length() == 0 || value.length() > 128) {
      Serial.println(F("[MQTT][PROVISION][ERROR] Password length is invalid."));
      return;
    }
    preferences.begin("parkjom", false);
    const size_t saved = preferences.putString("mqttPass", value);
    preferences.end();
    if (saved > 0) {
      loadMqttCredentials();
      Serial.println(F("[MQTT][PROVISION] Password saved to NVS; value was not logged."));
    } else {
      Serial.println(F("[MQTT][PROVISION][ERROR] Password save failed."));
    }
    return;
  }
  if (!line.startsWith(secretPrefix)) return;

  String secret = line.substring(sizeof(secretPrefix) - 1);
  if (secret.length() < 32 || secret.length() > 128) {
    Serial.println(F("[BLE][PROVISION][ERROR] Secret length must be 32 to 128 characters; value was not saved."));
    return;
  }
  preferences.begin("parkjom", false);
  const size_t saved = preferences.putString(BLE_SECRET_PREF_KEY, secret);
  preferences.end();
  secret = "";
  if (saved > 0) {
    loadDeviceHmacSecret();
    Serial.println(F("[BLE][PROVISION] Device secret saved to NVS. Secret value was not logged."));
  } else {
    Serial.println(F("[BLE][PROVISION][ERROR] NVS save failed."));
  }
}

// Read a simple JSON string field from the backend's response envelope without an extra library.
String jsonStringField(const String& json, const char* fieldName) {
  const String key = String("\"") + fieldName + "\"";
  const int keyPosition = json.indexOf(key);
  if (keyPosition < 0) return "";

  const int colonPosition = json.indexOf(':', keyPosition + key.length());
  if (colonPosition < 0) return "";

  const int quotePosition = json.indexOf('"', colonPosition + 1);
  if (quotePosition < 0) return "";

  String value;
  bool escaped = false;
  for (int i = quotePosition + 1; i < json.length(); i++) {
    const char current = json[i];
    if (escaped) {
      value += current;
      escaped = false;
    } else if (current == '\\') {
      escaped = true;
    } else if (current == '"') {
      return value;
    } else {
      value += current;
    }
  }
  return "";
}

// Confirm the common ASP.NET response envelope reports success before using returned data.
bool jsonSuccess(const String& json) {
  String normalizedJson = json;
  normalizedJson.toLowerCase();
  const int keyPosition = normalizedJson.indexOf("\"success\"");
  if (keyPosition < 0) return false;
  const int colonPosition = normalizedJson.indexOf(':', keyPosition);
  if (colonPosition < 0) return false;
  String value = normalizedJson.substring(colonPosition + 1);
  value.trim();
  return value.startsWith("true");
}

// Register the persistent NVS UUID at boot; retries are handled by loop() if the API is unavailable.
void registerDeviceWithBackend() {
  if (registrationBlocked) {
    Serial.println(F("[HTTP][REGISTER][STOPPED] Fix the permanent HTTP/configuration error, then restart the ESP32."));
    return;
  }
  if (WiFi.status() != WL_CONNECTED) {
    Serial.println(F("[HTTP][REGISTER] Skipped: Wi-Fi is not connected."));
    return;
  }

  const String url = String(apiBaseUrl) + "/api/iot/register";
  const String requestBody = String("{\"deviceUuid\":\"") + deviceId + "\"}";
  HTTPClient http;
  http.setTimeout(8000);

  Serial.println(F("[HTTP][REGISTER] Registering device."));

  if (!http.begin(url)) {
    Serial.println(F("[HTTP][REGISTER][ERROR] Could not initialize HTTP client."));
    return;
  }

  http.addHeader("Content-Type", "application/json");
  const int statusCode = http.POST(requestBody);
  const String responseBody = statusCode > 0 ? http.getString() : "";
  Serial.print(F("[HTTP][REGISTER] Status: "));
  Serial.println(statusCode);
  if (statusCode <= 0) {
    Serial.print(F("[HTTP][REGISTER][ERROR] "));
    Serial.println(http.errorToString(statusCode));
  }

  deviceRegistered = statusCode >= 200 && statusCode < 300 && jsonSuccess(responseBody);
  if (deviceRegistered) {
    registrationRetryDelay = registrationRetryInitialDelay;
    Serial.println(F("[HTTP][REGISTER] Registration confirmed (new or already registered). No more registration retries needed."));
  } else if ((statusCode >= 300 && statusCode < 400)
      || (statusCode >= 400 && statusCode < 500 && statusCode != 429)
      || (statusCode >= 200 && statusCode < 300)) {
    // Redirects, client errors, and unexpected success bodies will not improve by repeated posting.
    registrationBlocked = true;
    Serial.print(F("[HTTP][REGISTER][STOPPED] Response: "));
    Serial.println(responseBody);
  } else {
    Serial.print(F("[HTTP][REGISTER][WARNING] Temporary failure. Next retry in "));
    Serial.print(registrationRetryDelay / 1000);
    Serial.println(F(" seconds."));
    if (registrationRetryDelay < registrationRetryMaxDelay) {
      registrationRetryDelay = min(registrationRetryDelay * 2, registrationRetryMaxDelay);
    }
  }
  http.end();
}

// Request a short-lived binding code when the physical button is pressed.
void requestBindingOtp() {
  Serial.println();
  Serial.println(F("[OTP] Physical button press detected."));
  if (WiFi.status() != WL_CONNECTED) {
    Serial.println(F("[OTP][ERROR] Cannot request OTP: Wi-Fi is disconnected."));
    return;
  }
  if (!deviceRegistered) {
    Serial.println(F("[OTP][ERROR] Device is not registered yet; wait for registration retry."));
    return;
  }

  const String url = String(apiBaseUrl) + "/api/iot/gen-otp";
  const String requestBody = String("{\"deviceUuid\":\"") + deviceId + "\"}";
  HTTPClient http;
  http.setTimeout(8000);

  Serial.println(F("[OTP] Requesting binding code."));

  if (!http.begin(url)) {
    Serial.println(F("[OTP][ERROR] Could not initialize HTTP client."));
    return;
  }

  http.addHeader("Content-Type", "application/json");
  const int statusCode = http.POST(requestBody);
  const String responseBody = statusCode > 0 ? http.getString() : "";
  Serial.print(F("[OTP] HTTP status: "));
  Serial.println(statusCode);

  if (statusCode <= 0) {
    Serial.print(F("[OTP][ERROR] "));
    Serial.println(http.errorToString(statusCode));
  }

  const String otp = jsonStringField(responseBody, "otp");
  const String expiresAtUtc = jsonStringField(responseBody, "expiresAtUtc");
  if (statusCode == 200 && jsonSuccess(responseBody) && otp.length() == 6) {
    Serial.println(F("[OTP] ========================================"));
    Serial.print(F("[OTP] BINDING CODE: "));
    Serial.println(otp);
    Serial.print(F("[OTP] Expires at (UTC): "));
    Serial.println(expiresAtUtc);
    Serial.println(F("[OTP] Enter this code in ParkJom to bind this device."));
    Serial.println(F("[OTP] Keep this code private; it is valid for five minutes."));
    Serial.println(F("[OTP] ========================================"));
  } else {
    Serial.print(F("[OTP][ERROR] Invalid OTP response: "));
    Serial.println(responseBody);
  }

  http.end();
}

// =============================================
// 8. ULTRASONIC SENSOR
// =============================================

float readDistance() {
  constexpr uint8_t maxAttempts = 3;
  constexpr unsigned long quietIntervalMs = 60;
  constexpr float minDistanceCm = 2.0f;
  constexpr float maxDistanceCm = 400.0f;
  static unsigned long lastPingAt = millis() - quietIntervalMs;

  for (uint8_t attempt = 1; attempt <= maxAttempts; attempt++) {
    // Space out pings even when two callers request fresh readings back-to-back.
    const unsigned long elapsedMs = millis() - lastPingAt;
    if (elapsedMs < quietIntervalMs) {
      delay(quietIntervalMs - elapsedMs);
    }
    lastPingAt = millis();

    digitalWrite(TRIG_PIN, LOW);
    delayMicroseconds(3);
    digitalWrite(TRIG_PIN, HIGH);
    delayMicroseconds(10);
    digitalWrite(TRIG_PIN, LOW);

    const unsigned long duration = pulseIn(ECHO_PIN, HIGH, 30000UL);
    const float distanceCm = duration * 0.0343f / 2.0f;
    Serial.print(F("[HC-SR04] Fresh sample "));
    Serial.print(attempt);
    Serial.print(F("/"));
    Serial.print(maxAttempts);
    Serial.print(F(": "));

    if (duration == 0) {
      Serial.println(F("no echo"));
    } else if (distanceCm < minDistanceCm || distanceCm > maxDistanceCm) {
      Serial.print(F("out of range ("));
      Serial.print(distanceCm, 1);
      Serial.println(F(" cm)"));
    } else {
      Serial.print(distanceCm, 1);
      Serial.println(F(" cm"));
      return distanceCm;
    }
  }

  Serial.println(F("[HC-SR04][ERROR] No valid reading after three attempts."));
  return -1.0f;
}

// =============================================
// 9. MQTT TELEMETRY
// =============================================

void publishTelemetry() {

  const float distance = readDistance();

  Serial.print(F("[SENSOR] Distance: "));
  if (distance < 0) {
    Serial.println(F("no valid echo"));
  } else {
    Serial.print(distance, 2);
    Serial.println(F(" cm"));
  }
  if (!client.connected()) {
    Serial.println(F("[MQTT][TELEMETRY][WARNING] Not connected; telemetry was not published."));
    return;
  }

  char distanceJson[8] = "null"; // Largest valid distance is 400.00.
  if (distance >= 0) snprintf(distanceJson, sizeof(distanceJson), "%.2f", distance);
  char payload[112];
  const int length = snprintf(payload, sizeof(payload),
    "{\"deviceId\":\"%s\",\"distanceCm\":%s,\"bollardPosition\":\"%s\"}",
    deviceId.c_str(), distanceJson, bollardUnlocked ? "UNLOCKED" : "LOCKED");
  const bool published = length > 0 && static_cast<size_t>(length) < sizeof(payload) &&
    client.publish(telemetryTopic.c_str(), payload);

  if (!published) {
    Serial.print(F("[MQTT][TELEMETRY][ERROR] Publish failed. MQTT state: "));
    Serial.println(client.state());
  } else {
    Serial.println(F("[MQTT][TELEMETRY] Published."));
  }
}

// =============================================
// 10. MQTT ACKNOWLEDGEMENT
// =============================================

void publishAcknowledgement(
  const char* command,
  const char* result,
  int bookingId,
  const char* commandId,
  float distanceCm = -1.0f
) {
  const bool isStatus = strcmp(command, "STATUS") == 0;
  char bookingField[25] = "";
  if (bookingId != 0 || isStatus) {
    snprintf(bookingField, sizeof(bookingField), "\"bookingId\":%d,", bookingId);
  }
  char distanceField[22] = "";
  if (isStatus) {
    if (distanceCm >= 0) {
      snprintf(distanceField, sizeof(distanceField), "\"distanceCm\":%.2f,", distanceCm);
    } else {
      memcpy(distanceField, "\"distanceCm\":null,", sizeof("\"distanceCm\":null,"));
    }
  }

  const bool hasCommandId = *commandId != '\0';
  char payload[240];
  const int length = snprintf(payload, sizeof(payload),
    "{\"deviceId\":\"%s\",\"command\":\"%s\",%s%s%s%s\"result\":\"%s\",%s\"bollardPosition\":\"%s\"}",
    deviceId.c_str(), command, bookingField,
    hasCommandId ? "\"commandId\":\"" : "", commandId, hasCommandId ? "\"," : "",
    result, distanceField, bollardUnlocked ? "UNLOCKED" : "LOCKED");
  const bool published = length > 0 && static_cast<size_t>(length) < sizeof(payload) &&
    client.publish(ackTopic.c_str(), payload);

  if (!published) {
    Serial.print(F("[MQTT][ACK][ERROR] Publish failed. MQTT state: "));
    Serial.println(client.state());
  }
}

// =============================================
// 11. MQTT MESSAGE CALLBACK
// =============================================

bool isAckToken(const char* value) {
  const size_t length = strlen(value);
  if (length == 0 || length > 32) return false;
  for (size_t i = 0; i < length; i++) {
    const unsigned char c = value[i];
    if (!(isalnum(c) || c == '-' || c == '_')) return false;
  }
  return true;
}

void handleStatusRequest(const char* message) {
  constexpr char prefix[] = "STATUS|-1|";
  if (strncmp(message, prefix, sizeof(prefix) - 1) != 0) return;

  const char* commandId = message + sizeof(prefix) - 1;
  if (strlen(commandId) != 32 || strspn(commandId, "0123456789ABCDEF") != 32) return;

  const float distanceCm = readDistance();
  publishAcknowledgement(
    "STATUS",
    distanceCm >= 0 ? "SUCCESS" : "SENSOR_READING_UNAVAILABLE",
    -1,
    commandId,
    distanceCm
  );
}

void callback(
  char* topic,
  byte* payload,
  unsigned int length
) {
  const bool isStatusTopic = statusTopic.equals(topic);
  if (!isStatusTopic && !commandTopic.equals(topic)) return;

  // Copy the bounded wire message before publishing through the MQTT client's shared buffer.
  char buffer[192];
  if (length == 0 || length >= sizeof(buffer) || memchr(payload, '\0', length) != nullptr) {
    Serial.println(F("[MQTT][COMMAND][WARNING] Invalid message size or content."));
    return;
  }
  memcpy(buffer, payload, length);
  while (length > 0 && isspace(static_cast<unsigned char>(buffer[length - 1]))) length--;
  buffer[length] = '\0';
  char* message = buffer;
  while (isspace(static_cast<unsigned char>(*message))) message++;
  if (*message == '\0') return;

  if (isStatusTopic) {
    handleStatusRequest(message);
    return;
  }

  char* arguments = strchr(message, '|');
  if (arguments != nullptr) *arguments++ = '\0';
  const char* commandName = message;
  if (!isAckToken(commandName)) return;

  const char* result = "INVALID_COMMAND";
  int bookingId = 0;
  const char* commandId = "";
  if (strcmp(commandName, "BLE_START") != 0 && arguments != nullptr) {
    char* id = strchr(arguments, '|');
    if (id != nullptr) {
      *id++ = '\0';
      if (!isAckToken(id)) return;
      commandId = id;
    }
    char* end = nullptr;
    errno = 0;
    const long parsedBookingId = strtol(arguments, &end, 10);
    if (errno == ERANGE || end == arguments || *end != '\0' ||
        parsedBookingId < INT_MIN || parsedBookingId > INT_MAX) return;
    bookingId = static_cast<int>(parsedBookingId);
  }

  // Start short-lived BLE advertising only after the authenticated backend creates a challenge.
  if (strcmp(commandName, "BLE_START") == 0) {
    char* nonce = arguments == nullptr ? nullptr : strchr(arguments, '|');
    if (nonce != nullptr && nonce != arguments) {
      *nonce++ = '\0';
      Serial.println(F("[MQTT][BLE] Challenge activation message received; nonce omitted from log."));
      result = activateBleForChallenge(arguments, nonce) ? "SUCCESS" : "BLE_START_FAILED";
    } else {
      Serial.println(F("[MQTT][BLE][ERROR] Invalid BLE_START message format."));
    }
  }

  // UNLOCK COMMAND
  else if (strcmp(commandName, "UNLOCK") == 0) {
    bollardServo.write(90);

    bollardUnlocked = true;

    result = "SUCCESS";

    Serial.println(F("[SERVO] Commanded UNLOCK position (90 degrees)."));
  }

  // LOCK COMMAND
  else if (strcmp(commandName, "LOCK") == 0 || strcmp(commandName, "AUTO_LOCK") == 0) {
    const bool automaticLock = strcmp(commandName, "AUTO_LOCK") == 0;
    const float distanceCm = readDistance();
    Serial.print(automaticLock ? F("[AUTO-LOCK]") : F("[LOCK]"));
    Serial.print(F(" Live HC-SR04 reading: "));
    if (distanceCm < 0) {
      Serial.println(F("no valid echo."));
      result = "SENSOR_READING_UNAVAILABLE";
    } else {
      Serial.print(distanceCm, 2);
      Serial.println(F(" cm."));
      if (distanceCm <= 50.0) {
        result = "OBSTACLE_DETECTED";
        Serial.println(F("[LOCK][BLOCKED] Obstacle detected within 50 cm; servo was not moved."));
      } else {
        bollardServo.write(0);
        bollardUnlocked = false;
        result = "SUCCESS";
        Serial.println(F("[SERVO] Commanded LOCK position (0 degrees)."));
      }
    }
  }

  else {

    Serial.println(F("[MQTT][COMMAND][WARNING] Unknown command; returning INVALID_COMMAND ACK."));
  }

  // Publish acknowledgement
  publishAcknowledgement(commandName, result, bookingId, commandId);
}

// =============================================
// 12. WI-FI CONNECTION
// =============================================

void setupWiFi() {

  Serial.println(F("[Wi-Fi] Connecting."));

  WiFi.mode(WIFI_STA);
  WiFi.begin(ssid, password);

  const unsigned long startTime = millis();
  while (WiFi.status() != WL_CONNECTED && millis() - startTime < 30000) {

    delay(500);

  }

  Serial.println();
  if (WiFi.status() != WL_CONNECTED) {
    Serial.println(F("[Wi-Fi][ERROR] Connection timed out after 30 seconds."));
    Serial.print(F("[Wi-Fi] Status code: "));
    Serial.println(WiFi.status());
    return;
  }

  Serial.println(F("[Wi-Fi] Connected successfully."));

  Serial.print(F("[Wi-Fi] ESP32 IP address: "));

  Serial.println(WiFi.localIP());
  Serial.printf("[Wi-Fi] Signal: %d dBm\n", WiFi.RSSI());
}

// =============================================
// 13. MQTT CONNECTION
// =============================================

// Make one MQTT attempt per interval so HTTP/button diagnostics keep running offline.
void reconnectMQTT() {
  if (client.connected() || WiFi.status() != WL_CONNECTED) return;
  if (millis() - lastMqttReconnectAttempt < mqttReconnectInterval) return;

  lastMqttReconnectAttempt = millis();
  if (mqttUsername.length() == 0 || mqttPassword.length() == 0) {
    Serial.println(F("[MQTT][ERROR] Provision device username and password through USB Serial first."));
    return;
  }
  if (mqttServer[0] == '\0' || mqttPort <= 0) {
    Serial.println(F("[MQTT][ERROR] Configure the Ubuntu broker host and port in the sketch first."));
    return;
  }
  const String mqttClientId = "ParkJom-" + deviceId;
  Serial.print(F("[MQTT] Connecting to "));
  Serial.print(mqttServer);
  Serial.print(F(":"));
  Serial.print(mqttPort);
  Serial.print(F(" using client ID "));
  Serial.println(mqttClientId);

  if (!client.connect(mqttClientId.c_str(), mqttUsername.c_str(), mqttPassword.c_str())) {
    Serial.print(F("[MQTT][ERROR] Connection failed. State code: "));
    Serial.println(client.state());
    return;
  }

  Serial.println(F("[MQTT] Connected successfully."));
  Serial.print(F("[MQTT] Subscribing to: "));
  Serial.println(commandTopic);
  const bool subscribed = client.subscribe(commandTopic.c_str());
  Serial.println(subscribed
    ? F("[MQTT] Command subscription succeeded.")
    : F("[MQTT][ERROR] Command subscription failed."));
  if (!client.subscribe(statusTopic.c_str())) {
    Serial.println(F("[MQTT][ERROR] Status subscription failed."));
  }
}

// Debounce the active-low physical button and request exactly one OTP per press.
void checkOtpButton() {
  const uint8_t reading = digitalRead(OTP_BUTTON_PIN);
  if (reading != lastButtonReading) {
    lastButtonDebounceAt = millis();
    lastButtonReading = reading;
  }

  if (millis() - lastButtonDebounceAt >= buttonDebounceInterval && reading != stableButtonState) {
    stableButtonState = reading;
    if (stableButtonState == LOW) {
      requestBindingOtp();
    }
  }
}

// =============================================
// 14. SETUP
// =============================================

void setup() {

  Serial.begin(115200);
  delay(500);
  Serial.println(F("[BOOT] ParkJom bollard starting."));

  // Load persistent device identity
  loadDeviceUuid();
  if (deviceId.length() == 0) {
    Serial.println(F("[BOOT] Waiting for SET_UUID=<uuid>. Use 115200 baud with Newline enabled."));
    return;
  }
  loadDeviceHmacSecret();
  startBleChallengeService();

  // Initialize ultrasonic sensor
  pinMode(TRIG_PIN, OUTPUT);

  pinMode(ECHO_PIN, INPUT);

  digitalWrite(TRIG_PIN, LOW);
  pinMode(OTP_BUTTON_PIN, INPUT_PULLUP);

  // Initialize servo
  bollardServo.setPeriodHertz(50);

  bollardServo.attach(
    SERVO_PIN,
    500,
    2400
  );

  bollardServo.write(0);

  // Connect Wi-Fi
  setupWiFi();

  loadMqttCredentials();

  // Configure MQTT
  client.setServer(mqttServer, mqttPort);

  client.setCallback(callback);

  // Allow sufficient MQTT message size
  client.setBufferSize(512);

  Serial.print(F("[MQTT] Broker: "));
  Serial.print(mqttServer);
  Serial.print(F(":"));
  Serial.println(mqttPort);

  // Registration is idempotent; keep MQTT and sensor operation available if the API is offline.
  lastRegistrationAttempt = millis();
  registerDeviceWithBackend();

  Serial.println(F("[BOOT] Setup complete."));
}

// =============================================
// 15. MAIN LOOP
// =============================================

void loop() {
  // Serial provisioning remains available even before an identity has been configured.
  handleSecretProvisioning();
  if (deviceId.length() == 0) return;
  const unsigned long now = millis();
  if (bleChallengeActive && (long)(now - bleChallengeStopAt) >= 0) {
    Serial.println(F("[BLE] Challenge timed out after 30 seconds."));
    stopBleChallenge();
  }

  // Recover Wi-Fi without keeping the whole firmware blocked in a connection loop.
  if (WiFi.status() != WL_CONNECTED && now - lastWifiReconnectAttempt >= wifiReconnectInterval) {
    lastWifiReconnectAttempt = now;
    Serial.print(F("[Wi-Fi][WARNING] Disconnected; reconnecting. Status code: "));
    Serial.println(WiFi.status());
    WiFi.reconnect();
  }

  // Retry backend registration until it succeeds, then leave it unchanged.
  if (!deviceRegistered && !registrationBlocked && WiFi.status() == WL_CONNECTED
      && now - lastRegistrationAttempt >= registrationRetryDelay) {
    lastRegistrationAttempt = now;
    registerDeviceWithBackend();
  }

  // Process the physical pairing button and MQTT commands on every loop pass.
  checkOtpButton();
  if (client.connected()) {
    client.loop();
  } else {
    reconnectMQTT();
  }

  // Publish a sensor reading every five minutes and log the result.
  if (millis() - lastSensorUpdate >= sensorInterval) {

    lastSensorUpdate = millis();

    publishTelemetry();
  }
}
