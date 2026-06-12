using System;
using System.Drawing;
using System.Windows.Forms;

namespace DLP
{
    public class DisplayForm : Form
    {
        private PictureBox pictureBox;

        public DisplayForm()
        {
            InitializeForm();
        }

        private void InitializeForm()
        {
            // 设置无边框样式
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Normal;

            // 获取副屏
            if (Screen.AllScreens.Length > 1)
            {
                Screen secondaryScreen = Screen.AllScreens[1];
                
                this.StartPosition = FormStartPosition.Manual;
                this.Location = secondaryScreen.Bounds.Location;
                this.Size = new Size(Screen.AllScreens[1].Bounds.Width, Screen.AllScreens[1].Bounds.Height); // 设置为副屏分辨率，实际8520*4320，2130,1080，72um*72um
            }

            // 创建和配置PictureBox
            pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.StretchImage
            };
            this.Controls.Add(pictureBox);

            // 双击关闭窗口
            this.DoubleClick += (sender, e) => this.Close();
        }

        public void ShowImage(int x1, int x2, int y1, int y2)
        {
            // 创建黑色背景图片
            Bitmap bmp = new Bitmap(2130, 1080);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                // 填充黑色背景
                g.Clear(Color.Black);

                // 在指定区域绘制白色矩形
                using (SolidBrush whiteBrush = new SolidBrush(Color.White))
                {
                    int width = x2 - x1;
                    int height = y2 - y1;
                    g.FillRectangle(whiteBrush, x1, y1, width, height);
                }
            }

            // 如果已有图片，释放它
            if (pictureBox.Image != null)
            {
                pictureBox.Image.Dispose();
            }

            pictureBox.Image = bmp;
           
        }

        public void ShowBlack()
        {
            // 创建纯黑图片
            Bitmap bmp = new Bitmap(2130, 1080);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
            }

            // 如果已有图片，释放它
            if (pictureBox.Image != null)
            {
                pictureBox.Image.Dispose();
            }

            pictureBox.Image = bmp;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 清理资源
            if (pictureBox.Image != null)
            {
                pictureBox.Image.Dispose();
            }
            base.OnFormClosing(e);
        }


    }
}