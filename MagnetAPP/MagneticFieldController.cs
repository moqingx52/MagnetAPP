using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using MagneticFieldReader;

namespace MotorControl
{
    public sealed class MagneticFieldController : IDisposable
    {
        private static readonly TimeSpan MotorSettleDelay = TimeSpan.FromSeconds(10);

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
        private readonly CsvSearcher _csvSearcher = new();
        private double _currentYawAngle = 0.0;
        private double _currentRollAngle = 0.0;
        private bool _hasCommandedAngles = false;
        private MagneticSensor? _magneticSensor;

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
            string? portName = _mainForm.MagneticFieldPort;
            if (string.IsNullOrWhiteSpace(portName))
            {
                throw new InvalidOperationException("请先在磁场传感器下拉列表中选择串口。");
            }

            _magneticSensor?.Dispose();
            _magneticSensor = new MagneticSensor(portName);
            _magneticSensor.OnDataReceived += MagneticSensor_OnDataReceived;
            _magneticSensor.OnError += MagneticSensor_OnError;

            if (!_magneticSensor.StartReading())
            {
                throw new InvalidOperationException($"无法启动磁场传感器读取: {portName}");
            }
        }

        private void StopMagneticFieldReading()
        {
            if (_magneticSensor is null)
            {
                return;
            }

            _magneticSensor.OnDataReceived -= MagneticSensor_OnDataReceived;
            _magneticSensor.OnError -= MagneticSensor_OnError;
            _magneticSensor.Dispose();
            _magneticSensor = null;
        }

        private void MagneticSensor_OnDataReceived(object? sender, MagneticDataEventArgs e)
        {
            UpdateMagneticFieldValues(e.X, e.Y, e.Z);
        }

