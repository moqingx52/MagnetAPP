/*
  MagnetAPP UNO slave firmware

  Serial settings:
    115200 baud, 8 data bits, no parity, 1 stop bit, newline-terminated ASCII commands.

  Pin map for Arduino UNO R3:
    M1_STEP = D2
    M1_DIR  = D4
    M1_EN   = D7

    M2_STEP = D8
    M2_DIR  = D12
    M2_EN   = A0   // used as a digital output

    PWM_OUT = D3   // D11 is also acceptable if the wiring changes.

  D3/D11 are preferred for UV PWM because D9/D10 use Timer1 PWM on UNO.
  Leaving D9/D10 free avoids conflicts if Timer1 is later used for steadier STEP pulses.

  Command protocol:
    PING
      -> OK PONG

    STATUS
      -> OK STATUS UV=<0-255> M1=<0|1> M2=<0|1>

    UV <0-255>
      Set raw PWM value on PWM_OUT.

    UVP <0-100>
      Set UV brightness by percent; firmware maps it to 0-255 PWM.

    UVOFF
      Set UV PWM to 0.

    ENABLE <motor> <0|1>
      Enable or disable motor driver. motor: 1 or 2.

    MOTOR <motor> <direction> <steps> <pulse_us> [keep_enabled]
      Blocking move command.
      motor: 1 or 2
      direction: 0 or 1
      steps: non-negative integer
      pulse_us: STEP high time and low time in microseconds
      keep_enabled: optional 0 or 1, default 1

    STOP
      Set UV PWM to 0 and disable both motors.

  Responses:
    OK ...
    ERR ...
*/

#include <Arduino.h>
#include <stdlib.h>
#include <string.h>

const uint32_t SERIAL_BAUD = 115200;

const uint8_t M1_STEP_PIN = 2;
const uint8_t M1_DIR_PIN = 4;
const uint8_t M1_ENABLE_PIN = 7;

const uint8_t M2_STEP_PIN = 8;
const uint8_t M2_DIR_PIN = 12;
const uint8_t M2_ENABLE_PIN = A0;

const uint8_t UV_PWM_PIN = 3;

const bool MOTOR_ENABLE_ACTIVE_LOW = true;
const unsigned int DEFAULT_PULSE_US = 800;
const uint16_t MAX_COMMAND_LENGTH = 96;

char commandBuffer[MAX_COMMAND_LENGTH];
uint8_t commandLength = 0;

uint8_t currentUvPwm = 0;
bool motor1Enabled = false;
bool motor2Enabled = false;

void setup() {
  pinMode(M1_STEP_PIN, OUTPUT);
  pinMode(M1_DIR_PIN, OUTPUT);
  pinMode(M1_ENABLE_PIN, OUTPUT);

  pinMode(M2_STEP_PIN, OUTPUT);
  pinMode(M2_DIR_PIN, OUTPUT);
  pinMode(M2_ENABLE_PIN, OUTPUT);

  pinMode(UV_PWM_PIN, OUTPUT);

  digitalWrite(M1_STEP_PIN, LOW);
  digitalWrite(M2_STEP_PIN, LOW);
  setMotorEnabled(1, false);
  setMotorEnabled(2, false);
  setUvPwm(0);

  Serial.begin(SERIAL_BAUD);
  Serial.println(F("OK READY MagnetAPP UNOslave"));
}

void loop() {
  while (Serial.available() > 0) {
    char c = (char)Serial.read();
    if (c == '\r') {
      continue;
    }

    if (c == '\n') {
      commandBuffer[commandLength] = '\0';
      if (commandLength > 0) {
        handleCommand(commandBuffer);
      }
      commandLength = 0;
      continue;
    }

    if (commandLength < MAX_COMMAND_LENGTH - 1) {
      commandBuffer[commandLength++] = c;
    } else {
      commandLength = 0;
      Serial.println(F("ERR COMMAND_TOO_LONG"));
    }
  }
}

void handleCommand(char* command) {
  char* token = strtok(command, " ");
  if (token == NULL) {
    return;
  }

  if (equalsCommand(token, "PING")) {
    Serial.println(F("OK PONG"));
    return;
  }

  if (equalsCommand(token, "STATUS")) {
    printStatus();
    return;
  }

  if (equalsCommand(token, "UV")) {
    handleUvRaw();
    return;
  }

  if (equalsCommand(token, "UVP")) {
    handleUvPercent();
    return;
  }

  if (equalsCommand(token, "UVOFF")) {
    setUvPwm(0);
    Serial.println(F("OK UV 0"));
    return;
  }

  if (equalsCommand(token, "ENABLE")) {
    handleEnable();
    return;
  }

  if (equalsCommand(token, "MOTOR")) {
    handleMotor();
    return;
  }

  if (equalsCommand(token, "STOP")) {
    setUvPwm(0);
    setMotorEnabled(1, false);
    setMotorEnabled(2, false);
    Serial.println(F("OK STOPPED"));
    return;
  }

  Serial.println(F("ERR UNKNOWN_COMMAND"));
}

