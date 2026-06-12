# MagnetAPP UNO Slave Protocol

## Serial

- Baud rate: `115200`
- Format: `8N1`
- Encoding: ASCII text
- Command terminator: newline, `\n`
- Response prefix: `OK` or `ERR`

## Pin Map

| Signal | UNO R3 Pin |
| --- | --- |
| `M1_STEP` | `D2` |
| `M1_DIR` | `D4` |
| `M1_EN` | `D7` |
| `M2_STEP` | `D8` |
| `M2_DIR` | `D12` |
| `M2_EN` | `A0` as digital output |
| `PWM_OUT` | `D3` |

`D3` is used for UV PWM. `D11` is also acceptable if rewired. Keep `D9/D10` free because they use Timer1 PWM on UNO, and Timer1 may later be useful for steadier STEP pulse generation.

## Commands

```text
PING
STATUS
UV <0-255>
UVP <0-100>
UVOFF
ENABLE <motor:1|2> <enabled:0|1>
MOTOR <motor:1|2> <direction:0|1> <steps> <pulse_us> [keep_enabled:0|1]
STOP
```

Examples:

```text
UVP 35
UV 128
ENABLE 1 1
MOTOR 1 1 3200 800 1
MOTOR 2 0 1600 800 0
STOP
```

The current firmware treats motor driver enable as active-low by default, which matches many stepper drivers. Change `MOTOR_ENABLE_ACTIVE_LOW` in `UNOslave.ino` if your driver uses active-high enable.
