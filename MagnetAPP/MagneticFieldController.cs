using System;
using System.Windows.Forms;

namespace MotorControl
{
    public sealed class MagneticFieldController : IDisposable
    {
        private readonly MainForm _mainForm;
        private readonly MotorController? _motorController;
        private readonly ArduinoCommunication? _arduinoCom;

        private readonly RichTextBox _fieldLog;
        private readonly Button _toggleReadingButton;
        private readonly Label _labelX; // X axis display
        private readonly Label _labelY; // Y axis display
        private readonly Label _labelZ; // Z axis display

        private readonly TextBox _yawAngleTextBox;
        private readonly TextBox _rollAngleTextBox;
        private readonly Button _setYawButton;
        private readonly Button _setRollButton;
        private readonly Button _toggleAngleUpdateButton;
        private readonly RichTextBox _angleLog;

        // Magnetic field properties
        public double MagneticX { get; private set; }
        public double MagneticY { get; private set; }
        public double MagneticZ { get; private set; }
        
        // Status properties
        public bool IsAngleUpdateRunning { get; private set; }
        private bool _isReadingActive = false;

        public MagneticFieldController(MainForm mainForm, RichTextBox richTextBox1, Button button2,
            Label labelX, Label labelY, Label labelZ, MotorController? motorController,
            ArduinoCommunication? arduinoCom, TextBox textBox3, TextBox textBox4,
            Button button5, Button button6, Button button7, RichTextBox richTextBox6)
        {
            _mainForm = mainForm;
            _fieldLog = richTextBox1;
            _toggleReadingButton = button2;
            _labelX = labelX;
            _labelY = labelY;
            _labelZ = labelZ;
            _motorController = motorController;
            _arduinoCom = arduinoCom;
            _yawAngleTextBox = textBox3;
            _rollAngleTextBox = textBox4;
            _setYawButton = button5;
            _setRollButton = button6;
            _toggleAngleUpdateButton = button7;
            _angleLog = richTextBox6;

            BindEvents();
        }

        private void BindEvents()
        {
            // Magnetic field reading events
            _toggleReadingButton.Click += ToggleReadingButton_Click;

            // Magnetic field orientation events
            _setYawButton.Click += SetYawButton_Click;
            _setRollButton.Click += SetRollButton_Click;
            _toggleAngleUpdateButton.Click += ToggleAngleUpdateButton_Click;
        }

        #region Magnetic Field Reading Methods

