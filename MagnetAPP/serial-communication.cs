using System;
using System.IO.Ports;
using System.Threading.Tasks;
using System.Text;
using Newtonsoft.Json;

namespace MotorControl
{
    public class SerialCommunication : IDisposable
    {
        private SerialPort _serialPort;
        private readonly object _lockObject = new object();

        public SerialCommunication(string portName, int baudRate)
        {
            _serialPort = new SerialPort(portName, baudRate)
            {
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One
            };
        }

        public void Open()
        {
            if (!_serialPort.IsOpen)
            {
                _serialPort.Open();
            }
        }

        public void Close()
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
        }

        public void SendCommand(byte[] command)
        {
            lock (_lockObject)
            {
                if (_serialPort.IsOpen)
                {
                    _serialPort.Write(command, 0, command.Length);
                }
            }
        }

        public byte[] ReadResponse(int count, int timeout = 100)
        {
            var response = new byte[count];
            int bytesRead = 0;
            DateTime startTime = DateTime.Now;

            while (bytesRead < count)
            {
                if ((DateTime.Now - startTime).TotalMilliseconds > timeout)
                {
                    break;
                }

                if (_serialPort.BytesToRead > 0)
                {
                    response[bytesRead] = (byte)_serialPort.ReadByte();
                    bytesRead++;
                }
                Task.Delay(10).Wait();
            }

            return response;
        }

        public void Dispose()
        {
            _serialPort?.Dispose();
        }
    }

    public class ArduinoCommunication : IDisposable
    {
        private SerialPort _serialPort;
        private CancellationTokenSource _cancellationTokenSource;

        public ArduinoCommunication(string portName, int baudRate = 9600)
        {
            _serialPort = new SerialPort(portName, baudRate);
            _serialPort.Open();
            Task.Delay(2000).Wait(); // Wait for port to stabilize
        }

        public void SendCommand(int motorIndex, bool enable, bool direction, int steps)
        {
            var command = new
            {
                motorIndex = motorIndex,
                enable = enable,
                direction = direction,
                steps = steps
            };

            string jsonCommand = JsonConvert.SerializeObject(command);
            _serialPort.WriteLine(jsonCommand);
        }

        public void Dispose()
        {
            _serialPort?.Dispose();
        }
    }
}
