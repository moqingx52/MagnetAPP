using DLP;
using MotorControl;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace _3DPrint
{
    public class _3DPrinter
    {
        private const double PixelSize = 0.072; // 72微米转换为毫米
        private const int ScreenWidth = 2130;
        private const int ScreenHeight = 1080;
        private const int DisplayHalfWidth = 14;

        private readonly MainForm _mainForm;
        private bool _isProcessing = false;
        private DisplayForm? _displayForm;

        public _3DPrinter(MainForm form)
        {
            _mainForm = form;
        }

        public async Task ProcessNextGcodeLine()
        {
            if (_isProcessing) return;
            _isProcessing = true;
            _mainForm.SetButton4Enabled(false);

            try
            {
                string line = _mainForm.GetFirstTextBox2Line();
                if (string.IsNullOrEmpty(line))
                {
                    return; // No lines to process
                }

                _mainForm.RemoveFirstTextBox2Line();
                _mainForm.AppendToRichTextBox4(line + Environment.NewLine);

                if (line.StartsWith("G0"))
                {
                    var match = Regex.Match(line, @"X(\d+\.?\d*)\s*Y(\d+\.?\d*)");
                    if (match.Success)
                    {
                        double xMm = double.Parse(match.Groups[1].Value);
                        double yMm = double.Parse(match.Groups[2].Value);

                        // 将毫米坐标转换为像素坐标
                        int xPixel = (int)(xMm / PixelSize - 4.4 / PixelSize);
                        int yPixel = (int)(yMm / PixelSize + 16 / PixelSize);

                        //实际坐标系偏移：原点在x方向偏移了-24.00, y方向偏移了-1.00


                        // 确保像素坐标在屏幕范围内，以下是需要随运动路径更改的地方
                        xPixel = Math.Min(Math.Max(xPixel, DisplayHalfWidth), ScreenWidth - DisplayHalfWidth);
                        yPixel = Math.Min(Math.Max(yPixel, DisplayHalfWidth), ScreenHeight - DisplayHalfWidth);



                        // 更新显示
                        //DisplayManager.Instance.EnableAutoUpdate();
                        //DisplayManager.Instance.UpdateDisplay(xPixel-5, xPixel + 5, yPixel-5, yPixel + 5);//+50是为了看清楚，记得改成1
                        //DisplayManager.Instance.DisableAutoUpdate();

                        if (_displayForm == null || _displayForm.IsDisposed)
                        {
                            _displayForm = new DisplayForm();
                        }

                        _displayForm.ShowImage(
                            xPixel - DisplayHalfWidth,
                            xPixel + DisplayHalfWidth,
                            yPixel - DisplayHalfWidth,
                            yPixel + DisplayHalfWidth);
                        _displayForm.Show();

                        KlipperCommunicator? klipper = _mainForm.Klipper;
                        if (klipper is null || !_mainForm.IsKlipperConnected())
                        {
                            _mainForm.AppendToRichTextBox5("未连接到Klipper服务器" + Environment.NewLine);
                            return;
                        }

                        if (await klipper.SendCommandAsync(line))
                        {
                            _mainForm.AppendToRichTextBox5($"像素位置: X={xPixel}, Y={yPixel},语句：{line}" + Environment.NewLine);
                        }
                        else
                        {
                            _mainForm.AppendToRichTextBox5($"语句：{line}，未执行" + Environment.NewLine);
                        }
                        // 等待指定时间
                        await Task.Delay(1000); // 1秒
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"处理出错: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
                _mainForm.SetButton4Enabled(_mainForm.GetTextBox2LinesCount() > 0);
            }
        }
    }
}
