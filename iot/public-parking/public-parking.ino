#include <WiFi.h>
#include <PubSubClient.h>
#include <time.h>
#include <sys/time.h>

// Configure these locally. IOT_DEVICE_UUID must match one lot's iotDeviceUuid.
const char* WIFI_SSID = "B100M";
const char* WIFI_PASSWORD = "12345678";
const char* MQTT_HOST = "dns-my.samsam123.name.my";
const uint16_t MQTT_PORT = 48881;
const char* MQTT_USERNAME = "";
const char* MQTT_PASSWORD = "";
const char* IOT_DEVICE_UUID = "cce1259c-19bc-4904-a878-4365b41cb50b";

// Existing ESP32-WROOM-32 wiring, two ultrasonic sensors.
const byte TRIG_PINS[2] = {13, 27};
const byte ECHO_PINS[2] = {32, 34};
const char* SENSOR_IDS[2] = {"S01", "S02"};
const unsigned long SCAN_INTERVAL_MS = 5000;
const unsigned long RECONNECT_INTERVAL_MS = 5000;
const float OCCUPIED_DISTANCE_CM = 50.0f;
unsigned long lastScan = 0;
unsigned long lastReconnect = 0;
bool clockConfigured = false;
WiFiClient network;
PubSubClient mqtt(network);
char occupancyTopic[96];

float readDistance(byte sensor) {
  digitalWrite(TRIG_PINS[sensor], LOW);
  delayMicroseconds(2);
  digitalWrite(TRIG_PINS[sensor], HIGH);
  delayMicroseconds(10);
  digitalWrite(TRIG_PINS[sensor], LOW);
  const unsigned long duration = pulseIn(ECHO_PINS[sensor], HIGH, 30000UL);
  return duration == 0 ? -1.0f : duration * 0.0343f / 2.0f;
}

void publishCount(int occupiedCount, bool reliable) {
  if (!mqtt.connected()) return;
  struct timeval clockNow;
  gettimeofday(&clockNow, nullptr);
  if (clockNow.tv_sec < 1700000000) {
    Serial.println("Waiting for NTP: no occupancy is published with an unknown timestamp.");
    return;
  }
  struct tm utc;
  const time_t seconds = clockNow.tv_sec;
  gmtime_r(&seconds, &utc);
  char observedAt[32];
  snprintf(observedAt, sizeof(observedAt), "%04d-%02d-%02dT%02d:%02d:%02d.%03ldZ",
      utc.tm_year + 1900, utc.tm_mon + 1, utc.tm_mday, utc.tm_hour, utc.tm_min, utc.tm_sec,
      static_cast<long>(clockNow.tv_usec / 1000));
  char countJson[12] = "null";
  if (reliable) snprintf(countJson, sizeof(countJson), "%d", occupiedCount);
  char payload[300];
  const int length = snprintf(payload, sizeof(payload),
      "{\"iotDeviceUuid\":\"%s\",\"occupiedCount\":%s,\"observedAt\":\"%s\"}",
      IOT_DEVICE_UUID, countJson, observedAt);
  if (length <= 0 || static_cast<size_t>(length) >= sizeof(payload)) {
    Serial.println("Occupancy payload too large; not published.");
    return;
  }
  // Send one whole-lot snapshot, not +/- deltas. PubSubClient publishes at QoS 0.
  // Periodic non-retained snapshots resync after MQTT reconnects or missed messages.
  if (!mqtt.publish(occupancyTopic, payload, false)) Serial.println("Occupancy publish failed.");
}

void setup() {
  Serial.begin(115200);
  for (byte sensor = 0; sensor < 2; sensor++) {
    pinMode(TRIG_PINS[sensor], OUTPUT);
    pinMode(ECHO_PINS[sensor], INPUT);
    digitalWrite(TRIG_PINS[sensor], LOW);
  }
  snprintf(occupancyTopic, sizeof(occupancyTopic), "parkjom/parking/%s/occupancy", IOT_DEVICE_UUID);
  mqtt.setServer(MQTT_HOST, MQTT_PORT);
  mqtt.setBufferSize(512);
  mqtt.setSocketTimeout(3);
  WiFi.mode(WIFI_STA);
  WiFi.setAutoReconnect(true);
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
  lastScan = millis() - SCAN_INTERVAL_MS;
  lastReconnect = millis() - RECONNECT_INTERVAL_MS;
  Serial.println("Two-sensor public parking publisher started.");
}

void loop() {
  const unsigned long now = millis();
  if (WiFi.status() == WL_CONNECTED) {
    if (!clockConfigured) {
      configTime(0, 0, "pool.ntp.org", "time.google.com");
      clockConfigured = true;
    }
    if (!mqtt.connected() && now - lastReconnect >= RECONNECT_INTERVAL_MS) {
      lastReconnect = now;
      const bool connected = strlen(MQTT_USERNAME) > 0
          ? mqtt.connect(IOT_DEVICE_UUID, MQTT_USERNAME, MQTT_PASSWORD) : mqtt.connect(IOT_DEVICE_UUID);
      if (connected) {
        Serial.println("MQTT connected.");
        lastScan = millis() - SCAN_INTERVAL_MS;
      }
    }
    mqtt.loop();
  }
  if (millis() - lastScan < SCAN_INTERVAL_MS) return;
  lastScan = millis();
  int occupiedCount = 0;
  bool reliable = true;
  for (byte sensor = 0; sensor < 2; sensor++) {
    const float distanceCm = readDistance(sensor);
    Serial.print(SENSOR_IDS[sensor]); Serial.print(": ");
    if (distanceCm < 0) Serial.println("NO ECHO (unknown occupancy)");
    else { Serial.print(distanceCm, 1); Serial.println(distanceCm <= OCCUPIED_DISTANCE_CM ? " cm: occupied" : " cm: free"); }
    if (distanceCm < 0) reliable = false;
    else if (distanceCm <= OCCUPIED_DISTANCE_CM) occupiedCount++;
    delay(60); // Preserve separation between the two ultrasonic pulses.
  }
  publishCount(occupiedCount, reliable);
}
