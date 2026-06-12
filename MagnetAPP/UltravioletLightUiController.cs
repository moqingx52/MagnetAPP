using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using log4net;

namespace MotorControl
{
    public sealed class UltravioletLightUiController : IDisposable
    {
        private const int SliderDebounceMilliseconds = 150;
        private const int DefaultBrightnessPercent = 50;

        private readonly MainForm _mainForm;
        private readonly Func<bool, UnoDeviceClient?> _getUnoDevice;
        private readonly TrackBar _brightnessBar;
        private readonly Button _turnOnButton;
        private readonly Button _turnOffButton;
        private readonly Label _statusLabel;
        private readonly ILog _log = LogManager.GetLogger(typeof(UltravioletLightUiController));

        private bool _isLightOn;
        private CancellationTokenSource? _sliderDebounceCts;

        public UltravioletLightUiController(
            MainForm mainForm,
            Func<bool, UnoDeviceClient?> getUnoDevice,
            TrackBar brightnessBar,
            Button turnOnButton,
            Button turnOffButton,
            Label statusLabel)
        {
            _mainForm = mainForm;
            _getUnoDevice = getUnoDevice;
            _brightnessBar = brightnessBar;
            _turnOnButton = turnOnButton;
            _turnOffButton = turnOffButton;
            _statusLabel = statusLabel;

            InitializeUi();
            BindEvents();
        }

        private void InitializeUi()
        {
            _brightnessBar.Minimum = UnoDeviceProtocol.MinimumBrightnessPercent;
            _brightnessBar.Maximum = UnoDeviceProtocol.MaximumBrightnessPercent;
            _brightnessBar.Value = DefaultBrightnessPercent;
            _statusLabel.Text = "灯：关";
        }

        private void BindEvents()
        {
            _turnOnButton.Click += TurnOnButton_Click;
            _turnOffButton.Click += TurnOffButton_Click;
            _brightnessBar.ValueChanged += BrightnessBar_ValueChanged;
        }

        private async void TurnOnButton_Click(object? sender, EventArgs e)
        {
            await ExecuteLightActionAsync(
                async light =>
                {
                    await light.SetBrightnessPercentAsync(_brightnessBar.Value);
                    _isLightOn = true;
                    UpdateStatusLabel(true);
                },
                "开灯",
                showConnectionErrors: true);
        }

        private async void TurnOffButton_Click(object? sender, EventArgs e)
        {
            await ExecuteLightActionAsync(
                async light =>
                {
                    await light.TurnOffAsync();
                    _isLightOn = false;
                    UpdateStatusLabel(false);
                },
                "关灯",
                showConnectionErrors: true);
        }

        private void BrightnessBar_ValueChanged(object? sender, EventArgs e)
        {
            if (!_isLightOn)
            {
                return;
            }

            _sliderDebounceCts?.Cancel();
            _sliderDebounceCts?.Dispose();
            _sliderDebounceCts = new CancellationTokenSource();
            CancellationToken token = _sliderDebounceCts.Token;
            int brightness = _brightnessBar.Value;

            _ = DebouncedSetBrightnessAsync(brightness, token);
        }

        private UltravioletLightController? TryCreateLightController(bool showConnectionErrors)
        {
            UnoDeviceClient? client = _getUnoDevice(showConnectionErrors);
            if (client == null)
            {
                return null;
            }

            return new UltravioletLightController(client);
        }

        private async Task DebouncedSetBrightnessAsync(int brightness, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(SliderDebounceMilliseconds, cancellationToken);
                UltravioletLightController? light = TryCreateLightController(showConnectionErrors: false);
                if (light == null)
                {
                    return;
                }

                await light.SetBrightnessPercentAsync(brightness, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log.Error($"Adjust brightness failed: {ex.Message}", ex);
                Debug.WriteLine($"Adjust brightness failed: {ex.Message}");
                ShowError($"调整亮度失败：{ex.Message}");
            }
        }

        private async Task ExecuteLightActionAsync(
            Func<UltravioletLightController, Task> action,
            string actionName,
            bool showConnectionErrors)
        {
            _turnOnButton.Enabled = false;
            _turnOffButton.Enabled = false;
            try
            {
                UltravioletLightController? light = TryCreateLightController(showConnectionErrors);
                if (light == null)
                {
                    return;
                }

                await action(light);
            }
            catch (Exception ex)
            {
                _log.Error($"{actionName} failed: {ex.Message}", ex);
                Debug.WriteLine($"{actionName} failed: {ex.Message}");
                ShowError($"{actionName}失败：{ex.Message}");
            }
            finally
            {
                _turnOnButton.Enabled = true;
                _turnOffButton.Enabled = true;
            }
        }

        private void UpdateStatusLabel(bool isOn)
        {
            _statusLabel.RunOnUiThread(() => _statusLabel.Text = isOn ? "灯：开" : "灯：关");
        }

        private void ShowError(string message)
        {
            _mainForm.RunOnUiThread(() =>
                MessageBox.Show(_mainForm, message, "紫外光源", MessageBoxButtons.OK, MessageBoxIcon.Warning));
        }

        public void Dispose()
        {
            _sliderDebounceCts?.Cancel();
            _sliderDebounceCts?.Dispose();
            _sliderDebounceCts = null;

            _turnOnButton.Click -= TurnOnButton_Click;
            _turnOffButton.Click -= TurnOffButton_Click;
            _brightnessBar.ValueChanged -= BrightnessBar_ValueChanged;
        }
    }
}
