using System;
using System.Windows.Forms;
using DLP;

namespace MotorControl
{
    public sealed class DisplayController : IDisposable
    {
        private const int DisplayWidth = 2130;
        private const int DisplayHeight = 1080;

        private readonly MainForm _mainForm;
        private readonly TextBox _startXTextBox;
        private readonly TextBox _endXTextBox;
        private readonly TextBox _startYTextBox;
        private readonly TextBox _endYTextBox;
        private readonly Button _showButton;
        private readonly Button _resetButton;

        private DLP.DisplayForm? _displayForm;
        private bool _autoUpdateEnabled = false;

        public DisplayController(
            MainForm mainForm,
            TextBox startXTextBox,
            TextBox endXTextBox,
            TextBox startYTextBox,
            TextBox endYTextBox,
            Button showButton,
            Button resetButton)
        {
            _mainForm = mainForm;
            _startXTextBox = startXTextBox;
            _endXTextBox = endXTextBox;
            _startYTextBox = startYTextBox;
            _endYTextBox = endYTextBox;
            _showButton = showButton;
            _resetButton = resetButton;

            _showButton.Click += ShowButton_Click;
            _resetButton.Click += ResetButton_Click;
        }

        private void ShowButton_Click(object? sender, EventArgs e)
        {
            try
            {
                if (!TryReadDisplayRegion(out int x1, out int x2, out int y1, out int y2, out string errorMessage))
                {
                    MessageBox.Show(errorMessage, "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                ShowDisplayRegion(x1, x2, y1, y2);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"显示区域更新失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ResetButton_Click(object? sender, EventArgs e)
        {
            ShowBlackScreen();
        }

        public void UpdateDisplay(int x1, int x2, int y1, int y2)
        {
            if (!_autoUpdateEnabled) return;

            _mainForm.RunOnUiThread(() => ShowDisplayRegion(x1, x2, y1, y2));
        }

        private void ShowDisplayRegion(int x1, int x2, int y1, int y2)
        {
            DisplayForm displayForm = GetOrCreateDisplayForm();
            displayForm.ShowImage(x1, x2, y1, y2);
            displayForm.Show();
            displayForm.BringToFront();
        }

        public void ShowBlackScreen()
        {
            _mainForm.RunOnUiThread(ShowBlackScreenInternal);
        }

        private void ShowBlackScreenInternal()
        {
            DisplayForm displayForm = GetOrCreateDisplayForm();
            displayForm.ShowBlack();
            displayForm.Show();
            displayForm.BringToFront();
        }

        public void EnableAutoUpdate()
        {
            _autoUpdateEnabled = true;
        }

        public void DisableAutoUpdate()
        {
            _autoUpdateEnabled = false;
        }

        public void CloseDisplay()
        {
            if (_displayForm is null || _displayForm.IsDisposed)
            {
                return;
            }

            _mainForm.RunOnUiThread(() => _displayForm.Close());
        }

        public void Dispose()
        {
            _showButton.Click -= ShowButton_Click;
            _resetButton.Click -= ResetButton_Click;

            if (_displayForm is not null && !_displayForm.IsDisposed)
            {
                _displayForm.Close();
                _displayForm.Dispose();
            }

            _displayForm = null;
        }

        private DisplayForm GetOrCreateDisplayForm()
        {
            if (_displayForm is null || _displayForm.IsDisposed)
            {
                _displayForm = new DisplayForm();
            }

            return _displayForm;
        }

        private bool TryReadDisplayRegion(
            out int x1,
            out int x2,
            out int y1,
            out int y2,
            out string errorMessage)
        {
            x1 = x2 = y1 = y2 = 0;
            errorMessage = string.Empty;

            if (!int.TryParse(_startXTextBox.Text, out x1)
                || !int.TryParse(_endXTextBox.Text, out x2)
                || !int.TryParse(_startYTextBox.Text, out y1)
                || !int.TryParse(_endYTextBox.Text, out y2))
            {
                errorMessage = "请输入有效的数字。";
                return false;
            }

            if (x1 < 0 || x2 > DisplayWidth || y1 < 0 || y2 > DisplayHeight || x2 <= x1 || y2 <= y1)
            {
                errorMessage = $"请输入有效的坐标值。\n横坐标范围：0-{DisplayWidth}\n纵坐标范围：0-{DisplayHeight}";
                return false;
            }

            return true;
        }
    }
}
