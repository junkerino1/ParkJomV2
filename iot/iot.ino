
#include <WiFi.h>
#include <PubSubClient.h>
#include <ESP32Servo.h>
#include <Preferences.h>
#include <HTTPClient.h>
#include <NimBLEDevice.h>
#include <mbedtls/md.h>
#include <mbedtls/base64.h>

// =============================================
// 1. WI-FI CONFIGURATION
// =============================================

const char* ssid = "FBIVan@unifi";
const char* password = "Pota+0CHIP";

// =============================================
// 2. MQTT CONFIGURATION
// =============================================

// Self-hosted Ubuntu Mosquitto broker; keep aligned with Mqtt:Broker in ASP.NET.
const char* mqttServer = "dns-my.samsam123.name.my";
const int mqttPort = 48881;

// Set this to the computer's reachable LAN IP running ASP.NET Core.
// Use the backend's HTTP profile/port for this local prototype.
const char* apiBaseUrl = "https://parkjom-api-gbgcbycbcjghczgu.malaysiawest-01.azurewebsites.net";

// =============================================
// 3. DEVICE IDENTITY
// =============================================

// Manually assign a unique UUID to each ESP32.
// Existing UUID stored in NVS takes priority.

const char* configuredDeviceUuid =
  "7d9f2c41-8a63-4b72-9e15-6c2a0f84b731";

// Provision a unique random Base64 secret using Serial Monitor command SET_SECRET=<secret>.
// Keep the matching server value in IoT:DeviceSecrets:<device UUID>; never put it in Flutter.
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

// Dynamic MQTT topics
String commandTopic;
String ackTopic;
String telemetryTopic;

// =============================================
// 4. HARDWARE CONFIGURATION
// =============================================

const int SERVO_PIN = 18;
const int TRIG_PIN = 5;
const int ECHO_PIN = 19;
const int OTP_BUTTON_PIN = 23;  // Wire a momentary button between GPIO 23 and GND.

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

const unsigned long sensorInterval = 300000UL;
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
int lastButtonReading = HIGH;
int stableButtonState = HIGH;
unsigned long lastButtonDebounceAt = 0;

// =============================================
// 7. LOAD DEVICE UUID FROM NVS
// =============================================

void loadDeviceUuid() {

  preferences.begin("parkjom", false);

  // Retrieve previously stored UUID
  deviceId = preferences.getString("deviceUuid", "");

  // First-time initialization
  if (deviceId.length() == 0) {

    deviceId = configuredDeviceUuid;

    preferences.putString("deviceUuid", deviceId);

    Serial.println("New device UUID saved to NVS.");

  } else {

    Serial.println("Existing device UUID loaded from NVS.");
  }

  preferences.end();

  // Generate MQTT topics dynamically
  commandTopic = "parkjom/devices/" + deviceId + "/commands";

  ackTopic = "parkjom/devices/" + deviceId + "/ack";

  telemetryTopic = "parkjom/devices/" + deviceId + "/telemetry";

  // Display device identity
  Serial.print("[DEVICE] UUID: ");
  Serial.println(deviceId);
}

void loadMqttCredentials() {
  preferences.begin("parkjom", true);
  mqttUsername = preferences.getString("mqttUser", "");
  mqttPassword = preferences.getString("mqttPass", "");
  preferences.end();
  Serial.println(mqttUsername.length() > 0 && mqttPassword.length() > 0
    ? "[MQTT] Device credentials loaded from NVS."
    : "[MQTT][WARNING] Device credentials are not provisioned.");
}

