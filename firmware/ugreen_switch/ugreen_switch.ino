// UGREEN CM430 DP switch - remote button press via NodeMCU (option A, open-drain)
// Wiring: switch S1 (3.3 V button leg) -> D1, switch GND -> GND, switch 5V -> VIN
//
// Serial (115200): t = press, 1 / 2 = select input, ? = status,
//                  w SSID|PASSWORD = save Wi-Fi credentials (kept in flash)
// HTTP:            /press  /1  /2  /state

#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>

const int  BTN = D1;            // button signal leg
const int  SENSE = D2;          // optional: LED "1" sense
const bool HAVE_SENSE = false;  // set true once SENSE is wired
const int  LED1_ON_LEVEL = LOW; // level on SENSE when input 1 is active
const int  PRESS_MS = 150;

ESP8266WebServer server(80);
bool serverStarted = false;
String line;

void press(int ms = PRESS_MS) {
  digitalWrite(BTN, LOW);
  pinMode(BTN, OUTPUT);
  delay(ms);
  pinMode(BTN, INPUT);          // release = high impedance, never drive HIGH
}

int active() {                  // 1, 2, or 0 if unknown
  if (!HAVE_SENSE) return 0;
  return digitalRead(SENSE) == LED1_ON_LEVEL ? 1 : 2;
}

void select(int want) {
  if (!HAVE_SENSE) { press(); return; }
  for (int i = 0; i < 3 && active() != want; i++) { press(); delay(400); }
}

String status() {
  String s = "active=" + String(active());
  s += WiFi.status() == WL_CONNECTED ? " ip=" + WiFi.localIP().toString() : String(" wifi=off");
  return s + "\n";
}

void handleLine(String l) {
  l.trim();
  if (l.startsWith("w ")) {
    int bar = l.indexOf('|');
    if (bar < 0) { Serial.println("usage: w SSID|PASSWORD"); return; }
    String ssid = l.substring(2, bar), pass = l.substring(bar + 1);
    WiFi.persistent(true);
    WiFi.begin(ssid.c_str(), pass.c_str());
    Serial.println("connecting to " + ssid + " ...");
    return;
  }
  if (l == "t") press();
  else if (l == "1") select(1);
  else if (l == "2") select(2);
  else if (l != "?") { Serial.println("commands: t 1 2 ? | w SSID|PASSWORD"); return; }
  Serial.print(status());
}

void setup() {
  pinMode(BTN, INPUT);
  pinMode(SENSE, INPUT);
  Serial.begin(115200);
  WiFi.mode(WIFI_STA);
  WiFi.setAutoReconnect(true);
  WiFi.begin();                 // uses credentials saved in flash, if any

  server.on("/press", [] { press();   server.send(200, "text/plain", status()); });
  server.on("/1",     [] { select(1); server.send(200, "text/plain", status()); });
  server.on("/2",     [] { select(2); server.send(200, "text/plain", status()); });
  server.on("/state", [] {            server.send(200, "text/plain", status()); });
  Serial.println("\nugreen_switch ready");
}

void loop() {
  if (WiFi.status() == WL_CONNECTED && !serverStarted) {
    server.begin();
    serverStarted = true;
    Serial.println("http://" + WiFi.localIP().toString() + "/");
  }
  if (serverStarted) server.handleClient();

  while (Serial.available()) {
    char c = Serial.read();
    if (c == '\n' || c == '\r') { if (line.length()) handleLine(line); line = ""; }
    else if (line.length() < 128) line += c;
  }
}
