using System;

namespace MotorControl
{
    internal static class DisplayAlignmentSettings
    {
        public const double PixelSizeMillimeters = 0.072;
        public const int ScreenWidth = 2130;
        public const int ScreenHeight = 1080;

        private static readonly object SyncRoot = new();

        private static double _offsetXMillimeters = 4.4;
        private static double _offsetYMillimeters = -16.0;

        public static double OffsetXMillimeters
        {
            get
            {
                lock (SyncRoot)
                {
                    return _offsetXMillimeters;
                }
            }
        }

        public static double OffsetYMillimeters
        {
            get
            {
                lock (SyncRoot)
                {
                    return _offsetYMillimeters;
                }
            }
        }

        public static void UpdateOffsets(double offsetXMillimeters, double offsetYMillimeters)
        {
            lock (SyncRoot)
            {
                _offsetXMillimeters = offsetXMillimeters;
                _offsetYMillimeters = offsetYMillimeters;
            }
        }

        public static (int X, int Y) RobotMillimetersToDisplayPixel(double robotXMillimeters, double robotYMillimeters)
        {
            double offsetX;
            double offsetY;
            lock (SyncRoot)
            {
                offsetX = _offsetXMillimeters;
                offsetY = _offsetYMillimeters;
            }

            int pixelX = (int)Math.Round((robotXMillimeters - offsetX) / PixelSizeMillimeters);
            int pixelY = (int)Math.Round((robotYMillimeters - offsetY) / PixelSizeMillimeters);

            return (
                Math.Min(Math.Max(pixelX, 0), ScreenWidth - 1),
                Math.Min(Math.Max(pixelY, 0), ScreenHeight - 1));
        }
    }
}
