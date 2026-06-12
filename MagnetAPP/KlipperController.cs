using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using log4net;

namespace MotorControl
{
    public sealed class KlipperController : IDisposable
    {
        private readonly MainForm _mainForm;
        private readonly ILog _log = LogManager.GetLogger(typeof(KlipperController));

        // UI Controls
        private readonly TextBox _txtKlipperAddress;
        private readonly TextBox _txtCommand;
        private readonly TextBox _textBoxGcode;
        private readonly TextBox _textBoxGcode2;
        private readonly TextBox _generatedGCodePathTextBox;
        private readonly Button _btnConnect;
        private readonly Button _btnSendCommand;
        private readonly Button _buttonGcode;
        private readonly Button _buttonGcode2;
        private readonly RichTextBox _rtbLog;
        private readonly Label _lblStatus;

        private KlipperCommunicator _klipper;
        private readonly MagneticVoxelGCodeGenerator _magneticVoxelGCodeGenerator = new();

        public KlipperCommunicator Klipper => _klipper;

        public KlipperController(MainForm mainForm, TextBox txtKlipperAddress, TextBox txtCommand,
            TextBox textBoxGcode, TextBox textBoxGcode2, Button btnConnect, Button btnSendCommand,
            Button buttonGcode, Button buttonGcode2, RichTextBox rtbLog, Label lblStatus,
            TextBox generatedGCodePathTextBox)
        {
            _mainForm = mainForm;
            _txtKlipperAddress = txtKlipperAddress;
            _txtCommand = txtCommand;
            _textBoxGcode = textBoxGcode;
            _textBoxGcode2 = textBoxGcode2;
            _generatedGCodePathTextBox = generatedGCodePathTextBox;
            _btnConnect = btnConnect;
            _btnSendCommand = btnSendCommand;
            _buttonGcode = buttonGcode;
            _buttonGcode2 = buttonGcode2;
            _rtbLog = rtbLog;
            _lblStatus = lblStatus;

            InitializeKlipperControls();
        }

        private void InitializeKlipperControls()
        {
            // 初始化Klipper通讯器（不立即连接）
            _klipper = new KlipperCommunicator(""); // 地址将通过UI设置

            // 注册事件处理器
            RegisterKlipperEvents();

            // 设置初始状态
            _btnSendCommand.Enabled = false;
            UpdateStatus(false);

            // 绑定按钮事件
            _btnConnect.Click += ConnectButton_Click;
            _btnSendCommand.Click += SendCommandButton_Click;
            _buttonGcode.Click += ButtonGcode_Click;
            _buttonGcode2.Click += ButtonGcode2_Click;
        }

        private void RegisterKlipperEvents()
        {
            // 注册所有事件处理器
            _klipper.OnMessageReceived += HandleKlipperMessage;
            _klipper.OnError += HandleKlipperError;
            _klipper.OnConnectionStateChanged += HandleConnectionStateChanged;
            _klipper.StatusUpdated += HandleStatusUpdated;
        }

        private void UnregisterKlipperEvents()
        {
            if (_klipper != null)
            {
                _klipper.OnMessageReceived -= HandleKlipperMessage;
                _klipper.OnError -= HandleKlipperError;
                _klipper.OnConnectionStateChanged -= HandleConnectionStateChanged;
                _klipper.StatusUpdated -= HandleStatusUpdated;
            }
        }

