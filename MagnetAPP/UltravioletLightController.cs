using System;
using System.Threading;
using System.Threading.Tasks;

namespace MotorControl
{
    public sealed class UltravioletLightController
    {
        private readonly UnoDeviceClient _unoDevice;

        public int CurrentBrightnessPercent { get; private set; }
        public int CurrentPwmValue { get; private set; }

        public UltravioletLightController(UnoDeviceClient unoDevice)
        {
            _unoDevice = unoDevice ?? throw new ArgumentNullException(nameof(unoDevice));
        }

        public async Task SetBrightnessPercentAsync(
            int percent,
            CancellationToken cancellationToken = default)
        {
            await _unoDevice.SetUvBrightnessAsync(percent, cancellationToken);
            CurrentBrightnessPercent = Math.Min(Math.Max(percent, 0), 100);
            CurrentPwmValue = UnoDeviceProtocol.BrightnessPercentToPwm(CurrentBrightnessPercent);
        }

        public async Task SetPwmValueAsync(int pwmValue, CancellationToken cancellationToken = default)
        {
            int normalizedPwm = Math.Min(Math.Max(pwmValue, 0), 255);
            await _unoDevice.SetUvPwmAsync(normalizedPwm, cancellationToken);
            CurrentPwmValue = normalizedPwm;
            CurrentBrightnessPercent = (int)Math.Round(normalizedPwm / 255.0 * 100.0);
        }

        public async Task TurnOffAsync(CancellationToken cancellationToken = default)
        {
            await _unoDevice.TurnUvOffAsync(cancellationToken);
            CurrentBrightnessPercent = 0;
            CurrentPwmValue = 0;
        }

        public async Task FlashFullPowerAsync(
            TimeSpan duration,
            int restoreBrightnessPercent = 0,
            CancellationToken cancellationToken = default)
        {
            if (duration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be greater than zero.");
            }

            await SetBrightnessPercentAsync(100, cancellationToken);
            await Task.Delay(duration, cancellationToken);
            await SetBrightnessPercentAsync(restoreBrightnessPercent, cancellationToken);
        }
    }
}
