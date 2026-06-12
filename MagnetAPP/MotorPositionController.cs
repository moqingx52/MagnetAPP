using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using log4net;

namespace MotorControl
{
    public sealed class MotorPositionController : IDisposable
    {
        private readonly MainForm _mainForm;
        private readonly KlipperController _klipperController;
        private readonly ILog _log = LogManager.GetLogger(typeof(MotorPositionController));

        private readonly TextBox _xPositionTextBox;
        private readonly TextBox _yPositionTextBox;
        private readonly TextBox _zPositionTextBox;
        private readonly TextBox _alignmentRobotXTextBox;
        private readonly TextBox _alignmentRobotYTextBox;
        private readonly TextBox _alignmentResultTextBox;
        private readonly Button _moveXButton;
        private readonly Button _moveYButton;
        private readonly Button _moveZButton;
        private readonly Button _calculateAlignmentButton;
        private readonly Label _currentXLabel;
        private readonly Label _currentYLabel;
        private readonly Label _currentZLabel;

        // Position tracking
        private double _currentX = 0;
        private double _currentY = 0;
        private double _currentZ = 0;

        private readonly TextBox _displayStartXTextBox;
        private readonly TextBox _displayEndXTextBox;
        private readonly TextBox _displayStartYTextBox;
        private readonly TextBox _displayEndYTextBox;

        public double CurrentX => _currentX;
        public double CurrentY => _currentY;
        public double CurrentZ => _currentZ;

        public MotorPositionController(MainForm mainForm, KlipperController klipperController,
            TextBox textBox5, TextBox textBox6, TextBox textBox7, TextBox textBox9, TextBox textBox10,
            TextBox textBox8, Button button8, Button button9, Button button10, Button button11,
            Label label19, Label label20, Label label21, TextBox txtX1, TextBox txtX2,
            TextBox txtY1, TextBox txtY2)
        {
            _mainForm = mainForm;
            _klipperController = klipperController;
            _xPositionTextBox = textBox5;
            _yPositionTextBox = textBox6;
            _zPositionTextBox = textBox7;
            _alignmentRobotXTextBox = textBox9;
            _alignmentRobotYTextBox = textBox10;
            _alignmentResultTextBox = textBox8;
            _moveXButton = button8;
            _moveYButton = button9;
            _moveZButton = button10;
            _calculateAlignmentButton = button11;
            _currentXLabel = label19;
            _currentYLabel = label20;
            _currentZLabel = label21;
            _displayStartXTextBox = txtX1;
            _displayEndXTextBox = txtX2;
            _displayStartYTextBox = txtY1;
            _displayEndYTextBox = txtY2;

            InitializeControls();
            UpdatePositionLabels();
        }

        private void InitializeControls()
        {
            _moveXButton.Click += MoveXButton_Click;
            _moveYButton.Click += MoveYButton_Click;
            _moveZButton.Click += MoveZButton_Click;
            _calculateAlignmentButton.Click += CalculateAlignmentButton_Click;
        }

        private async void MoveXButton_Click(object? sender, EventArgs e)
        {
            await MoveAxisAsync("X", _xPositionTextBox, _moveXButton, 0, 100, value => _currentX = value);
        }

        private async void MoveYButton_Click(object? sender, EventArgs e)
        {
            await MoveAxisAsync("Y", _yPositionTextBox, _moveYButton, 0, 50, value => _currentY = value);
        }

        private async void MoveZButton_Click(object? sender, EventArgs e)
        {
            await MoveAxisAsync("Z", _zPositionTextBox, _moveZButton, 0, 50, value => _currentZ = value);
        }

        private async Task MoveAxisAsync(
            string axis,
            TextBox positionTextBox,
            Button moveButton,
            double minimum,
            double maximum,
            Action<double> updateCurrentPosition)
        {
            try
            {
                if (!double.TryParse(positionTextBox.Text, out double targetPosition))
                {
                    MessageBox.Show($"Please enter a valid {axis} position", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (targetPosition < minimum || targetPosition > maximum)
                {
                    MessageBox.Show($"{axis} position must be between {minimum} and {maximum}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                moveButton.Enabled = false;
                string command = $"G1 {axis}{targetPosition:F3} F800";

                if (await _klipperController.SendCommandAsync(command))
                {
                    updateCurrentPosition(targetPosition);
                    UpdatePositionLabels();
                }
            }
            catch (Exception ex)
            {
                _log.Error($"{axis} movement error: {ex}");
                MessageBox.Show($"Error moving {axis} axis: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                moveButton.Enabled = true;
            }
        }

        private void CalculateAlignmentButton_Click(object? sender, EventArgs e)
        {
            try
            {
                int x1 = Convert.ToInt32(_displayStartXTextBox.Text);
                int x2 = Convert.ToInt32(_displayEndXTextBox.Text);
                int y1 = Convert.ToInt32(_displayStartYTextBox.Text);
                int y2 = Convert.ToInt32(_displayEndYTextBox.Text);

                double robotX = Convert.ToDouble(_alignmentRobotXTextBox.Text);
                double robotY = Convert.ToDouble(_alignmentRobotYTextBox.Text);

                double centerX = (x1 + x2) / 2.0;
                double centerY = (y1 + y2) / 2.0;

                double offsetX = robotX - centerX * 0.072;
                double offsetY = robotY - centerY * 0.072;

                _alignmentResultTextBox.Text = $"原点在x方向偏移了{offsetX:F2}, y方向偏移了{offsetY:F2}";
            }
            catch (FormatException)
            {
                MessageBox.Show("请输入有效的数字!", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdatePositionLabels()
        {
            _mainForm.RunOnUiThread(() =>
            {
                _currentXLabel.Text = _currentX.ToString("F2");
                _currentYLabel.Text = _currentY.ToString("F2");
                _currentZLabel.Text = _currentZ.ToString("F2");
            });
        }

        public void SetCurrentPosition(double x, double y, double z)
        {
            _currentX = x;
            _currentY = y;
            _currentZ = z;
            UpdatePositionLabels();
        }

        public void Dispose()
        {
            _moveXButton.Click -= MoveXButton_Click;
            _moveYButton.Click -= MoveYButton_Click;
            _moveZButton.Click -= MoveZButton_Click;
            _calculateAlignmentButton.Click -= CalculateAlignmentButton_Click;
        }
    }
}