        private void ToggleReadingButton_Click(object? sender, EventArgs e)
        {
            try
            {
                if (!_isReadingActive)
                {
                    StartMagneticFieldReading();
                    _toggleReadingButton.Text = "停止读取";
                    _isReadingActive = true;
                    _fieldLog.AppendLineSafe($"开始磁场读取 - {DateTime.Now:HH:mm:ss}");
                }
                else
                {
                    StopMagneticFieldReading();
                    _toggleReadingButton.Text = "开始读取";
                    _isReadingActive = false;
                    _fieldLog.AppendLineSafe($"停止磁场读取 - {DateTime.Now:HH:mm:ss}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"磁场读取操作失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StartMagneticFieldReading()
        {
            // 实现开始磁场读取的逻辑
            // 这里需要根据具体的硬件接口实现
            UpdateMagneticFieldValues(0, 0, 0); // 示例调用
        }

        private void StopMagneticFieldReading()
        {
            // 实现停止磁场读取的逻辑
        }

        public void UpdateMagneticFieldValues(double x, double y, double z)
        {
            MagneticX = x;
            MagneticY = y;
            MagneticZ = z;

            _mainForm.RunOnUiThread(() => UpdateMagneticFieldDisplay(x, y, z));
        }

        private void UpdateMagneticFieldDisplay(double x, double y, double z)
        {
            _labelX.Text = x.ToString("F2");
            _labelY.Text = y.ToString("F2");
            _labelZ.Text = z.ToString("F2");

            string fieldInfo = $"磁场强度 - X: {x:F2}, Y: {y:F2}, Z: {z:F2}";
            _fieldLog.AppendText($"{fieldInfo}{Environment.NewLine}");
            _fieldLog.KeepLastLines(1000);
            _fieldLog.ScrollToCaret();
        }

        #endregion

        #region Magnetic Field Orientation Methods

        private void SetYawButton_Click(object? sender, EventArgs e)
        {
            if (double.TryParse(_yawAngleTextBox.Text, out double targetYaw))
            {
                UpdateYawAngle(targetYaw);
                _angleLog.AppendLineSafe($"偏航角设置为: {targetYaw:F2}°");
            }
            else
            {
                MessageBox.Show("请输入有效的偏航角数值。", "输入错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SetRollButton_Click(object? sender, EventArgs e)
        {
            if (double.TryParse(_rollAngleTextBox.Text, out double targetRoll))
            {
                UpdateRollAngle(targetRoll);
                _angleLog.AppendLineSafe($"滚转角设置为: {targetRoll:F2}°");
            }
            else
            {
                MessageBox.Show("请输入有效的滚转角数值。", "输入错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ToggleAngleUpdateButton_Click(object? sender, EventArgs e)
        {
            try
            {
                if (!IsAngleUpdateRunning)
                {
                    StartContinuousAngleUpdate();
                    _toggleAngleUpdateButton.Text = "停止更新";
                    IsAngleUpdateRunning = true;
                    _angleLog.AppendLineSafe($"开始磁场角度连续更新 - {DateTime.Now:HH:mm:ss}");
                }
                else
                {
                    StopContinuousAngleUpdate();
                    _toggleAngleUpdateButton.Text = "开始更新";
                    IsAngleUpdateRunning = false;
                    _angleLog.AppendLineSafe($"停止磁场角度更新 - {DateTime.Now:HH:mm:ss}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"操作失败: {ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _angleLog.AppendLineSafe($"错误: {ex.Message}");
            }
        }

        public void UpdateYawAngle(double yawAngle)
        {
            // 实现偏航角更新逻辑
            // 这里需要根据具体的硬件接口实现
        }

        public void UpdateRollAngle(double rollAngle)
        {
            // 实现滚转角更新逻辑
            // 这里需要根据具体的硬件接口实现
        }

        public void StartContinuousAngleUpdate()
        {
            // 实现连续角度更新逻辑
            IsAngleUpdateRunning = true;
        }

        public void StopContinuousAngleUpdate()
        {
            // 实现停止连续角度更新逻辑
            IsAngleUpdateRunning = false;
        }

        public void UpdateMagneticAngles()
        {
            try
            {
                // 手动更新磁场角度
                // 这里需要根据具体的硬件接口实现
                _angleLog.AppendLineSafe($"手动更新磁场角度 - {DateTime.Now:HH:mm:ss}");
            }
            catch (Exception ex)
            {
                _angleLog.AppendLineSafe($"手动更新失败: {ex.Message}");
            }
        }

        public void UpdateMagneticFieldAngleDisplay(double yaw, double pitch, double roll)
        {
            _mainForm.RunOnUiThread(() => UpdateMagneticFieldAngleDisplayInternal(yaw, pitch, roll));
        }

        private void UpdateMagneticFieldAngleDisplayInternal(double yaw, double pitch, double roll)
        {
            string angleInfo = $"磁场角度 - 偏航: {yaw:F2}°, 俯仰: {pitch:F2}°, 滚转: {roll:F2}°";
            _angleLog.AppendText($"{angleInfo}{Environment.NewLine}");

            _angleLog.KeepLastLines(1000);
            _angleLog.ScrollToCaret();
        }

        #endregion

        #region Public Methods

        public void ControlMagnetPosition(double yaw, double pitch, double roll)
        {
            try
            {
                // 实现磁铁位置控制逻辑
                _angleLog.AppendLineSafe($"磁铁位置设置: 偏航{yaw:F2}° 俯仰{pitch:F2}° 滚转{roll:F2}°");
            }
            catch (Exception ex)
            {
                _angleLog.AppendLineSafe($"设置磁铁位置失败: {ex.Message}");
            }
        }

        public double GetCurrentYawAngle()
        {
            // 返回当前偏航角
            return 0.0; // 需要根据实际实现返回真实值
        }

        public double GetCurrentRollAngle()
        {
            // 返回当前滚转角
            return 0.0; // 需要根据实际实现返回真实值
        }

        public (double X, double Y, double Z) GetCurrentMagneticField()
        {
            return (MagneticX, MagneticY, MagneticZ);
        }

        public double GetMagneticFieldStrength()
        {
            return Math.Sqrt(MagneticX * MagneticX + MagneticY * MagneticY + MagneticZ * MagneticZ);
        }

        public void ClearMagneticFieldDisplay()
        {
            _mainForm.RunOnUiThread(() => _fieldLog.Clear());
        }

        public void ClearAngleDisplay()
        {
            _mainForm.RunOnUiThread(() => _angleLog.Clear());
        }

        #endregion

        public void Dispose()
        {
            // 停止所有运行中的操作
            if (IsAngleUpdateRunning)
            {
                StopContinuousAngleUpdate();
            }
            
            if (_isReadingActive)
            {
                StopMagneticFieldReading();
            }

            _toggleReadingButton.Click -= ToggleReadingButton_Click;
            _setYawButton.Click -= SetYawButton_Click;
            _setRollButton.Click -= SetRollButton_Click;
            _toggleAngleUpdateButton.Click -= ToggleAngleUpdateButton_Click;
        }
    }
}
