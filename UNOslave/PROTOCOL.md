# MagnetAPP UNO 从机通信协议

本文档描述 PC 端 MagnetAPP 与 Arduino UNO R3 从机固件（`UNOslave.ino`）之间的串口协议，并说明 **偏航角**、**滚转角** 步进电机从界面按钮到引脚脉冲的完整命令链路。

---

## 串口参数

| 项目 | 值 |
| --- | --- |
| 波特率 | `115200` |
| 数据位 / 校验 / 停止位 | `8N1` |
| 编码 | ASCII 文本 |
| 命令结束符 | 换行 `\n` |
| 响应前缀 | `OK` 或 `ERR` |

PC 端通过 `comboBox2`（UNO R3 串口下拉框）选择端口，由 `UnoDeviceClient` 打开串口并发送命令。

---

## 电机与界面映射

磁铁位姿控制区（`groupBox7`「磁铁位姿控制」）中，两个「设置」按钮分别驱动 UNO 上的两路步进电机：

| 界面控件 | 功能 | 角度输入框 | 软件电机编号 | 固件 `motor` 参数 | 物理轴 |
| --- | --- | --- | --- | --- | --- |
| `button5` | 偏航角设置 | `textBox3` | `UnoMotor.Motor1` | `1` | 偏航（Yaw） |
| `button6` | 滚转角设置 | `textBox4` | `UnoMotor.Motor2` | `2` | 滚转（Roll） |

> 早期版本曾通过 `MotorController` 走另一套串口二进制协议；当前代码中该路径已注释，**偏航 / 滚转实际均走 UNO 从机**。

---

## 引脚映射

### 偏航角步进电机（Motor 1 / M1）

| 信号 | UNO R3 引脚 | 固件常量 | 说明 |
| --- | --- | --- | --- |
| **STEP** | **D2** | `M1_STEP_PIN` | 步进脉冲，每个上升沿 + 下降沿完成一步 |
| **DIR** | **D4** | `M1_DIR_PIN` | 方向：`0` = 反转，`1` = 正转（由 PC 端角度差符号决定） |
| **EN** | **D7** | `M1_ENABLE_PIN` | 驱动器使能；固件默认 **低电平有效**（`MOTOR_ENABLE_ACTIVE_LOW = true`） |

### 滚转角步进电机（Motor 2 / M2）

| 信号 | UNO R3 引脚 | 固件常量 | 说明 |
| --- | --- | --- | --- |
| **STEP** | **D8** | `M2_STEP_PIN` | 步进脉冲 |
| **DIR** | **D12** | `M2_DIR_PIN` | 方向：`0` = 反转，`1` = 正转 |
| **EN** | **A0**（作数字输出） | `M2_ENABLE_PIN` | 驱动器使能；同样默认低电平有效 |

### 紫外光源 PWM

| 信号 | UNO R3 引脚 | 说明 |
| --- | --- | --- |
| `PWM_OUT` | **D3** | 紫外亮度 PWM；若改线可用 D11 |

`D3` 用于紫外 PWM。`D9` / `D10` 在 UNO 上使用 Timer1 PWM，若后续需用 Timer1 产生更稳定的 STEP 脉冲，建议保持 `D9` / `D10` 空闲。

---

## 偏航角 / 滚转角命令链路

以下以 **button5（偏航角设置）** 为例；**button6（滚转角设置）** 链路相同，仅电机编号与输入框不同。

```
button5 点击
  └─ MagneticFieldController.SetYawButton_Click()
       └─ 读取 textBox3 目标偏航角（度）
            └─ UpdateYawAngleAsync(targetYaw)
                 └─ MoveAngleAxisAsync(UnoMotor.Motor1, ...)
                      ├─ MainForm.GetOrConnectUnoDevice()  → comboBox2 所选串口
                      ├─ 计算角度差 delta = 最短有符号角差(current, target)
                      ├─ 换算步数 steps = round(|delta| / 360 × 3200)
                      └─ UnoDeviceClient.MoveMotorAsync(Motor1, direction, steps)
                           └─ 串口发送: MOTOR 1 <dir> <steps> 800 1
                                └─ UNOslave.ino handleMotor()
                                     ├─ setMotorEnabled(1, true)  → EN(D7) 拉至使能
                                     ├─ digitalWrite(DIR, dir)     → 设置 D4 方向
                                     └─ 循环 steps 次脉冲          → D2 STEP 高低翻转
```

**button6（滚转角）** 对应链路：

| 步骤 | 偏航 (button5) | 滚转 (button6) |
| --- | --- | --- |
| 事件处理 | `SetYawButton_Click` | `SetRollButton_Click` |
| 输入框 | `textBox3` | `textBox4` |
| 更新方法 | `UpdateYawAngleAsync` | `UpdateRollAngleAsync` |
| 电机枚举 | `UnoMotor.Motor1` | `UnoMotor.Motor2` |
| 串口命令 | `MOTOR 1 ...` | `MOTOR 2 ...` |
| STEP 引脚 | D2 | D8 |
| DIR 引脚 | D4 | D12 |
| EN 引脚 | D7 | A0 |