        private void MagneticSensor_OnError(object? sender, ErrorEventArgs e)
        {
            _fieldLog.AppendLineSafe($"磁场传感器错误: {e.GetException().Message}");
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

        private async void SetYawButton_Click(object? sender, EventArgs e)
        {
            if (double.TryParse(_yawAngleTextBox.Text, out double targetYaw))
            {
                try
                {
                    await UpdateYawAngleAsync(targetYaw);
                    _angleLog.AppendLineSafe($"偏航角设置为: {targetYaw:F2}°");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"偏航角设置失败: {ex.Message}", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                MessageBox.Show("请输入有效的偏航角数值。", "输入错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void SetRollButton_Click(object? sender, EventArgs e)
        {
            if (double.TryParse(_rollAngleTextBox.Text, out double targetRoll))
            {
                try
                {
                    await UpdateRollAngleAsync(targetRoll);
                    _angleLog.AppendLineSafe($"滚转角设置为: {targetRoll:F2}°");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"滚转角设置失败: {ex.Message}", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
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
            _ = UpdateYawAngleAsync(yawAngle);
        }

        public void UpdateRollAngle(double rollAngle)
        {
            _ = UpdateRollAngleAsync(rollAngle);
        }

        public async Task SetAnglesForFieldVectorAsync(double targetX, double targetY, double targetZ)
        {
            await InitializeCurrentAnglesFromSensorIfAvailableAsync();

            SearchResultEventArgs? result = await _csvSearcher.SearchInCsvAsync(
                FormatNumber(targetX),
                FormatNumber(targetY),
                FormatNumber(targetZ));
            if (result is null)
            {
                throw new InvalidOperationException($"CSV 中找不到磁场方向近似值: [{targetX},{targetY},{targetZ}]");
            }

            double yaw = ParseAngle(result.ResultColumn1);
            double roll = ParseAngle(result.ResultColumn2);

            _mainForm.RunOnUiThread(() =>
            {
                _yawAngleTextBox.Text = yaw.ToString("F2", CultureInfo.InvariantCulture);
                _rollAngleTextBox.Text = roll.ToString("F2", CultureInfo.InvariantCulture);
            });

            await UpdateYawAngleAsync(yaw);
            await UpdateRollAngleAsync(roll);
            _angleLog.AppendLineSafe(
                $"目标磁场 [{targetX:F2},{targetY:F2},{targetZ:F2}] -> 偏航 {yaw:F2}°, 滚转 {roll:F2}°");
        }

        public async Task UpdateYawAngleAsync(double yawAngle)
        {
            await MoveAngleAxisAsync(UnoMotor.Motor1, yawAngle, _currentYawAngle, value => _currentYawAngle = value, "偏航");
        }

        public async Task UpdateRollAngleAsync(double rollAngle)
        {
            await MoveAngleAxisAsync(UnoMotor.Motor2, rollAngle, _currentRollAngle, value => _currentRollAngle = value, "滚转");
        }

        private async Task MoveAngleAxisAsync(
            UnoMotor motor,
            double targetAngle,
            double currentAngle,
            Action<double> updateCurrentAngle,
            string axisName)
        {
            UnoDeviceClient unoDevice = _mainForm.GetOrConnectUnoDevice(true)
                ?? throw new InvalidOperationException("UNO 未连接。");

            double delta = ShortestSignedDelta(currentAngle, targetAngle);
            int steps = (int)Math.Round(Math.Abs(delta) / 360.0 * MotorController.STEPS_PER_REVOLUTION);
            if (steps > 0)
            {
                UnoMotorDirection direction = delta >= 0
                    ? UnoMotorDirection.Forward
                    : UnoMotorDirection.Reverse;
                await unoDevice.MoveMotorAsync(motor, direction, steps);
                _angleLog.AppendLineSafe($"{axisName}轴转动完成，等待 {MotorSettleDelay.TotalSeconds:F0}s");
                await Task.Delay(MotorSettleDelay);
            }

            double normalizedTarget = NormalizeAngle(targetAngle);
            updateCurrentAngle(normalizedTarget);
            _hasCommandedAngles = true;
            _angleLog.AppendLineSafe($"{axisName}轴移动: {currentAngle:F2}° -> {normalizedTarget:F2}°, steps={steps}");
        }

        private async Task InitializeCurrentAnglesFromSensorIfAvailableAsync()
        {
            if (_hasCommandedAngles || GetMagneticFieldStrength() <= 1e-6)
            {
                return;
            }

            SearchResultEventArgs? result = await _csvSearcher.SearchInCsvAsync(
                FormatNumber(MagneticX),
                FormatNumber(MagneticY),
                FormatNumber(MagneticZ));
            if (result is null)
            {
                return;
            }

            _currentYawAngle = NormalizeAngle(ParseAngle(result.ResultColumn1));
            _currentRollAngle = NormalizeAngle(ParseAngle(result.ResultColumn2));
            _hasCommandedAngles = true;
            _angleLog.AppendLineSafe($"由当前磁场估计磁铁角度: 偏航 {_currentYawAngle:F2}°, 滚转 {_currentRollAngle:F2}°");
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
            return _currentYawAngle;
        }

        public double GetCurrentRollAngle()
        {
            return _currentRollAngle;
        }

        public (double X, double Y, double Z) GetCurrentMagneticField()
        {
            return (MagneticX, MagneticY, MagneticZ);
        }

        public double GetMagneticFieldStrength()
        {
            return Math.Sqrt(MagneticX * MagneticX + MagneticY * MagneticY + MagneticZ * MagneticZ);
        }

        public bool ConfirmTargetFieldIfNeeded(double targetX, double targetY, double targetZ)
        {
            const double maxDirectionErrorDegrees = 20.0;

            double currentNorm = GetMagneticFieldStrength();
            double targetNorm = Math.Sqrt(targetX * targetX + targetY * targetY + targetZ * targetZ);
            if (targetNorm <= 1e-9)
            {
                return true;
            }

            if (currentNorm <= 1e-6)
            {
                return ConfirmContinue(
                    "当前没有有效磁场传感器读数，无法确认磁铁姿态是否到位。\n是否继续执行曝光？");
            }

            double dot = (MagneticX * targetX + MagneticY * targetY + MagneticZ * targetZ) / (currentNorm * targetNorm);
            dot = Math.Min(1.0, Math.Max(-1.0, dot));
            double angleError = Math.Acos(dot) * 180.0 / Math.PI;
            if (angleError <= maxDirectionErrorDegrees)
            {
                return true;
            }

            return ConfirmContinue(
                $"当前磁场方向与目标方向偏差约 {angleError:F1}°，超过 {maxDirectionErrorDegrees:F0}°。\n" +
                $"目标: [{targetX:F2}, {targetY:F2}, {targetZ:F2}]\n" +
                $"当前: [{MagneticX:F2}, {MagneticY:F2}, {MagneticZ:F2}]\n" +
                "是否继续执行曝光？");
        }

        private bool ConfirmContinue(string message)
        {
            DialogResult result = DialogResult.No;
            _mainForm.RunOnUiThread(() =>
            {
                result = MessageBox.Show(
                    _mainForm,
                    message,
                    "磁场姿态确认",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
            });
            return result == DialogResult.Yes;
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

        private static string FormatNumber(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        private static double ParseAngle(string value)
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
                || double.TryParse(value, out result))
            {
                return result;
            }

            throw new InvalidOperationException($"角度解析失败: {value}");
        }

        private static double NormalizeAngle(double angle)
        {
            double normalized = angle % 360.0;
            return normalized < 0 ? normalized + 360.0 : normalized;
        }

        private static double ShortestSignedDelta(double currentAngle, double targetAngle)
        {
            double delta = NormalizeAngle(targetAngle) - NormalizeAngle(currentAngle);
            if (delta > 180.0)
            {
                delta -= 360.0;
            }
            else if (delta < -180.0)
            {
                delta += 360.0;
            }

            return delta;
        }
    }
}
