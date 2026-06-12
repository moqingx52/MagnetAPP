using System;
using System.Collections.Generic;
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
            string[]? bestMatchCells = null;
            bool targetIsDirectionOnly = IsDirectionOnlyTarget(targetX, targetY, targetZ);

            foreach (string line in lines.Skip(1))
            {
                string[] cells = SplitCsvLine(line);
                if (cells.Length < 3)
                {
                    WriteOutput($"Line has insufficient columns: {cells.Length} columns found");
                    continue;
                }

                if (!TryParseVector3(cells[2], out double currentX, out double currentY, out double currentZ))
                {
                    WriteOutput($"Failed to parse values in line: {line}");
                    continue;
                }

                double distance = targetIsDirectionOnly
                    ? CalculateDirectionDistance(currentX, currentY, currentZ, targetX, targetY, targetZ)
                    : CalculateDistance(currentX, currentY, currentZ, targetX, targetY, targetZ);
                if (distance >= minimumDistance)
                {
                    continue;
                }

                minimumDistance = distance;
                bestMatchCells = cells;
                WriteOutput($"Find a line: {line}");
            }

            if (bestMatchCells is null)
            {
                return null;
            }

            SearchResultEventArgs result = CreateResult(bestMatchCells);
            SearchCompleted?.Invoke(this, result);
            return result;
        }

        private static SearchResultEventArgs CreateResult(string[] cells)
        {
            TryParseVector3(cells[2], out double x, out double y, out double z);
            return new SearchResultEventArgs
            {
                ResultColumn1 = cells[0],
                ResultColumn2 = cells[1],
                MatchedX = x,
                MatchedY = y,
                MatchedZ = z
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

        private static bool TryParseVector3(string value, out double x, out double y, out double z)
        {
            x = y = z = 0;
            string text = value.Trim().Trim('"').Trim('[', ']', '(', ')');
            text = text.Replace(",", " ").Replace(";", " ");
            string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
            {
                return false;
            }

            return TryParseCell(parts[0], out x)
                && TryParseCell(parts[1], out y)
                && TryParseCell(parts[2], out z);
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

        private static double CalculateDirectionDistance(
            double x1,
            double y1,
            double z1,
            double x2,
            double y2,
            double z2)
        {
            double n1 = Math.Sqrt(x1 * x1 + y1 * y1 + z1 * z1);
            double n2 = Math.Sqrt(x2 * x2 + y2 * y2 + z2 * z2);
            if (n1 <= 1e-9 || n2 <= 1e-9)
            {
                return double.MaxValue;
            }

            double dot = (x1 * x2 + y1 * y2 + z1 * z2) / (n1 * n2);
            dot = Math.Min(1.0, Math.Max(-1.0, dot));
            return 1.0 - dot;
        }

        private static bool IsDirectionOnlyTarget(double x, double y, double z)
        {
            double norm = Math.Sqrt(x * x + y * y + z * z);
            return norm > 1e-9 && norm <= 2.0;
        }

        private static string[] SplitCsvLine(string line)
        {
            List<string> cells = new();
            StringWriter current = new();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Write('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                    continue;
                }

                if (ch == ',' && !inQuotes)
                {
                    cells.Add(current.ToString());
                    current.Dispose();
                    current = new StringWriter();
                    continue;
                }

                current.Write(ch);
            }

            cells.Add(current.ToString());
            current.Dispose();
            return cells.ToArray();
        }

        private void WriteOutput(string message)
        {
            OutputGenerated?.Invoke(message);
        }
    }
}