### 步数换算

```text
steps = round(|delta| / 360 × STEPS_PER_REVOLUTION)
STEPS_PER_REVOLUTION = 3200   // 定义于 MotorController.STEPS_PER_REVOLUTION
```

- `delta`：当前角与目标角之间的最短有符号角差（范围 −180° ~ +180°）。
- `delta ≥ 0` → `direction = 1`（`UnoMotorDirection.Forward`）
- `delta < 0` → `direction = 0`（`UnoMotorDirection.Reverse`）
- `steps = 0` 时不发送 `MOTOR` 命令。

### 运动后等待

电机转动完成后，PC 端会等待 **10 秒**（`MotorSettleDelay`）再更新内部当前角度记录，用于机械稳定。

### 其他触发路径

除手动点击 button5 / button6 外，以下逻辑也会调用相同的 `UpdateYawAngleAsync` / `UpdateRollAngleAsync`：

- CSV 磁场查表后自动设置角度（`SetAnglesForFieldVectorAsync`）
- GCode 执行流程中根据磁场向量注释驱动磁铁（`GCodeController` → `MagneticFieldController`）

这些路径最终发出的 UNO 串口命令格式与按钮触发完全一致。

---

## 命令列表

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

### 命令说明

| 命令 | 功能 |
| --- | --- |
| `PING` | 心跳检测，返回 `OK PONG` |
| `STATUS` | 查询紫外 PWM 与两路电机使能状态 |
| `UV <0-255>` | 设置紫外 PWM 原始值（0–255） |
| `UVP <0-100>` | 按百分比设置紫外亮度，固件映射为 0–255 PWM |
| `UVOFF` | 关闭紫外（PWM = 0） |
| `ENABLE <motor> <0\|1>` | 单独使能 / 禁用指定电机驱动 |
| `MOTOR` | **阻塞式**步进运动（偏航 / 滚转按钮使用此命令） |
| `STOP` | 关闭紫外并使两路电机全部禁用 |

### `MOTOR` 参数

| 参数 | 含义 |
| --- | --- |
| `motor` | `1` = 偏航（M1），`2` = 滚转（M2） |
| `direction` | `0` = 反转，`1` = 正转 |
| `steps` | 非负整数，步进脉冲个数 |
| `pulse_us` | STEP 高电平与低电平各持续的微秒数 |
| `keep_enabled` | 可选，`0` 或 `1`，默认 `1`；为 `1` 时运动后保持使能 |

固件收到 `MOTOR` 后会依次：使能对应 EN 引脚 → 设置 DIR → 在 STEP 引脚输出 `steps` 个脉冲 → 按 `keep_enabled` 决定是否保持使能。

---

## 示例

### 偏航角：正转 90°（3200 步/圈 → 800 步）

```text
MOTOR 1 1 800 800 1
```

对应引脚动作：EN **D7** 使能，DIR **D4** = HIGH，STEP **D2** 输出 800 个脉冲，脉冲宽度 800 µs。

### 滚转角：反转 45°（400 步）

```text
MOTOR 2 0 400 800 1
```

对应引脚动作：EN **A0** 使能，DIR **D12** = LOW，STEP **D8** 输出 400 个脉冲。

### 其他常用命令

```text
PING
STATUS
ENABLE 1 1
UVP 35
UVOFF
STOP
```

---

## 响应格式

成功示例：

```text
OK MOTOR 1 STEPS 800
OK ENABLE 1 1
OK PONG
```

失败示例：

```text
ERR INVALID_MOTOR
ERR INVALID_MOTION
ERR UNKNOWN_COMMAND
```

---

## 驱动器使能极性

当前固件默认步进驱动器 **使能为低电平有效**（`MOTOR_ENABLE_ACTIVE_LOW = true`），与多数 A4988 / DRV8825 等模块一致。若硬件为高电平使能，请修改 `UNOslave.ino` 中的 `MOTOR_ENABLE_ACTIVE_LOW`。

---

## 相关源文件

| 层级 | 文件 | 职责 |
| --- | --- | --- |
| 界面 | `MagnetAPP/MainForm.cs` | `button5` / `button6`、`comboBox2`、UNO 连接 |
| 业务 | `MagnetAPP/MagneticFieldController.cs` | 角度计算、步数换算、调用 `MoveMotorAsync` |
| 协议 | `MagnetAPP/UnoDeviceProtocol.cs` | 组装 `MOTOR` / `ENABLE` 等命令字符串 |
| 串口 | `MagnetAPP/UnoDeviceClient.cs` | 115200 串口读写 |
| 引脚定义 | `MagnetAPP/UnoDeviceProtocol.cs` → `UnoPinMap` | 与固件引脚一致 |
| 固件 | `UNOslave/UNOslave.ino` | 解析命令、驱动 STEP / DIR / EN |
