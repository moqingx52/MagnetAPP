using DLP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GcodeFollow
{
    public class GCodeProcessor//屏幕8520*4320，等比例缩小使用
    {
        const int SCREEN_WIDTH = 2130;
        const int SCREEN_HEIGHT = 1080;
        const double PIXEL_SIZE = 0.018;
        const int PIXEL_GROUP = 4;
        const int INTERPOLATION_STEP = 1;

        public delegate void GCodeOutputHandler(string gcode);
        public event GCodeOutputHandler OnGCodeOutput;
        public delegate void PositionUpdateHandler(double x, double y);
        public event PositionUpdateHandler OnPositionUpdate;

        public async Task ProcessGCodeFile(string filePath)
        {
            try
            {
                string[] lines = await File.ReadAllLinesAsync(filePath);

                for (int i = 0; i < lines.Length - 2; i += 3)
                {
                    if (i + 2 >= lines.Length) break;

                    await ProcessGCodeGroup(lines[i], lines[i + 1], lines[i + 2]);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error processing GCode file: {ex.Message}");
            }
        }

        private async Task ProcessGCodeGroup(string firstG0, string secondG0, string g1)
        {
            OnGCodeOutput?.Invoke(firstG0);
            await Task.Delay(1000);

            OnGCodeOutput?.Invoke(secondG0);
            await Task.Delay(1000);

            OnGCodeOutput?.Invoke(g1);

            var startPos = ExtractCoordinates(secondG0);
            var endPos = ExtractCoordinates(g1);
            var feedrate = ExtractFeedrate(g1);

            if (startPos != null && endPos != null && feedrate > 0)
            {
                await SimulateMovement(startPos.Value, endPos.Value, feedrate);
            }
        }

        private async Task SimulateMovement((double x, double y) start, (double x, double y) end, double feedrate)
        {
            double dx = end.x - start.x;
            double dy = end.y - start.y;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            double moveTime = distance / (feedrate / 60);
            int steps = (int)(distance / INTERPOLATION_STEP);
            if (steps < 1) steps = 1;

            int sleepTime = (int)(moveTime * 1000 / steps);

            //DisplayManager.Instance.EnableAutoUpdate();

            for (int step = 0; step <= steps; step++)
            {
                double t = (double)step / steps;
                double currentX = start.x + dx * t;
                double currentY = start.y + dy * t;

                //OnPositionUpdate?.Invoke(currentX, currentY);

                UpdateDisplay(currentX, currentY);
                await Task.Delay(sleepTime);
            }

            //DisplayManager.Instance.DisableAutoUpdate();
        }

        private void UpdateDisplay(double x, double y)
        {
            int pixelX = (int)(x / PIXEL_SIZE / PIXEL_GROUP);
            int pixelY = (int)(y / PIXEL_SIZE / PIXEL_GROUP);

            OnPositionUpdate?.Invoke(pixelX, pixelY );

            //DisplayManager.Instance.UpdateDisplay(
            //    pixelX ,
            //    pixelY ,
            //    (pixelX + 1),
            //    (pixelY + 1)
            //);
        }

        private static (double x, double y)? ExtractCoordinates(string gcode)
        {
            var xMatch = Regex.Match(gcode, @"X([-\d.]+)");
            var yMatch = Regex.Match(gcode, @"Y([-\d.]+)");

            if (xMatch.Success && yMatch.Success)
            {
                return (
                    double.Parse(xMatch.Groups[1].Value),
                    double.Parse(yMatch.Groups[1].Value)
                );
            }
            return null;
        }

        private static double ExtractFeedrate(string gcode)
        {
            var match = Regex.Match(gcode, @"F(\d+)");
            return match.Success ? double.Parse(match.Groups[1].Value) : 0;
        }
    }

}
