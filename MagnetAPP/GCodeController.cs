using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using DLP;

namespace MotorControl
{
    public sealed class GCodeController : IDisposable
    {
        private readonly KlipperController _klipperController;
        private readonly MagneticFieldController? _magneticFieldController;

        private readonly TextBox _printGCodeTextBox;
        private readonly Button _executePrintButton;
        private readonly RichTextBox _printGCodeLog;
        private readonly RichTextBox _pixelLog;

        public GCodeController(MainForm mainForm, KlipperController klipperController,
            MagneticFieldController? magneticFieldController,
            TextBox textBox2, Button button4, RichTextBox richTextBox4, RichTextBox richTextBox5)
        {
            _ = mainForm;
            _klipperController = klipperController;
            _magneticFieldController = magneticFieldController;
            _printGCodeTextBox = textBox2;
            _executePrintButton = button4;
            _printGCodeLog = richTextBox4;
            _pixelLog = richTextBox5;

            BindEvents();
        }

        private void BindEvents()
        {
            _executePrintButton.Click += ExecutePrintButton_Click;
        }

        private async Task ProcessMagneticVoxelGCodeFileAsync(
            string filePath,
            RichTextBox sentGCodeLog,
            RichTextBox positionLog)
        {
            string[] lines = await File.ReadAllLinesAsync(filePath);
            await ProcessMagneticVoxelGCodeLinesAsync(lines, sentGCodeLog, positionLog);
        }

        private async Task ProcessMagneticVoxelGCodeLinesAsync(
            string[] lines,
            RichTextBox sentGCodeLog,
            RichTextBox positionLog)
        {
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

                    UpdateWhiteSquareFromGCode(command, positionLog);
                    sentGCodeLog.AppendLineSafe(command);

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

        private void UpdateWhiteSquareFromGCode(string gcode, RichTextBox positionLog)
        {
            if (!TryExtractXy(gcode, out double x, out double y))
            {
                return;
            }

            (int pixelX, int pixelY) = DisplayAlignmentSettings.RobotMillimetersToDisplayPixel(x, y);

            positionLog.RunOnUiThread(() =>
                positionLog.Text =
                    $"currentX: {pixelX}, currentY: {pixelY}, offsetX: {DisplayAlignmentSettings.OffsetXMillimeters:F2}mm, offsetY: {DisplayAlignmentSettings.OffsetYMillimeters:F2}mm{Environment.NewLine}");

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

                _executePrintButton.Enabled = false;
                try
                {
                    string pathOrInlineGCode = _printGCodeTextBox.Text.Trim();
                    if (File.Exists(pathOrInlineGCode))
                    {
                        await ProcessMagneticVoxelGCodeFileAsync(pathOrInlineGCode, _printGCodeLog, _pixelLog);
                    }
                    else
                    {
                        await ProcessMagneticVoxelGCodeLinesAsync(
                            _printGCodeTextBox.Lines,
                            _printGCodeLog,
                            _pixelLog);
                    }

                    _pixelLog.AppendLineSafe($"GCODE执行完成 - {DateTime.Now:HH:mm:ss}");
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
            _printGCodeLog.Clear();
            _pixelLog.Clear();
        }

        public void UpdatePixelDisplay(string pixelInfo)
        {
            _pixelLog.AppendLineSafe(pixelInfo);
        }

        public void Dispose()
        {
            _executePrintButton.Click -= ExecutePrintButton_Click;
        }
    }
}
