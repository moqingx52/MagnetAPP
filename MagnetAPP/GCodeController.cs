using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GcodeFollow;
using _3DPrint;

namespace MotorControl
{
    public sealed class GCodeController : IDisposable
    {
        private readonly MainForm _mainForm;
        private readonly KlipperController _klipperController;

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
            TextBox textBox1, Button button3, RichTextBox richTextBox2, RichTextBox richTextBox3,
            TextBox textBox2, Button button4, RichTextBox richTextBox4, RichTextBox richTextBox5)
        {
            _mainForm = mainForm;
            _klipperController = klipperController;
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
                    await _processor.ProcessGCodeFile(filePath2);
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
