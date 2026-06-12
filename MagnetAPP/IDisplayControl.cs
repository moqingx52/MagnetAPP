using System;

namespace DLP
{
    public interface IDisplayControl
    {
        void UpdateDisplay(int x1, int x2, int y1, int y2);
        void ShowBlackScreen();
        void EnableAutoUpdate();
        void DisableAutoUpdate();
        void CloseDisplay();
    }
}