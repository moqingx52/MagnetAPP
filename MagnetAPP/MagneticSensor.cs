// MagneticSensor.cs
using System;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MagneticFieldReader
{
    public class MagneticSensor : IDisposable
    {
        private SerialPort serialPort;
        private bool isRunning;
        private CancellationTokenSource cancellationSource;

        public event EventHandler<MagneticDataEventArgs> OnDataReceived;
        public event EventHandler<ErrorEventArgs> OnError;

        public (double X, double Y, double Z) CurrentMagneticField { get; private set; }

        public bool IsRunning => isRunning;

        public MagneticSensor(string portName = "COM19", int baudRate = 9600)
        {
            serialPort = new SerialPort(portName, baudRate)
            {
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One,
                ReadTimeout = 2000,      // 增加超时时间到2秒
                WriteTimeout = 2000,
                DtrEnable = true,        // 启用DTR信号
                RtsEnable = true,        // 启用RTS信号
                ReceivedBytesThreshold = 1
            };

            serialPort.DataReceived += DataReceivedHandler;
        }

        public bool StartReading()
        {
            if (isRunning) return false;

            try
            {
                if (!serialPort.IsOpen)
                {
                    serialPort.Open();
                    // 发送初始命令以确保设备处于正确状态
                    serialPort.WriteLine("\r\n");
                    Thread.Sleep(100);
                }

                isRunning = true;
                cancellationSource = new CancellationTokenSource();

                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, new ErrorEventArgs(ex));
                return false;
            }
        }

        public void StopReading()
        {
            if (!isRunning) return;

            isRunning = false;
            cancellationSource?.Cancel();

            if (serialPort.IsOpen)
            {
                serialPort.DataReceived -= DataReceivedHandler;
                serialPort.Close();
            }
        }

        private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            if (!isRunning) return;

            try
            {
                if (serialPort.BytesToRead > 0)
                {
                    string line = serialPort.ReadLine().Trim();
                    Regex regex = new Regex(@"X = ([\-\d\.]+) mT; Y = ([\-\d\.]+) mT; Z = ([\-\d\.]+) mT");
                    Match match = regex.Match(line);

                    if (match.Success)
                    {
                        double x = double.Parse(match.Groups[1].Value);
                        double y = double.Parse(match.Groups[2].Value);
                        double z = double.Parse(match.Groups[3].Value);

                        CurrentMagneticField = (x, y, z);
                        OnDataReceived?.Invoke(this, new MagneticDataEventArgs(x, y, z));
                    }
                }
            }
            catch (Exception ex)
            {
                if (!(ex is TimeoutException)) // 忽略超时异常的输出
                {
                    OnError?.Invoke(this, new ErrorEventArgs(ex));
                }
            }
        }

        public void Dispose()
        {
            StopReading();
            serialPort?.Dispose();
        }
    }

    public class MagneticDataEventArgs : EventArgs
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public MagneticDataEventArgs(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}