void handleUvRaw() {
  long pwmValue;
  if (!readLongArg(pwmValue)) {
    Serial.println(F("ERR UV_VALUE_REQUIRED"));
    return;
  }

  setUvPwm(constrain(pwmValue, 0, 255));
  Serial.print(F("OK UV "));
  Serial.println(currentUvPwm);
}

void handleUvPercent() {
  long percent;
  if (!readLongArg(percent)) {
    Serial.println(F("ERR UV_PERCENT_REQUIRED"));
    return;
  }

  percent = constrain(percent, 0, 100);
  setUvPwm((uint8_t)((percent * 255L + 50L) / 100L));
  Serial.print(F("OK UVP "));
  Serial.print(percent);
  Serial.print(F(" PWM "));
  Serial.println(currentUvPwm);
}

void handleEnable() {
  long motor;
  long enabled;
  if (!readLongArg(motor) || !readLongArg(enabled)) {
    Serial.println(F("ERR ENABLE_ARGS motor enabled"));
    return;
  }

  if (!isValidMotor(motor)) {
    Serial.println(F("ERR INVALID_MOTOR"));
    return;
  }

  setMotorEnabled((uint8_t)motor, enabled != 0);
  Serial.print(F("OK ENABLE "));
  Serial.print(motor);
  Serial.print(F(" "));
  Serial.println(enabled != 0 ? 1 : 0);
}

void handleMotor() {
  long motor;
  long direction;
  long steps;
  long pulseUs;
  long keepEnabled = 1;

  if (!readLongArg(motor) || !readLongArg(direction) || !readLongArg(steps) || !readLongArg(pulseUs)) {
    Serial.println(F("ERR MOTOR_ARGS motor direction steps pulse_us [keep_enabled]"));
    return;
  }

  readLongArg(keepEnabled);

  if (!isValidMotor(motor)) {
    Serial.println(F("ERR INVALID_MOTOR"));
    return;
  }

  if (steps < 0 || pulseUs <= 0) {
    Serial.println(F("ERR INVALID_MOTION"));
    return;
  }

  moveMotor((uint8_t)motor, direction != 0, (uint32_t)steps, (unsigned int)pulseUs, keepEnabled != 0);
  Serial.print(F("OK MOTOR "));
  Serial.print(motor);
  Serial.print(F(" STEPS "));
  Serial.println(steps);
}

void moveMotor(uint8_t motor, bool direction, uint32_t steps, unsigned int pulseUs, bool keepEnabled) {
  uint8_t stepPin = motor == 1 ? M1_STEP_PIN : M2_STEP_PIN;
  uint8_t dirPin = motor == 1 ? M1_DIR_PIN : M2_DIR_PIN;

  setMotorEnabled(motor, true);
  digitalWrite(dirPin, direction ? HIGH : LOW);
  delayMicroseconds(5);

  for (uint32_t i = 0; i < steps; i++) {
    digitalWrite(stepPin, HIGH);
    delayMicroseconds(pulseUs);
    digitalWrite(stepPin, LOW);
    delayMicroseconds(pulseUs);
  }

  if (!keepEnabled) {
    setMotorEnabled(motor, false);
  }
}

void setUvPwm(uint8_t pwmValue) {
  currentUvPwm = pwmValue;
  analogWrite(UV_PWM_PIN, currentUvPwm);
}

void setMotorEnabled(uint8_t motor, bool enabled) {
  uint8_t enablePin = motor == 1 ? M1_ENABLE_PIN : M2_ENABLE_PIN;
  bool pinHigh = MOTOR_ENABLE_ACTIVE_LOW ? !enabled : enabled;
  digitalWrite(enablePin, pinHigh ? HIGH : LOW);

  if (motor == 1) {
    motor1Enabled = enabled;
  } else if (motor == 2) {
    motor2Enabled = enabled;
  }
}

void printStatus() {
  Serial.print(F("OK STATUS UV="));
  Serial.print(currentUvPwm);
  Serial.print(F(" M1="));
  Serial.print(motor1Enabled ? 1 : 0);
  Serial.print(F(" M2="));
  Serial.println(motor2Enabled ? 1 : 0);
}

bool readLongArg(long& value) {
  char* token = strtok(NULL, " ");
  if (token == NULL) {
    return false;
  }

  value = atol(token);
  return true;
}

bool isValidMotor(long motor) {
  return motor == 1 || motor == 2;
}

bool equalsCommand(const char* left, const char* right) {
  while (*left != '\0' && *right != '\0') {
    char a = *left;
    char b = *right;
    if (a >= 'a' && a <= 'z') {
      a = a - 'a' + 'A';
    }
    if (b >= 'a' && b <= 'z') {
      b = b - 'a' + 'A';
    }
    if (a != b) {
      return false;
    }
    left++;
    right++;
  }

  return *left == '\0' && *right == '\0';
}
