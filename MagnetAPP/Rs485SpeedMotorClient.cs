using System;

namespace MotorControl
{
    /// <summary>
    /// RS485 stepper driver client using 0xF6 continuous speed mode (MotorApp protocol).
    /// </summary>
    public sealed class Rs485SpeedMotorClient : IDisposable
    {
        public const byte RotationMotorAddress = 0x01;
        public const byte LinearMotorAddress = 0x02;
        public const double DefaultLeadMm = 2.5;
        public const double DefaultLinearSpeedMmPerSec = 2.0;

        private const byte CommandSpeedMode = 0xF6;
        private const byte SpeedSlopeHigh = 0x03;
        private const byte SpeedSlopeLow = 0xE8;
        private const byte FrameTail = 0x6B;

        private readonly SerialCommunication _serial;

        public Rs485SpeedMotorClient(string portName)
        {
            _serial = new SerialCommunication(portName, 115200);
        }

        public bool IsOpen => _serial.IsOpen;

        public void Open()
        {
            _serial.Open();
        }

        public void Close()
        {
            _serial.Close();
        }

        public void StopMotor(byte address)
        {
            SendSpeedFrame(address, direction: 0, speedValue: 0);
        }

        public void StopAll()
        {
            StopMotor(RotationMotorAddress);
            StopMotor(LinearMotorAddress);
        }

        /// <summary>
        /// Set rotation speed in rad/s. Negative values reverse direction.
        /// </summary>
        public void SetRotationSpeedRadPerSec(double omegaRadPerSec)
        {
            if (Math.Abs(omegaRadPerSec) < 1e-9)
            {
                StopMotor(RotationMotorAddress);
                return;
            }

            byte direction = omegaRadPerSec >= 0 ? (byte)0 : (byte)1;
            double hz = Math.Abs(omegaRadPerSec) / (2.0 * Math.PI);
            int speedValue = HzToSpeedValue(hz);
            SendSpeedFrame(RotationMotorAddress, direction, speedValue);
        }

        /// <summary>
        /// Set linear motor speed in mm/s along the ball screw.
        /// </summary>
        public void SendLinearSpeed(byte address, byte direction, double mmPerSec, double leadMm)
        {
            if (mmPerSec <= 0 || leadMm <= 0)
            {
                StopMotor(address);
                return;
            }

            double hz = mmPerSec / leadMm;
            int speedValue = HzToSpeedValue(hz);
            SendSpeedFrame(address, direction, speedValue);
        }

        public void SendLinearForward(double mmPerSec = DefaultLinearSpeedMmPerSec, double leadMm = DefaultLeadMm)
        {
            SendLinearSpeed(LinearMotorAddress, direction: 0, mmPerSec, leadMm);
        }

        public void SendLinearBackward(double mmPerSec = DefaultLinearSpeedMmPerSec, double leadMm = DefaultLeadMm)
        {
            SendLinearSpeed(LinearMotorAddress, direction: 1, mmPerSec, leadMm);
        }

        public void StopLinear()
        {
            StopMotor(LinearMotorAddress);
        }

        private static int HzToSpeedValue(double hz)
        {
            double speedRpm = hz * 60.0;
            return (int)(speedRpm * 10.0);
        }

        private void SendSpeedFrame(byte address, byte direction, int speedValue)
        {
            speedValue = Math.Clamp(speedValue, 0, 65535);
            byte speedHigh = (byte)(speedValue >> 8);
            byte speedLow = (byte)(speedValue & 0xFF);

            byte[] command =
            {
                address,
                CommandSpeedMode,
                direction,
                SpeedSlopeHigh,
                SpeedSlopeLow,
                speedHigh,
                speedLow,
                0x00,
                FrameTail
            };

            _serial.SendCommand(command);
        }

        public void Dispose()
        {
            try
            {
                StopAll();
            }
            catch
            {
                // Best-effort stop on dispose.
            }

            _serial.Dispose();
        }
    }
}