        private async void ConnectButton_Click(object? sender, EventArgs e)
        {
            try
            {
                if (!_klipper.IsConnected)
                {
                    string address = _txtKlipperAddress.Text.Trim();
                    if (string.IsNullOrEmpty(address))
                    {
                        MessageBox.Show("Please enter Klipper address", "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    _btnConnect.Enabled = false;
                    _txtKlipperAddress.Enabled = false;

                    // 清理旧的实例和事件
                    UnregisterKlipperEvents();
                    _klipper.Dispose();

                    // 创建新的实例并注册事件
                    _klipper = new KlipperCommunicator(address);
                    RegisterKlipperEvents();

                    if (await _klipper.ConnectAsync())
                    {
                        _btnConnect.Text = "Disconnect";
                        _btnSendCommand.Enabled = true;
                    }
                }
                else
                {
                    _klipper.Disconnect();
                    _btnConnect.Text = "Connect";
                    _btnSendCommand.Enabled = false;
                    _txtKlipperAddress.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Connection error: {ex}");
                MessageBox.Show($"Connection error: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnConnect.Enabled = true;
            }
        }

        private async void SendCommandButton_Click(object? sender, EventArgs e)
        {
            try
            {
                string command = _txtCommand.Text.Trim();
                if (string.IsNullOrEmpty(command))
                {
                    MessageBox.Show("Please enter a command", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _btnSendCommand.Enabled = false;
                if (await _klipper.SendCommandAsync(command))
                {
                    // 清除命令输入框或保留，根据需求决定
                    // txtCommand.Clear();
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Command error: {ex}");
                MessageBox.Show($"Error sending command: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnSendCommand.Enabled = true;
            }
        }

        private async void ButtonGcode_Click(object sender, EventArgs e)
        {
            string filePath = _textBoxGcode.Text.Trim();
            if (!File.Exists(filePath))
            {
                _textBoxGcode.Text = "文件不存在，请检查文件路径。";
                return;
            }

            if (!string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                _textBoxGcode.Text = "输入文件必须是 .xlsx。";
                return;
            }

            _buttonGcode.Enabled = false;
            try
            {
                string outputPath = await _magneticVoxelGCodeGenerator.GenerateAsync(filePath);
                _generatedGCodePathTextBox.Text = outputPath;
                _textBoxGcode.Text = filePath;
                MessageBox.Show($"GCODE 文件已生成:\n{outputPath}", "完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _textBoxGcode.Text = "处理文件时发生错误: " + ex.Message;
            }
            finally
            {
                _buttonGcode.Enabled = true;
            }
        }

        private async void ButtonGcode2_Click(object sender, EventArgs e)
        {
            try
            {
                string filePath = _textBoxGcode2.Text.Trim();
                if (string.IsNullOrEmpty(filePath))
                {
                    MessageBox.Show("请输入 GCODE 文件路径", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!File.Exists(filePath))
                {
                    MessageBox.Show("指定的 GCODE 文件不存在", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _buttonGcode2.Enabled = false;

                using (StreamReader reader = new StreamReader(filePath))
                {
                    string command;
                    while ((command = await reader.ReadLineAsync()) != null)
                    {
                        command = command.Trim();
                        if (string.IsNullOrEmpty(command) || command.StartsWith(";")) // 跳过空行和注释
                        {
                            continue;
                        }

                        if (await _klipper.SendCommandAsync(command))
                        {
                            // 成功发送命令后继续发送下一条
                            await Task.Delay(100); // 可选：小延迟以防止过多命令导致打印机负载
                        }
                        else
                        {
                            MessageBox.Show($"发送命令失败: {command}", "错误",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                            break; // 发送命令失败时停止
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"读取文件错误: {ex}");
                MessageBox.Show($"读取 GCODE 文件时出错: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _buttonGcode2.Enabled = true;
            }
        }

        public async Task<bool> SendCommandAsync(string command)
        {
            return await _klipper.SendCommandAsync(command);
        }

        private void HandleKlipperMessage(string message)
        {
            // 可以在此处处理接收到的消息
        }

        private void HandleKlipperError(string error)
        {
            _mainForm.RunOnUiThread(() =>
            {
                _rtbLog.SelectionColor = Color.Red;
                _rtbLog.AppendText($"{error}{Environment.NewLine}");
                _rtbLog.SelectionColor = _rtbLog.ForeColor;
                _rtbLog.ScrollToCaret();
            });
        }

        private void HandleConnectionStateChanged(bool isConnected)
        {
            _mainForm.RunOnUiThread(() => UpdateStatus(isConnected));
        }

        private void UpdateStatus(bool isConnected)
        {
            _lblStatus.Text = isConnected ? "Connected" : "Disconnected";
            _lblStatus.ForeColor = isConnected ? Color.Green : Color.Red;
            _btnSendCommand.Enabled = isConnected;
            _txtKlipperAddress.Enabled = !isConnected;
            _btnConnect.Text = isConnected ? "Disconnect" : "Connect";
        }

        private void HandleStatusUpdated(object? sender, EventArgs e)
        {
            // 处理状态更新，如果需要
        }

        public void Dispose()
        {
            _btnConnect.Click -= ConnectButton_Click;
            _btnSendCommand.Click -= SendCommandButton_Click;
            _buttonGcode.Click -= ButtonGcode_Click;
            _buttonGcode2.Click -= ButtonGcode2_Click;

            UnregisterKlipperEvents();
            _klipper?.Dispose();
        }
    }
}
