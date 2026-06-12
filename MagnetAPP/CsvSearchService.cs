using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MotorControl
{
    public sealed class SearchResultEventArgs : EventArgs
    {
        public string ResultColumn1 { get; init; } = string.Empty;
        public string ResultColumn2 { get; init; } = string.Empty;
        public double MatchedX { get; init; }
        public double MatchedY { get; init; }
        public double MatchedZ { get; init; }
    }

    public sealed class CsvSearcher
    {
        private readonly string _filePath;

        public event EventHandler<SearchResultEventArgs>? SearchCompleted;
        public event Action<string>? OutputGenerated;

        public CsvSearcher()
            : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mag_processed.csv"))
        {
        }

        public CsvSearcher(string filePath)
        {
            _filePath = filePath;
        }

        public async Task<SearchResultEventArgs?> SearchInCsvAsync(string xValue, string yValue, string zValue)
        {
            if (!TryParseInputs(xValue, yValue, zValue, out double targetX, out double targetY, out double targetZ))
            {
                throw new ArgumentException("请输入有效的数值！");
            }

            string[] lines = await File.ReadAllLinesAsync(_filePath);
            if (lines.Length == 0)
            {
                throw new InvalidDataException("CSV 文件为空。");
            }

            WriteOutput($"Total lines in CSV: {lines.Length}");
            WriteOutput($"CSV Header: {lines[0]}");

            double minimumDistance = double.MaxValue;
            string? bestMatchRow = null;

            foreach (string line in lines.Skip(1))
            {
                string[] cells = line.Split(',');
                if (cells.Length < 5)
                {
                    WriteOutput($"Line has insufficient columns: {cells.Length} columns found");
                    continue;
                }

                if (!TryParseCell(cells[2], out double currentX)
                    || !TryParseCell(cells[3], out double currentY)
                    || !TryParseCell(cells[4], out double currentZ))
                {
                    WriteOutput($"Failed to parse values in line: {line}");
                    continue;
                }

                double distance = CalculateDistance(currentX, currentY, currentZ, targetX, targetY, targetZ);
                if (distance >= minimumDistance)
                {
                    continue;
                }

                minimumDistance = distance;
                bestMatchRow = line;
                WriteOutput($"Find a line: {line}");
            }

            if (bestMatchRow is null)
            {
                return null;
            }

            SearchResultEventArgs result = CreateResult(bestMatchRow);
            SearchCompleted?.Invoke(this, result);
            return result;
        }

        private static SearchResultEventArgs CreateResult(string csvLine)
        {
            string[] cells = csvLine.Split(',');
            return new SearchResultEventArgs
            {
                ResultColumn1 = cells[0],
                ResultColumn2 = cells[1],
                MatchedX = double.Parse(cells[2], CultureInfo.InvariantCulture),
                MatchedY = double.Parse(cells[3], CultureInfo.InvariantCulture),
                MatchedZ = double.Parse(cells[4], CultureInfo.InvariantCulture)
            };
        }

        private static bool TryParseInputs(
            string textX,
            string textY,
            string textZ,
            out double targetX,
            out double targetY,
            out double targetZ)
        {
            targetX = 0;
            targetY = 0;
            targetZ = 0;
            return TryParseCell(textX, out targetX)
                && TryParseCell(textY, out targetY)
                && TryParseCell(textZ, out targetZ);
        }

        private static bool TryParseCell(string value, out double result)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                || double.TryParse(value, out result);
        }

        private static double CalculateDistance(
            double x1,
            double y1,
            double z1,
            double x2,
            double y2,
            double z2)
        {
            return Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2) + Math.Pow(z1 - z2, 2));
        }

        private void WriteOutput(string message)
        {
            OutputGenerated?.Invoke(message);
        }
    }
}
