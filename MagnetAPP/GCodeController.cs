using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using DLP;
using GcodeFollow;
using _3DPrint;

namespace MotorControl
{
    public sealed class GCodeController : IDisposable
    {
        private readonly MainForm _mainForm;
        private readonly KlipperController _klipperController;
        private readonly MagneticFieldController? _magneticFieldController;

        private readonly TextBox _followGCodePathTextBox;
        private readonly Button _startFollowButton;
        private readonly RichTextBox _sentGCodeLog;
        private readonly RichTextBox _followPositionLog;

        private readonly TextBox _printGCodeTextBox;
        private readonly Button _executePrintButton;
        private readonly RichTextBox _printGCodeLog;
        private readonly RichTextBox _pixelLog;

        private readonly GCodeProcessor _processor;
        private readonly _3DPrinter _printer;
        private bool _isSubscribed = false;

        public GCodeController(MainForm mainForm, KlipperController klipperController,
            MagneticFieldController? magneticFieldController,
            TextBox textBox1, Button button3, RichTextBox richTextBox2, RichTextBox richTextBox3,
            TextBox textBox2, Button button4, RichTextBox richTextBox4, RichTextBox richTextBox5)
        {
            _mainForm = mainForm;
            _klipperController = klipperController;
            _magneticFieldController = magneticFieldController;
            _followGCodePathTextBox = textBox1;
            _startFollowButton = button3;
            _sentGCodeLog = richTextBox2;
            _followPositionLog = richTextBox3;
            _printGCodeTextBox = textBox2;
            _executePrintButton = button4;
            _printGCodeLog = richTextBox4;
            _pixelLog = richTextBox5;

            _processor = new GCodeProcessor();
            _processor.OnGCodeOutput += HandleGCodeOutput;
            _printer = new _3DPrinter(_mainForm);

            BindEvents();
        }

        private void BindEvents()
        {
            _startFollowButton.Click += StartFollowButton_Click;
            _executePrintButton.Click += ExecutePrintButton_Click;
        }

        private async void HandleGCodeOutput(string gcode)
        {
            _sentGCodeLog.AppendLineSafe(gcode);

            await _klipperController.SendCommandAsync(gcode);
        }

        // 订阅 OnPositionUpdate 事件
        private void SubscribePositionUpdate()
        {
            if (!_isSubscribed) // 避免重复订阅
            {
                _isSubscribed = true;
                _processor.OnPositionUpdate += HandlePositionUpdate;
            }
        }

        private void HandlePositionUpdate(double x, double y)
        {
            _followPositionLog.RunOnUiThread(() =>
                _followPositionLog.Text = $"currentX: {x}, currentY: {y}{Environment.NewLine}");
        }

