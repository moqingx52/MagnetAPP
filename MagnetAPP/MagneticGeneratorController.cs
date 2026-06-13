using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using log4net;

namespace MotorControl
{
    public sealed class MagneticGeneratorController : IDisposable
    {
        private const double ConstantSpeedStepRadPerSec = 0.1;
        private const double DefaultConstantSpeedRadPerSec = 0.5;
        private const int FormulaDebounceMilliseconds = 300;
        private const int FormulaTimerIntervalMilliseconds = 50;

        private readonly MainForm _mainForm;
        private readonly Func<bool, Rs485SpeedMotorClient?> _getRs485Client;
        private readonly Button _forwardButton;
        private readonly Button _backwardButton;
        private readonly Button _stopButton;
        private readonly Button _increaseSpeedButton;
        private readonly Button _decreaseSpeedButton;
        private readonly TextBox _constantSpeedTextBox;
        private readonly TextBox _formulaTextBox;
        private readonly ILog _log = LogManager.GetLogger(typeof(MagneticGeneratorController));

        private readonly System.Windows.Forms.Timer _formulaTimer;
        private CancellationTokenSource? _formulaDebounceCts;

        private bool _userStopped = true;
        private bool _linearActive;
        private double _lastValidConstantSpeed = DefaultConstantSpeedRadPerSec;
        private MagnetSpeedFormulaEvaluator? _formulaEvaluator;
        private DateTime _formulaStartUtc;

        public MagneticGeneratorController(
            MainForm mainForm,
            Func<bool, Rs485SpeedMotorClient?> getRs485Client,
            Button forwardButton,
            Button backwardButton,
            Button stopButton,
            Button increaseSpeedButton,
            Button decreaseSpeedButton,
            TextBox constantSpeedTextBox,
            TextBox formulaTextBox)
        {
            _mainForm = mainForm;
            _getRs485Client = getRs485Client;
            _forwardButton = forwardButton;
            _backwardButton = backwardButton;
            _stopButton = stopButton;
            _increaseSpeedButton = increaseSpeedButton;
            _decreaseSpeedButton = decreaseSpeedButton;
            _constantSpeedTextBox = constantSpeedTextBox;
            _formulaTextBox = formulaTextBox;

            _formulaTimer = new System.Windows.Forms.Timer
            {
                Interval = FormulaTimerIntervalMilliseconds
            };
            _formulaTimer.Tick += FormulaTimer_Tick;

            InitializeUi();
            BindEvents();
        }

        private void InitializeUi()
        {
            _constantSpeedTextBox.Text = DefaultConstantSpeedRadPerSec.ToString("0.###", CultureInfo.InvariantCulture);
            _lastValidConstantSpeed = DefaultConstantSpeedRadPerSec;
        }

        private void BindEvents()
        {
            _forwardButton.MouseDown += ForwardButton_MouseDown;
            _forwardButton.MouseUp += LinearButton_MouseUp;
            _forwardButton.MouseLeave += LinearButton_MouseLeave;

            _backwardButton.MouseDown += BackwardButton_MouseDown;
            _backwardButton.MouseUp += LinearButton_MouseUp;
            _backwardButton.MouseLeave += LinearButton_MouseLeave;

            _stopButton.Click += StopButton_Click;
            _increaseSpeedButton.Click += IncreaseSpeedButton_Click;
            _decreaseSpeedButton.Click += DecreaseSpeedButton_Click;
            _constantSpeedTextBox.Validating += ConstantSpeedTextBox_Validating;
            _constantSpeedTextBox.KeyDown += ConstantSpeedTextBox_KeyDown;
            _formulaTextBox.TextChanged += FormulaTextBox_TextChanged;
        }

        private void ForwardButton_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            TryExecute(showConnectionErrors: true, client =>
            {
                client.SendLinearForward();
                _linearActive = true;
            }, "前进");
        }