// The phone writes: challengeId|base64Nonce|deviceUuid. The HMAC input matches
// the backend exactly: base64Nonce + lowercaseDeviceUuid + challengeId.
class BleChallengeCallbacks : public NimBLECharacteristicCallbacks {
  void onWrite(NimBLECharacteristic* characteristic, NimBLEConnInfo& connInfo) override {
    (void)connInfo;
    String request = characteristic->getValue().c_str();
    Serial.println("[BLE][CHALLENGE] Challenge request received.");

    const int firstSeparator = request.indexOf('|');
    const int secondSeparator = request.indexOf('|', firstSeparator + 1);
    if (firstSeparator <= 0 || secondSeparator <= firstSeparator + 1) {
      Serial.println("[BLE][ERROR] Invalid challenge format; expected challengeId|nonce|deviceUuid.");
      return;
    }

    const String challengeId = request.substring(0, firstSeparator);
    const String nonce = request.substring(firstSeparator + 1, secondSeparator);
    const String requestedDevice = request.substring(secondSeparator + 1);
    if (!bleChallengeActive) {
      Serial.println("[BLE][ERROR] No active backend challenge; BLE proof was not generated.");
      return;
    }
    if (!requestedDevice.equalsIgnoreCase(deviceId) || nonce.length() == 0 || challengeId.indexOf('|') >= 0) {
      Serial.println("[BLE][ERROR] Challenge device UUID or fields did not validate.");
      return;
    }
    if (challengeId != activeBleChallengeId || nonce != activeBleNonce) {
      Serial.println("[BLE][ERROR] BLE challenge does not match the active backend challenge.");
      return;
    }
    if (deviceHmacSecret.length() < 32) {
      Serial.println("[BLE][ERROR] Device secret is not provisioned. Send SET_SECRET=<base64-secret> over USB Serial.");
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
      Serial.println("[BLE][ERROR] HMAC-SHA256 calculation failed.");
      return;
    }

    unsigned char encoded[48];
    size_t encodedLength = 0;
    const int encodeResult = mbedtls_base64_encode(encoded, sizeof(encoded), &encodedLength, digest, sizeof(digest));
    if (encodeResult != 0) {
      Serial.println("[BLE][ERROR] Could not encode HMAC response.");
      return;
    }

    String proof;
    proof.reserve(encodedLength);
    for (size_t i = 0; i < encodedLength; i++) proof += static_cast<char>(encoded[i]);
    bleProofCharacteristic->setValue(proof.c_str());
    bleProofCharacteristic->notify();
    Serial.println("[BLE][CHALLENGE] HMAC proof generated and notified (proof omitted from logs).");
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
  Serial.print("[BLE] Device name: ");
  Serial.println(bleName);
  Serial.print("[BLE] GATT challenge service UUID: ");
  Serial.println(BLE_SERVICE_UUID);
  Serial.println("[BLE] GATT service initialized; advertising stays off until backend creates a challenge.");
  Serial.println("[BLE] Device name is for discovery only; authentication uses the HMAC proof.");
}

bool activateBleForChallenge(const String& challengeId, const String& nonce) {
  if (bleAdvertising == nullptr || challengeId.length() == 0 || nonce.length() == 0) {
    Serial.println("[BLE][ERROR] Cannot activate BLE: service or challenge data is missing.");
    return false;
  }

  bleChallengeActive = false;
  if (bleAdvertising->isAdvertising()) bleAdvertising->stop();
  activeBleChallengeId = challengeId;
  activeBleNonce = nonce;
  bleChallengeStopAt = millis() + bleChallengeWindowMs;

  if (!bleAdvertising->start()) {
    Serial.println("[BLE][ERROR] Advertising failed to start for the challenge.");
    return false;
  }

  bleChallengeActive = true;
  Serial.print("[BLE] Advertising started for challenge ");
  Serial.println(challengeId);
  return true;
}

void stopBleChallenge() {
  bleChallengeActive = false;
  if (bleAdvertising != nullptr && bleAdvertising->isAdvertising()) {
    bleAdvertising->stop();
  }
  Serial.println("[BLE] Challenge window ended; advertising stopped.");
}

void handleSecretProvisioning() {
  if (!Serial.available()) return;
  String line = Serial.readStringUntil('\n');
  line.trim();
  if (line.startsWith("SET_MQTT_USER=")) {
    const String value = line.substring(String("SET_MQTT_USER=").length());
    if (value.length() == 0 || value.length() > 128) {
      Serial.println("[MQTT][PROVISION][ERROR] Username length is invalid.");
      return;
    }
    preferences.begin("parkjom", false);
    const size_t saved = preferences.putString("mqttUser", value);
    preferences.end();
    if (saved > 0) {
      loadMqttCredentials();
      Serial.println("[MQTT][PROVISION] Username saved to NVS; value was not logged.");
    } else {
      Serial.println("[MQTT][PROVISION][ERROR] Username save failed.");
    }
    return;
  }
  if (line.startsWith("SET_MQTT_PASSWORD=")) {
    const String value = line.substring(String("SET_MQTT_PASSWORD=").length());
    if (value.length() == 0 || value.length() > 128) {
      Serial.println("[MQTT][PROVISION][ERROR] Password length is invalid.");
      return;
    }
    preferences.begin("parkjom", false);
    const size_t saved = preferences.putString("mqttPass", value);
    preferences.end();
    if (saved > 0) {
      loadMqttCredentials();
      Serial.println("[MQTT][PROVISION] Password saved to NVS; value was not logged.");
    } else {
      Serial.println("[MQTT][PROVISION][ERROR] Password save failed.");
    }
    return;
  }
  if (!line.startsWith("SET_SECRET=")) return;

  String secret = line.substring(String("SET_SECRET=").length());
  if (secret.length() < 32 || secret.length() > 128) {
    Serial.println("[BLE][PROVISION][ERROR] Secret length must be 32 to 128 characters; value was not saved.");
    return;
  }
  preferences.begin("parkjom", false);
  const size_t saved = preferences.putString(BLE_SECRET_PREF_KEY, secret);
  preferences.end();
  secret = "";
  if (saved > 0) {
    loadDeviceHmacSecret();
    Serial.println("[BLE][PROVISION] Device secret saved to NVS. Secret value was not logged.");
  } else {
    Serial.println("[BLE][PROVISION][ERROR] NVS save failed.");
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
    Serial.println("[HTTP][REGISTER][STOPPED] Fix the permanent HTTP/configuration error, then restart the ESP32.");
    return;
  }
  if (WiFi.status() != WL_CONNECTED) {
    Serial.println("[HTTP][REGISTER] Skipped: Wi-Fi is not connected.");
    return;
  }

  const String url = String(apiBaseUrl) + "/api/iot/register";
  const String requestBody = String("{\"deviceUuid\":\"") + deviceId + "\"}";
  HTTPClient http;
  http.setTimeout(8000);

  Serial.println("[HTTP][REGISTER] Registering device.");

  if (!http.begin(url)) {
    Serial.println("[HTTP][REGISTER][ERROR] Could not initialize HTTP client.");
    return;
  }

  http.addHeader("Content-Type", "application/json");
  const int statusCode = http.POST(requestBody);
  const String responseBody = statusCode > 0 ? http.getString() : "";
  Serial.print("[HTTP][REGISTER] Status: ");
  Serial.println(statusCode);
  if (statusCode <= 0) {
    Serial.print("[HTTP][REGISTER][ERROR] ");
    Serial.println(http.errorToString(statusCode));
  }

  deviceRegistered = statusCode >= 200 && statusCode < 300 && jsonSuccess(responseBody);
  if (deviceRegistered) {
    registrationRetryDelay = registrationRetryInitialDelay;
    Serial.println("[HTTP][REGISTER] Registration confirmed (new or already registered). No more registration retries needed.");
  } else if ((statusCode >= 300 && statusCode < 400)
      || (statusCode >= 400 && statusCode < 500 && statusCode != 429)
      || (statusCode >= 200 && statusCode < 300)) {
    // Redirects, client errors, and unexpected success bodies will not improve by repeated posting.
    registrationBlocked = true;
    Serial.print("[HTTP][REGISTER][STOPPED] Response: ");
    Serial.println(responseBody);
  } else {
    Serial.print("[HTTP][REGISTER][WARNING] Temporary failure. Next retry in ");
    Serial.print(registrationRetryDelay / 1000);
    Serial.println(" seconds.");
    if (registrationRetryDelay < registrationRetryMaxDelay) {
      registrationRetryDelay = min(registrationRetryDelay * 2, registrationRetryMaxDelay);
    }
  }
  http.end();
}

// Request a short-lived binding code when the physical button is pressed.
void requestBindingOtp() {
  Serial.println();
  Serial.println("[OTP] Physical button press detected.");
  if (WiFi.status() != WL_CONNECTED) {
    Serial.println("[OTP][ERROR] Cannot request OTP: Wi-Fi is disconnected.");
    return;
  }
  if (!deviceRegistered) {
    Serial.println("[OTP][ERROR] Device is not registered yet; wait for registration retry.");
    return;
  }

  const String url = String(apiBaseUrl) + "/api/iot/gen-otp";
  const String requestBody = String("{\"deviceUuid\":\"") + deviceId + "\"}";
  HTTPClient http;
  http.setTimeout(8000);

  Serial.println("[OTP] Requesting binding code.");

  if (!http.begin(url)) {
    Serial.println("[OTP][ERROR] Could not initialize HTTP client.");
    return;
  }

  http.addHeader("Content-Type", "application/json");
  const int statusCode = http.POST(requestBody);
  const String responseBody = statusCode > 0 ? http.getString() : "";
  Serial.print("[OTP] HTTP status: ");
  Serial.println(statusCode);

  if (statusCode <= 0) {
    Serial.print("[OTP][ERROR] ");
    Serial.println(http.errorToString(statusCode));
  }

  const String otp = jsonStringField(responseBody, "otp");
  const String expiresAtUtc = jsonStringField(responseBody, "expiresAtUtc");
  if (statusCode == 200 && jsonSuccess(responseBody) && otp.length() == 6) {
    Serial.println("[OTP] ========================================");
    Serial.print("[OTP] BINDING CODE: ");
    Serial.println(otp);
    Serial.print("[OTP] Expires at (UTC): ");
    Serial.println(expiresAtUtc);
    Serial.println("[OTP] Enter this code in ParkJom to bind this device.");
    Serial.println("[OTP] Keep this code private; it is valid for five minutes.");
    Serial.println("[OTP] ========================================");
  } else {
    Serial.print("[OTP][ERROR] Invalid OTP response: ");
    Serial.println(responseBody);
  }

  http.end();
}

// =============================================
// 8. ULTRASONIC SENSOR
// =============================================

float readDistance() {

  // Generate trigger pulse
  digitalWrite(TRIG_PIN, LOW);
  delayMicroseconds(2);

  digitalWrite(TRIG_PIN, HIGH);
  delayMicroseconds(10);

  digitalWrite(TRIG_PIN, LOW);

  // Read echo duration
  unsigned long duration =
    pulseIn(ECHO_PIN, HIGH, 30000);

  // No echo received
  if (duration == 0) {
    return -1.0;
  }

  // Convert duration into centimetres
  float distance = duration * 0.0343 / 2.0;

  return distance;
}

// =============================================
// 9. MQTT TELEMETRY
// =============================================

void publishTelemetry() {

  float distance = readDistance();

  String position =
    bollardUnlocked ? "UNLOCKED" : "LOCKED";

  String payload = "{";

  payload += "\"deviceId\":\"";
  payload += deviceId;
  payload += "\",";

  payload += "\"distanceCm\":";

  if (distance < 0) {
    payload += "null";
  } else {
    payload += String(distance, 2);
  }

  payload += ",\"bollardPosition\":\"";
  payload += position;
  payload += "\"";

  payload += "}";

  Serial.print("[SENSOR] Distance: ");
  if (distance < 0) {
    Serial.println("no echo (timeout)");
  } else {
    Serial.print(distance, 2);
    Serial.println(" cm");
  }
  if (!client.connected()) {
    Serial.println("[MQTT][TELEMETRY][WARNING] Not connected; telemetry was not published.");
    return;
  }

  // Publish telemetry
  bool published =
    client.publish(telemetryTopic.c_str(), payload.c_str());

  if (!published) {
    Serial.print("[MQTT][TELEMETRY][ERROR] Publish failed. MQTT state: ");
    Serial.println(client.state());
  } else {
    Serial.println("[MQTT][TELEMETRY] Published.");
  }
}

// =============================================
// 10. MQTT ACKNOWLEDGEMENT
// =============================================

void publishAcknowledgement(
  String command,
  String result,
  int bookingId = 0
) {

  String position =
    bollardUnlocked ? "UNLOCKED" : "LOCKED";

  String payload = "{";

  payload += "\"deviceId\":\"";
  payload += deviceId;
  payload += "\",";

  payload += "\"command\":\"";
  payload += command;
  payload += "\",";

  if (bookingId > 0) {
    payload += "\"bookingId\":";
    payload += String(bookingId);
    payload += ",";
  }

  payload += "\"result\":\"";
  payload += result;
  payload += "\",";

  payload += "\"bollardPosition\":\"";
  payload += position;
  payload += "\"";

  payload += "}";

  bool published =
    client.publish(ackTopic.c_str(), payload.c_str());

  if (!published) {
    Serial.print("[MQTT][ACK][ERROR] Publish failed. MQTT state: ");
    Serial.println(client.state());
  }
}

// =============================================
// 11. MQTT MESSAGE CALLBACK
// =============================================

void callback(
  char* topic,
  byte* payload,
  unsigned int length
) {

  String message = "";

  // Convert MQTT payload into String
  for (unsigned int i = 0; i < length; i++) {
    message += (char)payload[i];
  }

  message.trim();

  String result = "INVALID_COMMAND";
  const int commandSeparator = message.indexOf('|');
  String commandName = commandSeparator >= 0 ? message.substring(0, commandSeparator) : message;
  String acknowledgementCommand = commandName;
  int bookingId = 0;
  if (commandSeparator >= 0 && !message.startsWith("BLE_START|")) {
    bookingId = message.substring(commandSeparator + 1).toInt();
  }

  // Start short-lived BLE advertising only after the authenticated backend creates a challenge.
  if (message.startsWith("BLE_START|")) {
    const int firstSeparator = message.indexOf('|');
    const int secondSeparator = message.indexOf('|', firstSeparator + 1);
    if (secondSeparator > firstSeparator + 1) {
      const String challengeId = message.substring(firstSeparator + 1, secondSeparator);
      const String nonce = message.substring(secondSeparator + 1);
      Serial.println("[MQTT][BLE] Challenge activation message received; nonce omitted from log.");
      result = activateBleForChallenge(challengeId, nonce) ? "SUCCESS" : "BLE_START_FAILED";
      acknowledgementCommand = "BLE_START";
    } else {
      Serial.println("[MQTT][BLE][ERROR] Invalid BLE_START message format.");
    }
  }

  // UNLOCK COMMAND
  else if (commandName == "UNLOCK") {
    bollardServo.write(90);

    bollardUnlocked = true;

    result = "SUCCESS";

    Serial.println("[SERVO] Commanded UNLOCK position (90 degrees).");
  }

  // LOCK COMMAND
  else if (commandName == "LOCK" || commandName == "AUTO_LOCK") {
    const bool automaticLock = commandName == "AUTO_LOCK";
    const float distanceCm = readDistance();
    Serial.print(automaticLock ? "[AUTO-LOCK]" : "[LOCK]");
    Serial.print(" Live HC-SR04 reading: ");
    if (distanceCm < 0) {
      Serial.println("no echo.");
      result = "SENSOR_READING_UNAVAILABLE";
    } else {
      Serial.print(distanceCm, 2);
      Serial.println(" cm.");
      if (distanceCm <= 50.0) {
        result = "OBSTACLE_DETECTED";
        Serial.println("[LOCK][BLOCKED] Obstacle detected within 50 cm; servo was not moved.");
      } else {
        bollardServo.write(0);
        bollardUnlocked = false;
        result = "SUCCESS";
        Serial.println("[SERVO] Commanded LOCK position (0 degrees).");
      }
    }
  }

  else {

    Serial.println("[MQTT][COMMAND][WARNING] Unknown command; returning INVALID_COMMAND ACK.");
  }

  // Publish acknowledgement
  publishAcknowledgement(acknowledgementCommand, result, bookingId);
}

// =============================================
// 12. WI-FI CONNECTION
// =============================================

void setupWiFi() {

  Serial.println("[Wi-Fi] Connecting.");

  WiFi.mode(WIFI_STA);
  WiFi.begin(ssid, password);

  const unsigned long startTime = millis();
  while (WiFi.status() != WL_CONNECTED && millis() - startTime < 30000) {

    delay(500);

  }

  Serial.println();
  if (WiFi.status() != WL_CONNECTED) {
    Serial.println("[Wi-Fi][ERROR] Connection timed out after 30 seconds.");
    Serial.print("[Wi-Fi] Status code: ");
    Serial.println(WiFi.status());
    return;
  }

  Serial.println("[Wi-Fi] Connected successfully.");

  Serial.print("[Wi-Fi] ESP32 IP address: ");

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
    Serial.println("[MQTT][ERROR] Provision device username and password through USB Serial first.");
    return;
  }
  if (String(mqttServer).length() == 0 || mqttPort <= 0) {
    Serial.println("[MQTT][ERROR] Configure the Ubuntu broker host and port in the sketch first.");
    return;
  }
  const String mqttClientId = "ParkJom-" + deviceId;
  Serial.print("[MQTT] Connecting to ");
  Serial.print(mqttServer);
  Serial.print(":");
  Serial.print(mqttPort);
  Serial.print(" using client ID ");
  Serial.println(mqttClientId);