        // 按钮点击事件处理逻辑 - Screen following
        private async void StartFollowButton_Click(object? sender, EventArgs e)
        {
            try
            {
                // 确保事件订阅在文件处理前完成
                SubscribePositionUpdate();

                string filePath2 = _followGCodePathTextBox.Text;

                // 检查文件路径是否有效
                if (string.IsNullOrWhiteSpace(filePath2) || !File.Exists(filePath2))
                {
                    _sentGCodeLog.AppendLineSafe("错误：文件路径无效。");
                    return;
                }

                _startFollowButton.Enabled = false;
                try
                {
                    await ProcessMagneticVoxelGCodeFileAsync(filePath2);
                }
                finally
                {
                    _startFollowButton.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                _sentGCodeLog.AppendLineSafe("异常：" + ex.Message);
            }
        }

        private async Task ProcessMagneticVoxelGCodeFileAsync(string filePath)
        {
            string[] lines = await File.ReadAllLinesAsync(filePath);
            DisplayManager.Instance.EnableAutoUpdate();

            try
            {
                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";"))
                    {
                        continue;
                    }

                    string command = StripDoubleSlashComment(line).Trim();
                    if (string.IsNullOrWhiteSpace(command))
                    {
                        continue;
                    }

                    if (TryExtractFieldVector(line, out double mx, out double my, out double mz)
                        && _magneticFieldController is not null)
                    {
                        await _magneticFieldController.SetAnglesForFieldVectorAsync(mx, my, mz);
                    }

                    UpdateWhiteSquareFromGCode(command);
                    _sentGCodeLog.AppendLineSafe(command);

                    if (!await _klipperController.SendCommandAsync(command))
                    {
                        throw new InvalidOperationException($"发送命令失败: {command}");
                    }

                    await Task.Delay(100);
                }
            }
            finally
            {
                DisplayManager.Instance.DisableAutoUpdate();
                DisplayManager.Instance.ShowBlackScreen();
            }
        }

        private void UpdateWhiteSquareFromGCode(string gcode)
        {
            if (!TryExtractXy(gcode, out double x, out double y))
            {
                return;
            }

            const double pixelSizeMillimeters = 0.018;
            const int pixelGroup = 4;
            int pixelX = (int)Math.Round(x / pixelSizeMillimeters / pixelGroup);
            int pixelY = (int)Math.Round(y / pixelSizeMillimeters / pixelGroup);

            _followPositionLog.RunOnUiThread(() =>
                _followPositionLog.Text = $"currentX: {pixelX}, currentY: {pixelY}{Environment.NewLine}");

            DisplayManager.Instance.UpdateDisplay(pixelX, pixelX + 1, pixelY, pixelY + 1);
        }

        private static string StripDoubleSlashComment(string line)
        {
            int commentIndex = line.IndexOf("//", StringComparison.Ordinal);
            return commentIndex >= 0 ? line[..commentIndex] : line;
        }

        private static bool TryExtractFieldVector(string line, out double x, out double y, out double z)
        {
            x = y = z = 0;
            const string numberPattern = @"([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)";
            Match match = Regex.Match(
                line,
                @"//\s*\[\s*" + numberPattern + @"\s*,\s*" + numberPattern + @"\s*,\s*" + numberPattern + @"\s*\]");
            return match.Success
                && TryParseDouble(match.Groups[1].Value, out x)
                && TryParseDouble(match.Groups[2].Value, out y)
                && TryParseDouble(match.Groups[3].Value, out z);
        }

        private static bool TryExtractXy(string gcode, out double x, out double y)
        {
            x = y = 0;
            const string numberPattern = @"([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)";
            Match xMatch = Regex.Match(gcode, @"(?:^|\s)X" + numberPattern);
            Match yMatch = Regex.Match(gcode, @"(?:^|\s)Y" + numberPattern);
            return xMatch.Success
                && yMatch.Success
                && TryParseDouble(xMatch.Groups[1].Value, out x)
                && TryParseDouble(yMatch.Groups[1].Value, out y);
        }

        private static bool TryParseDouble(string value, out double result)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                || double.TryParse(value, out result);
        }

        // Print testing execution
        private async void ExecutePrintButton_Click(object? sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_printGCodeTextBox.Text))
                {
                    MessageBox.Show("请先加载G代码文件");
                    return;
                }

                // 如果textBox2包含文件路径，则读取文件
                if (_printGCodeTextBox.Text.EndsWith(".gcode"))
                {
                    string[] lines = File.ReadAllLines(_printGCodeTextBox.Text);
                    _printGCodeLog.Lines = lines;

                    // 显示文件已加载
                    _printGCodeLog.AppendText(Environment.NewLine + "文件已加载，准备执行...");
                    return;
                }

                _executePrintButton.Enabled = false;
                try
                {
                    await _printer.ProcessNextGcodeLine();

                    // 更新显示
                    _pixelLog.AppendLineSafe($"执行G代码行 - {DateTime.Now:HH:mm:ss}");
                }
                finally
                {
                    _executePrintButton.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"出错: {ex.Message}");
                _pixelLog.AppendLineSafe($"错误: {ex.Message}");
            }
        }

        public void LoadGCodeFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    string[] lines = File.ReadAllLines(filePath);
                    _printGCodeLog.Lines = lines;
                    _printGCodeTextBox.Text = filePath;
                    _printGCodeLog.AppendText(Environment.NewLine + "G代码文件已加载");
                }
                else
                {
                    MessageBox.Show("文件不存在", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载文件时出错: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void ClearDisplays()
        {
            _sentGCodeLog.Clear();
            _followPositionLog.Clear();
            _printGCodeLog.Clear();
            _pixelLog.Clear();
        }

        public void UpdatePixelDisplay(string pixelInfo)
        {
            _pixelLog.AppendLineSafe(pixelInfo);
        }

        public void Dispose()
        {
            _startFollowButton.Click -= StartFollowButton_Click;
            _executePrintButton.Click -= ExecutePrintButton_Click;
            _processor.OnGCodeOutput -= HandleGCodeOutput;
            _processor.OnPositionUpdate -= HandlePositionUpdate;
        }
    }
}
