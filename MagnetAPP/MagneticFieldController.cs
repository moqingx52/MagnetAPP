using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MagneticFieldReader;

namespace MotorControl
{
    public sealed class MagneticFieldController : IDisposable
    {
        private static readonly TimeSpan MotorSettleDelay = TimeSpan.FromSeconds(10);
        private const double MaxDirectionErrorDegrees = 20.0;
        private const int MaxClosedLoopIterations = 4;
        private const int MaximumSensorSamples = 40;
        private const int MinimumAveragingSamples = 5;
        private const double MinimumFieldStrength = 1e-6;
        private const double MaxCorrectionPulse3200 = 150.0;

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
        private double _currentYawPulse3200 = 0.0;
        private double _currentRollPulse3200 = 0.0;
        private bool _hasCommandedAngles = false;
        private MagneticSensor? _magneticSensor;
        private readonly object _sampleSync = new();
        private readonly Queue<(double X, double Y, double Z)> _recentFieldSamples = new();

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
            AddFieldSample(x, y, z);

            _mainForm.RunOnUiThread(() => UpdateMagneticFieldDisplay(x, y, z));
        }

        private void AddFieldSample(double x, double y, double z)
        {
            if (Math.Sqrt(x * x + y * y + z * z) <= MinimumFieldStrength)
            {
                return;
            }

            lock (_sampleSync)
            {
                _recentFieldSamples.Enqueue((x, y, z));
                while (_recentFieldSamples.Count > MaximumSensorSamples)
                {
                    _recentFieldSamples.Dequeue();
                }
            }
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
            if (double.TryParse(_yawAngleTextBox.Text, out double targetYawPulse))
            {
                try
                {
                    await UpdateYawAngleAsync(targetYawPulse);
                    _angleLog.AppendLineSafe($"偏航位置设置为: {targetYawPulse:F2} pulse3200");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"偏航位置设置失败: {ex.Message}", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                MessageBox.Show("请输入有效的偏航位置数值。", "输入错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void SetRollButton_Click(object? sender, EventArgs e)
        {
            if (double.TryParse(_rollAngleTextBox.Text, out double targetRollPulse))
            {
                try
                {
                    await UpdateRollAngleAsync(targetRollPulse);
                    _angleLog.AppendLineSafe($"俯仰/滚转位置设置为: {targetRollPulse:F2} pulse3200");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"俯仰/滚转位置设置失败: {ex.Message}", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                MessageBox.Show("请输入有效的俯仰/滚转位置数值。", "输入错误",
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

        public void UpdateYawAngle(double yawPulse3200)
        {
            _ = UpdateYawAngleAsync(yawPulse3200);
        }

        public void UpdateRollAngle(double rollPulse3200)
        {
            _ = UpdateRollAngleAsync(rollPulse3200);
        }

        public async Task SetAnglesForFieldVectorAsync(double targetX, double targetY, double targetZ)
        {
            if (!CsvSearcher.TryNormalize(targetX, targetY, targetZ, out double targetDirectionX, out double targetDirectionY, out double targetDirectionZ))
            {
                throw new InvalidOperationException($"目标磁场方向无效: [{targetX},{targetY},{targetZ}]");
            }

            await InitializeCurrentPositionFromSensorIfAvailableAsync();

            MagneticFieldMapPoint? coarsePoint = await _csvSearcher.FindClosestDirectionAsync(targetX, targetY, targetZ);
            if (coarsePoint is null)
            {
                throw new InvalidOperationException($"CSV 中找不到磁场方向近似值: [{targetX},{targetY},{targetZ}]");
            }

            _mainForm.RunOnUiThread(() =>
            {
                _yawAngleTextBox.Text = coarsePoint.YawPulse3200.ToString("F2", CultureInfo.InvariantCulture);
                _rollAngleTextBox.Text = coarsePoint.RollPulse3200.ToString("F2", CultureInfo.InvariantCulture);
            });

            _angleLog.AppendLineSafe(
                $"目标磁场 [{targetX:F2},{targetY:F2},{targetZ:F2}] -> 粗定位 偏航 {coarsePoint.YawPulse3200:F2}, 俯仰/滚转 {coarsePoint.RollPulse3200:F2} pulse3200");

            await MoveToCsvPositionAsync(coarsePoint.YawPulse3200, coarsePoint.RollPulse3200);
            await RunClosedLoopCorrectionAsync(targetDirectionX, targetDirectionY, targetDirectionZ);
        }

        public async Task UpdateYawAngleAsync(double yawPulse3200)
        {
            await MoveAxisToPulseAsync(UnoMotor.Motor1, yawPulse3200, _currentYawPulse3200, value => _currentYawPulse3200 = value, "偏航");
        }

        public async Task UpdateRollAngleAsync(double rollPulse3200)
        {
            await MoveAxisToPulseAsync(UnoMotor.Motor2, rollPulse3200, _currentRollPulse3200, value => _currentRollPulse3200 = value, "俯仰/滚转");
        }

        private async Task MoveToCsvPositionAsync(double yawPulse3200, double rollPulse3200)
        {
            await UpdateYawAngleAsync(yawPulse3200);
            await UpdateRollAngleAsync(rollPulse3200);
        }

        private async Task MoveAxisToPulseAsync(
            UnoMotor motor,
            double targetPulse3200,
            double currentPulse3200,
            Action<double> updateCurrentPulse,
            string axisName)
        {
            UnoDeviceClient unoDevice = _mainForm.GetOrConnectUnoDevice(true)
                ?? throw new InvalidOperationException("UNO 未连接。");

            double delta = CsvSearcher.ShortestSignedPulseDelta(currentPulse3200, targetPulse3200);
            int steps = CsvSearcher.CsvPulseDeltaToMotorSteps(delta);
            if (steps > 0)
            {
                UnoMotorDirection direction = delta >= 0
                    ? UnoMotorDirection.Forward
                    : UnoMotorDirection.Reverse;
                await unoDevice.MoveMotorAsync(motor, direction, steps);
                _angleLog.AppendLineSafe($"{axisName}轴转动完成，等待 {MotorSettleDelay.TotalSeconds:F0}s");
                await Task.Delay(MotorSettleDelay);
            }

            double normalizedTarget = CsvSearcher.NormalizeCsvPulse(targetPulse3200);
            updateCurrentPulse(normalizedTarget);
            _hasCommandedAngles = true;
            _angleLog.AppendLineSafe(
                $"{axisName}轴移动: {currentPulse3200:F2} -> {normalizedTarget:F2} pulse3200, delta={delta:F2}, motorSteps={steps}");
        }

        private async Task InitializeCurrentPositionFromSensorIfAvailableAsync()
        {
            if (_hasCommandedAngles || !TryGetBestFieldForControl(out double x, out double y, out double z, allowInstantFallback: true))
            {
                return;
            }

            MagneticFieldMapPoint? point = await _csvSearcher.FindClosestMeasuredVectorAsync(x, y, z);
            if (point is null)
            {
                return;
            }

            _currentYawPulse3200 = CsvSearcher.NormalizeCsvPulse(point.YawPulse3200);
            _currentRollPulse3200 = CsvSearcher.NormalizeCsvPulse(point.RollPulse3200);
            _hasCommandedAngles = true;
            _angleLog.AppendLineSafe(
                $"由当前磁场估计磁铁位置: 偏航 {_currentYawPulse3200:F2}, 俯仰/滚转 {_currentRollPulse3200:F2} pulse3200");
        }

        private async Task RunClosedLoopCorrectionAsync(double targetDirectionX, double targetDirectionY, double targetDirectionZ)
        {
            for (int iteration = 1; iteration <= MaxClosedLoopIterations; iteration++)
            {
                await CaptureFreshSamplesForControlAsync();
                if (!TryGetBestFieldForControl(out double currentX, out double currentY, out double currentZ, allowInstantFallback: false))
                {
                    _angleLog.AppendLineSafe(
                        $"闭环跳过: 最近有效磁场样本少于 {MinimumAveragingSamples} 个，保留查表粗定位结果。");
                    return;
                }

                double angleError = CsvSearcher.AngleErrorDegrees(
                    currentX,
                    currentY,
                    currentZ,
                    targetDirectionX,
                    targetDirectionY,
                    targetDirectionZ);
                _angleLog.AppendLineSafe(
                    $"闭环第 {iteration} 轮: 平均磁场 [{currentX:F2},{currentY:F2},{currentZ:F2}], 方向误差 {angleError:F1}°");

                if (angleError <= MaxDirectionErrorDegrees)
                {
                    _angleLog.AppendLineSafe($"闭环到位: 方向误差 {angleError:F1}° <= {MaxDirectionErrorDegrees:F0}°");
                    return;
                }

                if (!CsvSearcher.TryNormalize(currentX, currentY, currentZ, out double currentDirectionX, out double currentDirectionY, out double currentDirectionZ))
                {
                    _angleLog.AppendLineSafe("闭环停止: 平均磁场强度过小。");
                    return;
                }

                double targetYawPulse = _currentYawPulse3200;
                double targetRollPulse = _currentRollPulse3200;
                PulseCorrection? correction = await _csvSearcher.CalculateLocalCorrectionAsync(
                    _currentYawPulse3200,
                    _currentRollPulse3200,
                    currentDirectionX,
                    currentDirectionY,
                    currentDirectionZ,
                    targetDirectionX,
                    targetDirectionY,
                    targetDirectionZ);

                if (correction is { } localCorrection)
                {
                    double yawDelta = Clamp(localCorrection.YawDeltaPulse3200, -MaxCorrectionPulse3200, MaxCorrectionPulse3200);
                    double rollDelta = Clamp(localCorrection.RollDeltaPulse3200, -MaxCorrectionPulse3200, MaxCorrectionPulse3200);
                    if (Math.Abs(yawDelta) >= 1.0 || Math.Abs(rollDelta) >= 1.0)
                    {
                        targetYawPulse = _currentYawPulse3200 + yawDelta;
                        targetRollPulse = _currentRollPulse3200 + rollDelta;
                        _angleLog.AppendLineSafe(
                            $"闭环局部修正: dYaw={yawDelta:F2}, dRoll={rollDelta:F2} pulse3200");
                    }
                    else
                    {
                        correction = null;
                    }
                }

                if (correction is null)
                {
                    MagneticFieldMapPoint? neighbor = await _csvSearcher.FindBestNeighborDirectionAsync(
                        _currentYawPulse3200,
                        _currentRollPulse3200,
                        targetDirectionX,
                        targetDirectionY,
                        targetDirectionZ);
                    if (neighbor is null)
                    {
                        _angleLog.AppendLineSafe("闭环停止: 局部修正与邻域搜索都不可用。");
                        return;
                    }

                    targetYawPulse = neighbor.YawPulse3200;
                    targetRollPulse = neighbor.RollPulse3200;
                    _angleLog.AppendLineSafe(
                        $"闭环邻域修正: 偏航 {targetYawPulse:F2}, 俯仰/滚转 {targetRollPulse:F2} pulse3200");
                }

                await MoveToCsvPositionAsync(targetYawPulse, targetRollPulse);
            }

            _angleLog.AppendLineSafe(
                $"闭环达到最大迭代次数 {MaxClosedLoopIterations}，将交给最终姿态确认。");
        }

        private async Task CaptureFreshSamplesForControlAsync()
        {
            ClearFieldSamples();
            await Task.Delay(TimeSpan.FromMilliseconds(1500));
        }

        private void ClearFieldSamples()
        {
            lock (_sampleSync)
            {
                _recentFieldSamples.Clear();
            }
        }

        private bool TryGetBestFieldForControl(
            out double x,
            out double y,
            out double z,
            bool allowInstantFallback)
        {
            (double X, double Y, double Z)[] samples;
            lock (_sampleSync)
            {
                samples = _recentFieldSamples.ToArray();
            }

            if (samples.Length >= MinimumAveragingSamples)
            {
                x = samples.Average(sample => sample.X);
                y = samples.Average(sample => sample.Y);
                z = samples.Average(sample => sample.Z);
                return Math.Sqrt(x * x + y * y + z * z) > MinimumFieldStrength;
            }

            if (allowInstantFallback && GetMagneticFieldStrength() > MinimumFieldStrength)
            {
                x = MagneticX;
                y = MagneticY;
                z = MagneticZ;
                return true;
            }

            x = y = z = 0;
            return false;
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
            return _currentYawPulse3200;
        }

        public double GetCurrentRollAngle()
        {
            return _currentRollPulse3200;
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
            bool hasAveragedField = TryGetBestFieldForControl(
                out double currentX,
                out double currentY,
                out double currentZ,
                allowInstantFallback: true);
            double currentNorm = Math.Sqrt(currentX * currentX + currentY * currentY + currentZ * currentZ);
            double targetNorm = Math.Sqrt(targetX * targetX + targetY * targetY + targetZ * targetZ);
            if (targetNorm <= 1e-9)
            {
                return true;
            }

            if (!hasAveragedField || currentNorm <= MinimumFieldStrength)
            {
                return ConfirmContinue(
                    "当前没有有效磁场传感器读数，无法确认磁铁姿态是否到位。\n是否继续执行曝光？");
            }

            double dot = (currentX * targetX + currentY * targetY + currentZ * targetZ) / (currentNorm * targetNorm);
            dot = Math.Min(1.0, Math.Max(-1.0, dot));
            double angleError = Math.Acos(dot) * 180.0 / Math.PI;
            if (angleError <= MaxDirectionErrorDegrees)
            {
                return true;
            }

            return ConfirmContinue(
                $"当前磁场方向与目标方向偏差约 {angleError:F1}°，超过 {MaxDirectionErrorDegrees:F0}°。\n" +
                $"目标: [{targetX:F2}, {targetY:F2}, {targetZ:F2}]\n" +
                $"当前: [{currentX:F2}, {currentY:F2}, {currentZ:F2}]\n" +
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

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Min(Math.Max(value, minimum), maximum);
        }
    }
}
