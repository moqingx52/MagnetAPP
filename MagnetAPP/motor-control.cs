using System;
using System.Threading.Tasks;

namespace MotorControl
{
    public class MotorController : IDisposable
    {
        private readonly SerialCommunication _serial;
        public const int STEPS_PER_REVOLUTION = 3200;

        public MotorController(string portName)
        {
            _serial = new SerialCommunication(portName, 115200);
            _serial.Open();
        }

        public void MoveCircle(byte address, byte direction, int pulse)
        {
            SendMotorCommand(address, direction, 0x003C, 0x00, pulse, 0x00, 0x00);
        }

        public void MoveCircleSlow(byte address, byte direction, int pulse)
        {
            SendMotorCommand(address, direction, 0x0001, 0x00, pulse, 0x00, 0x00);
        }

        public void MoveCircleMulti(byte address1, byte address2, byte direction, int pulse)
        {
            // Send command to first motor
            SendMotorCommand(address1, direction, 0x003C, 0x00, pulse, 0x00, 0x01);
            Task.Delay(100).Wait();

            // Send command to second motor
            SendMotorCommand(address2, direction, 0x003C, 0x00, pulse, 0x00, 0x01);
            Task.Delay(100).Wait();

            // Send sync command
            byte[] syncCommand = new byte[] { 0x00, 0xFF, 0x66, 0x6B };
            _serial.SendCommand(syncCommand);
        }

        public void ReturnToZero(byte address)
        {
            byte[] command = new byte[] { address, 0x9A, 0x02, 0x00, 0x6B };
            _serial.SendCommand(command);
            Task.Delay(100).Wait();
        }

        public double GetCurrentPosition(byte address)
        {
            byte[] command = new byte[] { address, 0x36, 0x6B };
            _serial.SendCommand(command);
            Task.Delay(100).Wait();

            byte[] response = _serial.ReadResponse(8);
            return ParseMotorPosition(response);
        }

        private void SendMotorCommand(byte address, byte direction, int speed, byte acceleration, 
            int pulseCount, byte relativeMode, byte multiMachineSync)
        {
            byte[] command = new byte[]
            {
                address,
                0xFD,
                direction,
                (byte)(speed >> 8),
                (byte)(speed & 0xFF),
                (byte)(acceleration >> 8),
                (byte)(acceleration & 0xFF),
                (byte)(pulseCount >> 16),
                (byte)((pulseCount >> 8) & 0xFF),
                (byte)(pulseCount & 0xFF),
                relativeMode,
                multiMachineSync,
                0x6B
            };

            _serial.SendCommand(command);
            Task.Delay(100).Wait();
        }

        private double ParseMotorPosition(byte[] response)
        {
            if (response.Length < 8) return 0;

            // Extract sign and position value
            int sign = response[2];
            int positionValue = (response[3] << 24) | (response[4] << 16) | (response[5] << 8) | response[6];

            if (sign == 1)
            {
                positionValue *= -1;
            }

            return (positionValue * 360.0) / 65536.0;
        }

        public void Dispose()
        {
            _serial?.Dispose();
        }
    }
}
