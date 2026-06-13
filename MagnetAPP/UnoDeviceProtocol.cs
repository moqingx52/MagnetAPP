using System;

namespace MotorControl
{
    public enum UnoMotor
    {
        Motor1 = 1,
        Motor2 = 2
    }

    public enum UnoMotorDirection
    {
        Reverse = 0,
        Forward = 1
    }

    public sealed record UnoPinMap(
        int Motor1Step = 9,
        int Motor1Direction = 4,
        int Motor1Enable = 7,
        int Motor2Step = 10,
        int Motor2Direction = 8,
        string Motor2Enable = "12",
        int PwmOutput = 3)
    {
        public static UnoPinMap Default { get; } = new();
    }

    public static class UnoDeviceProtocol
    {
        /// <summary>1.8° 步进电机 + DM556 1/8 细分 (1600 pulse/rev)。</summary>
        public const int StepsPerRevolution = 1600;

        public const int DefaultBaudRate = 115200;
        public const int MinimumPwmValue = 0;
        public const int MaximumPwmValue = 255;
        public const int MinimumBrightnessPercent = 0;
        public const int MaximumBrightnessPercent = 100;
        public const int DefaultStepPulseWidthMicroseconds = 800;

        public static string PingCommand => "PING";
        public static string StatusCommand => "STATUS";
        public static string StopCommand => "STOP";
        public static string UvOffCommand => "UVOFF";

        public static string BuildSetUvPwmCommand(int pwmValue)
        {
            return $"UV {Clamp(pwmValue, MinimumPwmValue, MaximumPwmValue)}";
        }

        public static string BuildSetUvBrightnessCommand(int percent)
        {
            return $"UVP {Clamp(percent, MinimumBrightnessPercent, MaximumBrightnessPercent)}";
        }

        public static string BuildEnableMotorCommand(UnoMotor motor, bool enabled)
        {
            return $"ENABLE {(int)motor} {(enabled ? 1 : 0)}";
        }

        public static string BuildMoveMotorCommand(
            UnoMotor motor,
            UnoMotorDirection direction,
            int steps,
            int pulseWidthMicroseconds = DefaultStepPulseWidthMicroseconds,
            bool keepEnabled = true)
        {
            if (steps < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(steps), "Step count cannot be negative.");
            }

            if (pulseWidthMicroseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pulseWidthMicroseconds),
                    "Pulse width must be greater than zero.");
            }

            return FormattableString.Invariant(
                $"MOTOR {(int)motor} {(int)direction} {steps} {pulseWidthMicroseconds} {(keepEnabled ? 1 : 0)}");
        }

        public static int BrightnessPercentToPwm(int percent)
        {
            percent = Clamp(percent, MinimumBrightnessPercent, MaximumBrightnessPercent);
            return (int)Math.Round(percent / 100.0 * MaximumPwmValue, MidpointRounding.AwayFromZero);
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Min(Math.Max(value, minimum), maximum);
        }
    }
}