        private void BackwardButton_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            TryExecute(showConnectionErrors: true, client =>
            {
                client.SendLinearBackward();
                _linearActive = true;
            }, "后退");
        }

        private void LinearButton_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            StopLinearOnly(showConnectionErrors: false);
        }

        private void LinearButton_MouseLeave(object? sender, EventArgs e)
        {
            if (_linearActive)
            {
                StopLinearOnly(showConnectionErrors: false);
            }
        }

        private void StopButton_Click(object? sender, EventArgs e)
        {
            StopAll(showConnectionErrors: true);
        }

        private void IncreaseSpeedButton_Click(object? sender, EventArgs e)
        {
            AdjustConstantSpeed(ConstantSpeedStepRadPerSec);
        }

        private void DecreaseSpeedButton_Click(object? sender, EventArgs e)
        {
            AdjustConstantSpeed(-ConstantSpeedStepRadPerSec);
        }

        private void ConstantSpeedTextBox_Validating(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!TryApplyConstantSpeedFromTextBox(showConnectionErrors: true, out _))
            {
                _constantSpeedTextBox.Text = _lastValidConstantSpeed.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }

        private void ConstantSpeedTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (!TryApplyConstantSpeedFromTextBox(showConnectionErrors: true, out _))
                {
                    _constantSpeedTextBox.Text = _lastValidConstantSpeed.ToString("0.###", CultureInfo.InvariantCulture);
                }
            }
        }

        private void FormulaTextBox_TextChanged(object? sender, EventArgs e)
        {
            _formulaDebounceCts?.Cancel();
            _formulaDebounceCts?.Dispose();
            _formulaDebounceCts = new CancellationTokenSource();
            CancellationToken token = _formulaDebounceCts.Token;
            _ = DebounceFormulaUpdateAsync(token);
        }

        private async System.Threading.Tasks.Task DebounceFormulaUpdateAsync(CancellationToken cancellationToken)
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(FormulaDebounceMilliseconds, cancellationToken);
                _mainForm.RunOnUiThread(() => UpdateFormulaMode(showConnectionErrors: true));
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void FormulaTimer_Tick(object? sender, EventArgs e)
        {
            if (_formulaEvaluator == null || _userStopped)
            {
                return;
            }

            TryExecute(showConnectionErrors: false, client =>
            {
                double t = (DateTime.UtcNow - _formulaStartUtc).TotalSeconds;
                double omega = _formulaEvaluator!.EvaluateAt(t);
                client.SetRotationSpeedRadPerSec(omega);
            }, "公式转速");
        }

        private void AdjustConstantSpeed(double delta)
        {
            double nextSpeed = Math.Clamp(
                _lastValidConstantSpeed + delta,
                -MagnetSpeedFormulaEvaluator.MaxSpeedRadPerSec,
                MagnetSpeedFormulaEvaluator.MaxSpeedRadPerSec);

            _lastValidConstantSpeed = nextSpeed;
            _constantSpeedTextBox.Text = nextSpeed.ToString("0.###", CultureInfo.InvariantCulture);
            ApplyConstantRotationSpeed(showConnectionErrors: true);
        }

        private bool TryApplyConstantSpeedFromTextBox(bool showConnectionErrors, out double speed)
        {
            speed = _lastValidConstantSpeed;
            if (!double.TryParse(_constantSpeedTextBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out speed) &&
                !double.TryParse(_constantSpeedTextBox.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out speed))
            {
                ShowError("请输入有效的转速数值。");
                return false;
            }

            speed = Math.Clamp(speed, -MagnetSpeedFormulaEvaluator.MaxSpeedRadPerSec, MagnetSpeedFormulaEvaluator.MaxSpeedRadPerSec);
            _lastValidConstantSpeed = speed;
            _constantSpeedTextBox.Text = speed.ToString("0.###", CultureInfo.InvariantCulture);
            ApplyConstantRotationSpeed(showConnectionErrors);
            return true;
        }

        private void ApplyConstantRotationSpeed(bool showConnectionErrors)
        {
            if (IsFormulaModeActive())
            {
                return;
            }

            _userStopped = false;
            TryExecute(showConnectionErrors, client =>
            {
                client.SetRotationSpeedRadPerSec(_lastValidConstantSpeed);
            }, "设置常数转速");
        }

        private void UpdateFormulaMode(bool showConnectionErrors)
        {
            string formula = _formulaTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(formula))
            {
                StopFormulaTimer();
                _formulaEvaluator = null;

                if (!_userStopped)
                {
                    ApplyConstantRotationSpeed(showConnectionErrors);
                }

                return;
            }

            if (!MagnetSpeedFormulaEvaluator.TryCreate(formula, out MagnetSpeedFormulaEvaluator? evaluator, out string error))
            {
                StopFormulaTimer();
                _formulaEvaluator = null;
                ShowError($"公式无效：{error}");
                return;
            }

            _formulaEvaluator = evaluator;
            _formulaStartUtc = DateTime.UtcNow;
            _userStopped = false;
            _formulaTimer.Start();

            TryExecute(showConnectionErrors, client =>
            {
                double omega = _formulaEvaluator!.EvaluateAt(0);
                client.SetRotationSpeedRadPerSec(omega);
            }, "启动公式转速");
        }

        private bool IsFormulaModeActive()
        {
            return !string.IsNullOrWhiteSpace(_formulaTextBox.Text.Trim()) && _formulaEvaluator != null && _formulaTimer.Enabled;
        }

        private void StopFormulaTimer()
        {
            _formulaTimer.Stop();
        }

        private void StopLinearOnly(bool showConnectionErrors)
        {
            if (!_linearActive)
            {
                return;
            }

            TryExecute(showConnectionErrors, client => client.StopLinear(), "停止移动");
            _linearActive = false;
        }

        public void StopAll(bool showConnectionErrors)
        {
            StopFormulaTimer();
            _formulaEvaluator = null;
            _linearActive = false;
            _userStopped = true;

            TryExecute(showConnectionErrors, client => client.StopAll(), "停止");
        }

        private void TryExecute(bool showConnectionErrors, Action<Rs485SpeedMotorClient> action, string actionName)
        {
            try
            {
                Rs485SpeedMotorClient? client = _getRs485Client(showConnectionErrors);
                if (client == null)
                {
                    return;
                }

                action(client);
            }
            catch (Exception ex)
            {
                _log.Error($"{actionName} failed: {ex.Message}", ex);
                Debug.WriteLine($"{actionName} failed: {ex.Message}");
                if (showConnectionErrors)
                {
                    ShowError($"{actionName}失败：{ex.Message}");
                }
            }
        }

        private void ShowError(string message)
        {
            _mainForm.RunOnUiThread(() =>
                MessageBox.Show(_mainForm, message, "磁场发生器", MessageBoxButtons.OK, MessageBoxIcon.Warning));
        }

        public void Dispose()
        {
            _formulaDebounceCts?.Cancel();
            _formulaDebounceCts?.Dispose();
            _formulaDebounceCts = null;

            _formulaTimer.Stop();
            _formulaTimer.Tick -= FormulaTimer_Tick;
            _formulaTimer.Dispose();

            _forwardButton.MouseDown -= ForwardButton_MouseDown;
            _forwardButton.MouseUp -= LinearButton_MouseUp;
            _forwardButton.MouseLeave -= LinearButton_MouseLeave;

            _backwardButton.MouseDown -= BackwardButton_MouseDown;
            _backwardButton.MouseUp -= LinearButton_MouseUp;
            _backwardButton.MouseLeave -= LinearButton_MouseLeave;

            _stopButton.Click -= StopButton_Click;
            _increaseSpeedButton.Click -= IncreaseSpeedButton_Click;
            _decreaseSpeedButton.Click -= DecreaseSpeedButton_Click;
            _constantSpeedTextBox.Validating -= ConstantSpeedTextBox_Validating;
            _constantSpeedTextBox.KeyDown -= ConstantSpeedTextBox_KeyDown;
            _formulaTextBox.TextChanged -= FormulaTextBox_TextChanged;

            try
            {
                StopAll(showConnectionErrors: false);
            }
            catch
            {
            }
        }
    }
}
