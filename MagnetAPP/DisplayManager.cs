using System;

namespace DLP
{
    public class DisplayManager
    {
        private static DisplayManager instance;
        private IDisplayControl displayControl;

        // 单例实例
        public static DisplayManager Instance
        {
            get
            {
                if (instance == null)
                    instance = new DisplayManager();
                return instance;
            }
        }

        // 私有构造函数，防止外部创建实例
        private DisplayManager()
        {
        }

        // 初始化方法，接收实现了IDisplayControl接口的任何类
        public void Initialize(IDisplayControl control)
        {
            displayControl = control;
        }

        // 更新显示
        public void UpdateDisplay(int x1, int x2, int y1, int y2)
        {
            if (displayControl != null)
                displayControl.UpdateDisplay(x1, x2, y1, y2);
        }

        // 显示黑屏
        public void ShowBlackScreen()
        {
            if (displayControl != null)
                displayControl.ShowBlackScreen();
        }

        // 启用自动更新
        public void EnableAutoUpdate()
        {
            if (displayControl != null)
                displayControl.EnableAutoUpdate();
        }

        // 禁用自动更新
        public void DisableAutoUpdate()
        {
            if (displayControl != null)
                displayControl.DisableAutoUpdate();
        }

        // 关闭显示
        public void CloseDisplay()
        {
            if (displayControl != null)
                displayControl.CloseDisplay();
        }
    }
}
