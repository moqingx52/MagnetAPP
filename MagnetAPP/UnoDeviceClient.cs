using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MotorControl
{
    public sealed class UnoDeviceClient : IDisposable
    {
        private readonly SerialPort _serialPort;
        private readonly object _syncRoot = new();

        public event Action<string>? LineReceived;

        public string PortName => _serialPort.PortName;
        public bool IsOpen => _serialPort.IsOpen;
        public UnoPinMap Pins { get; }

        public UnoDeviceClient(
            string portName,
            int baudRate = UnoDeviceProtocol.DefaultBaudRate,
            UnoPinMap? pins = null)
        {
            Pins = pins ?? UnoPinMap.Default;
            _serialPort = new SerialPort(portName, baudRate)
            {
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One,
                Handshake = Handshake.None,
                Encoding = Encoding.ASCII,
                NewLine = "\n",
                ReadTimeout = 1000,
                WriteTimeout = 1000
            };

            _serialPort.DataReceived += SerialPort_DataReceived;
        }

        public void Open()
        {
            if (_serialPort.IsOpen)
            {
                return;
            }

            _serialPort.Open();
            Thread.Sleep(2000);
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();
        }

        public void Close()
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
        }

        public Task SendRawCommandAsync(string command, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                throw new ArgumentException("Command cannot be empty.", nameof(command));
            }

            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteLine(command.Trim());
            }, cancellationToken);
        }

        public Task PingAsync(CancellationToken cancellationToken = default)
        {
            return SendRawCommandAsync(UnoDeviceProtocol.PingCommand, cancellationToken);
        }

        public Task RequestStatusAsync(CancellationToken cancellationToken = default)
        {
            return SendRawCommandAsync(UnoDeviceProtocol.StatusCommand, cancellationToken);
        }

        public Task StopAllAsync(CancellationToken cancellationToken = default)
        {
            return SendRawCommandAsync(UnoDeviceProtocol.StopCommand, cancellationToken);
        }

        public Task SetUvPwmAsync(int pwmValue, CancellationToken cancellationToken = default)
        {
            return SendRawCommandAsync(UnoDeviceProtocol.BuildSetUvPwmCommand(pwmValue), cancellationToken);
        }

        public Task SetUvBrightnessAsync(int percent, CancellationToken cancellationToken = default)
        {
            return SendRawCommandAsync(UnoDeviceProtocol.BuildSetUvBrightnessCommand(percent), cancellationToken);
        }

        public Task TurnUvOffAsync(CancellationToken cancellationToken = default)
        {
            return SendRawCommandAsync(UnoDeviceProtocol.UvOffCommand, cancellationToken);
        }

        public Task SetMotorEnabledAsync(
            UnoMotor motor,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            return SendRawCommandAsync(
                UnoDeviceProtocol.BuildEnableMotorCommand(motor, enabled),
                cancellationToken);
        }

        public Task EnableBothMotorsAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                await SetMotorEnabledAsync(UnoMotor.Motor1, true, cancellationToken);
                await SetMotorEnabledAsync(UnoMotor.Motor2, true, cancellationToken);
            }, cancellationToken);
        }

        public Task MoveMotorAsync(
            UnoMotor motor,
            UnoMotorDirection direction,
            int steps,
            int pulseWidthMicroseconds = UnoDeviceProtocol.DefaultStepPulseWidthMicroseconds,
            bool keepEnabled = true,
            CancellationToken cancellationToken = default)
        {
            return SendRawCommandAsync(
                UnoDeviceProtocol.BuildMoveMotorCommand(
                    motor,
                    direction,
                    steps,
                    pulseWidthMicroseconds,
                    keepEnabled),
                cancellationToken);
        }

        private void WriteLine(string command)
        {
            lock (_syncRoot)
            {
                if (!_serialPort.IsOpen)
                {
                    throw new InvalidOperationException("UNO serial port is not open.");
                }

                _serialPort.WriteLine(command);
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                while (_serialPort.IsOpen && _serialPort.BytesToRead > 0)
                {
                    string line = _serialPort.ReadLine().Trim();
                    if (line.Length > 0)
                    {
                        LineReceived?.Invoke(line);
                    }
                }
            }
            catch (TimeoutException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        public void Dispose()
        {
            _serialPort.DataReceived -= SerialPort_DataReceived;
            Close();
            _serialPort.Dispose();
        }
    }
}