  if (!client.connect(mqttClientId.c_str(), mqttUsername.c_str(), mqttPassword.c_str())) {
    Serial.print("[MQTT][ERROR] Connection failed. State code: ");
    Serial.println(client.state());
    return;
  }

  Serial.println("[MQTT] Connected successfully.");
  Serial.print("[MQTT] Subscribing to: ");
  Serial.println(commandTopic);
  const bool subscribed = client.subscribe(commandTopic.c_str());
  Serial.println(subscribed
    ? "[MQTT] Command subscription succeeded."
    : "[MQTT][ERROR] Command subscription failed.");
}

// Debounce the active-low physical button and request exactly one OTP per press.
void checkOtpButton() {
  const int reading = digitalRead(OTP_BUTTON_PIN);
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
  Serial.println("[BOOT] ParkJom bollard starting.");

  // Load persistent device identity
  loadDeviceUuid();
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

  Serial.print("[MQTT] Broker: ");
  Serial.print(mqttServer);
  Serial.print(":");
  Serial.println(mqttPort);

  // Registration is idempotent; keep MQTT and sensor operation available if the API is offline.
  lastRegistrationAttempt = millis();
  registerDeviceWithBackend();

  Serial.println("[BOOT] Setup complete.");
}

// =============================================
// 15. MAIN LOOP
// =============================================

void loop() {

  const unsigned long now = millis();
  if (bleChallengeActive && (long)(now - bleChallengeStopAt) >= 0) {
    Serial.println("[BLE] Challenge timed out after 30 seconds.");
    stopBleChallenge();
  }

  // Recover Wi-Fi without keeping the whole firmware blocked in a connection loop.
  if (WiFi.status() != WL_CONNECTED && now - lastWifiReconnectAttempt >= wifiReconnectInterval) {
    lastWifiReconnectAttempt = now;
    Serial.print("[Wi-Fi][WARNING] Disconnected; reconnecting. Status code: ");
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
  handleSecretProvisioning();
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
