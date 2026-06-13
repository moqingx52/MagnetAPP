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

    public sealed record MagneticFieldMapPoint(
        double YawPulse3200,
        double RollPulse3200,
        double X,
        double Y,
        double Z,
        double DirectionX,
        double DirectionY,
        double DirectionZ)
    {
        public SearchResultEventArgs ToSearchResult()
        {
            return new SearchResultEventArgs
            {
                ResultColumn1 = YawPulse3200.ToString("G17", CultureInfo.InvariantCulture),
                ResultColumn2 = RollPulse3200.ToString("G17", CultureInfo.InvariantCulture),
                MatchedX = X,
                MatchedY = Y,
                MatchedZ = Z
            };
        }
    }

    public readonly record struct PulseCorrection(double YawDeltaPulse3200, double RollDeltaPulse3200);

    public sealed class CsvSearcher
    {
        public const double CsvPulsePeriod = 3200.0;
        public const double CsvToMotorPulseScale = UnoDeviceProtocol.StepsPerRevolution / CsvPulsePeriod;

        private const double DirectionEpsilon = 1e-9;
        private const double LocalStepPulse = 50.0;
        private const double Damping = 0.002;

        private readonly string _filePath;
        private readonly object _loadSync = new();
        private Task<IReadOnlyList<MagneticFieldMapPoint>>? _loadTask;

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

            MagneticFieldMapPoint? point = await FindClosestDirectionAsync(targetX, targetY, targetZ);
            if (point is null)
            {
                return null;
            }

            SearchResultEventArgs result = point.ToSearchResult();
            SearchCompleted?.Invoke(this, result);
            return result;
        }

        public Task<IReadOnlyList<MagneticFieldMapPoint>> GetPointsAsync()
        {
            lock (_loadSync)
            {
                _loadTask ??= LoadPointsAsync();
                return _loadTask;
            }
        }

        public async Task<MagneticFieldMapPoint?> FindClosestDirectionAsync(double targetX, double targetY, double targetZ)
        {
            if (!TryNormalize(targetX, targetY, targetZ, out double dx, out double dy, out double dz))
            {
                return null;
            }

            IReadOnlyList<MagneticFieldMapPoint> points = await GetPointsAsync();
            MagneticFieldMapPoint? best = null;
            double bestDistance = double.MaxValue;

            foreach (MagneticFieldMapPoint point in points)
            {
                double distance = DirectionDistance(point.DirectionX, point.DirectionY, point.DirectionZ, dx, dy, dz);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = point;
                }
            }

            return best;
        }

        public async Task<MagneticFieldMapPoint?> FindClosestMeasuredVectorAsync(double x, double y, double z)
        {
            return await FindClosestDirectionAsync(x, y, z);
        }

        public async Task<MagneticFieldMapPoint?> FindBestNeighborDirectionAsync(
            double yawPulse3200,
            double rollPulse3200,
            double targetDirectionX,
            double targetDirectionY,
            double targetDirectionZ,
            double searchRadiusPulse = 250.0)
        {
            if (!TryNormalize(
                    targetDirectionX,
                    targetDirectionY,
                    targetDirectionZ,
                    out double tx,
                    out double ty,
                    out double tz))
            {
                return null;
            }

            IReadOnlyList<MagneticFieldMapPoint> points = await GetPointsAsync();
            MagneticFieldMapPoint? best = null;
            double bestCost = double.MaxValue;

            foreach (MagneticFieldMapPoint point in points)
            {
                double yawDelta = Math.Abs(ShortestSignedPulseDelta(yawPulse3200, point.YawPulse3200));
                double rollDelta = Math.Abs(ShortestSignedPulseDelta(rollPulse3200, point.RollPulse3200));
                if (yawDelta > searchRadiusPulse || rollDelta > searchRadiusPulse)
                {
                    continue;
                }

                double directionCost = DirectionDistance(point.DirectionX, point.DirectionY, point.DirectionZ, tx, ty, tz);
                double motionPenalty = 0.00002 * (yawDelta + rollDelta);
                double cost = directionCost + motionPenalty;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = point;
                }
            }

            return best;
        }

        public async Task<PulseCorrection?> CalculateLocalCorrectionAsync(
            double yawPulse3200,
            double rollPulse3200,
            double currentDirectionX,
            double currentDirectionY,
            double currentDirectionZ,
            double targetDirectionX,
            double targetDirectionY,
            double targetDirectionZ)
        {
            if (!TryNormalize(
                    currentDirectionX,
                    currentDirectionY,
                    currentDirectionZ,
                    out double cx,
                    out double cy,
                    out double cz)
                || !TryNormalize(
                    targetDirectionX,
                    targetDirectionY,
                    targetDirectionZ,
                    out double tx,
                    out double ty,
                    out double tz))
            {
                return null;
            }

            MagneticFieldMapPoint? yawPlus = await FindNearestPositionAsync(yawPulse3200 + LocalStepPulse, rollPulse3200);
            MagneticFieldMapPoint? yawMinus = await FindNearestPositionAsync(yawPulse3200 - LocalStepPulse, rollPulse3200);
            MagneticFieldMapPoint? rollPlus = await FindNearestPositionAsync(yawPulse3200, rollPulse3200 + LocalStepPulse);
            MagneticFieldMapPoint? rollMinus = await FindNearestPositionAsync(yawPulse3200, rollPulse3200 - LocalStepPulse);
            if (yawPlus is null || yawMinus is null || rollPlus is null || rollMinus is null)
            {
                return null;
            }

            double j00 = (yawPlus.DirectionX - yawMinus.DirectionX) / (2.0 * LocalStepPulse);
            double j10 = (yawPlus.DirectionY - yawMinus.DirectionY) / (2.0 * LocalStepPulse);
            double j20 = (yawPlus.DirectionZ - yawMinus.DirectionZ) / (2.0 * LocalStepPulse);
            double j01 = (rollPlus.DirectionX - rollMinus.DirectionX) / (2.0 * LocalStepPulse);
            double j11 = (rollPlus.DirectionY - rollMinus.DirectionY) / (2.0 * LocalStepPulse);
            double j21 = (rollPlus.DirectionZ - rollMinus.DirectionZ) / (2.0 * LocalStepPulse);

            double ex = tx - cx;
            double ey = ty - cy;
            double ez = tz - cz;

            double a00 = j00 * j00 + j10 * j10 + j20 * j20 + Damping * Damping;
            double a01 = j00 * j01 + j10 * j11 + j20 * j21;
            double a11 = j01 * j01 + j11 * j11 + j21 * j21 + Damping * Damping;
            double b0 = j00 * ex + j10 * ey + j20 * ez;
            double b1 = j01 * ex + j11 * ey + j21 * ez;
            double determinant = a00 * a11 - a01 * a01;
            if (Math.Abs(determinant) < 1e-10)
            {
                return null;
            }

            double yawDeltaPulse3200 = (b0 * a11 - b1 * a01) / determinant;
            double rollDeltaPulse3200 = (a00 * b1 - a01 * b0) / determinant;
            if (!double.IsFinite(yawDeltaPulse3200) || !double.IsFinite(rollDeltaPulse3200))
            {
                return null;
            }

            return new PulseCorrection(yawDeltaPulse3200, rollDeltaPulse3200);
        }

        public async Task<MagneticFieldMapPoint?> FindNearestPositionAsync(double yawPulse3200, double rollPulse3200)
        {
            IReadOnlyList<MagneticFieldMapPoint> points = await GetPointsAsync();
            MagneticFieldMapPoint? best = null;
            double bestDistance = double.MaxValue;
            double normalizedYaw = NormalizeCsvPulse(yawPulse3200);
            double normalizedRoll = NormalizeCsvPulse(rollPulse3200);

            foreach (MagneticFieldMapPoint point in points)
            {
                double dy = ShortestSignedPulseDelta(normalizedYaw, point.YawPulse3200);
                double dr = ShortestSignedPulseDelta(normalizedRoll, point.RollPulse3200);
                double distance = dy * dy + dr * dr;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = point;
                }
            }

            return best;
        }

        public static double NormalizeCsvPulse(double pulse)
        {
            double normalized = pulse % CsvPulsePeriod;
            return normalized < 0 ? normalized + CsvPulsePeriod : normalized;
        }

        public static double ShortestSignedPulseDelta(double currentPulse, double targetPulse)
        {
            double delta = NormalizeCsvPulse(targetPulse) - NormalizeCsvPulse(currentPulse);
            if (delta > CsvPulsePeriod / 2.0)
            {
                delta -= CsvPulsePeriod;
            }
            else if (delta < -CsvPulsePeriod / 2.0)
            {
                delta += CsvPulsePeriod;
            }

            return delta;
        }

        public static int CsvPulseDeltaToMotorSteps(double csvPulseDelta)
        {
            return (int)Math.Round(Math.Abs(csvPulseDelta) * CsvToMotorPulseScale);
        }

        public static double AngleErrorDegrees(
            double x1,
            double y1,
            double z1,
            double x2,
            double y2,
            double z2)
        {
            if (!TryNormalize(x1, y1, z1, out double dx1, out double dy1, out double dz1)
                || !TryNormalize(x2, y2, z2, out double dx2, out double dy2, out double dz2))
            {
                return double.MaxValue;
            }

            double dot = dx1 * dx2 + dy1 * dy2 + dz1 * dz2;
            dot = Math.Min(1.0, Math.Max(-1.0, dot));
            return Math.Acos(dot) * 180.0 / Math.PI;
        }

        public static bool TryNormalize(
            double x,
            double y,
            double z,
            out double directionX,
            out double directionY,
            out double directionZ)
        {
            double norm = Math.Sqrt(x * x + y * y + z * z);
            if (norm <= DirectionEpsilon)
            {
                directionX = directionY = directionZ = 0;
                return false;
            }

            directionX = x / norm;
            directionY = y / norm;
            directionZ = z / norm;
            return true;
        }

        private async Task<IReadOnlyList<MagneticFieldMapPoint>> LoadPointsAsync()
        {
            string[] lines = await File.ReadAllLinesAsync(_filePath);
            if (lines.Length == 0)
            {
                throw new InvalidDataException("CSV 文件为空。");
            }

            WriteOutput($"Total lines in CSV: {lines.Length}");
            WriteOutput($"CSV Header: {lines[0]}");

            List<MagneticFieldMapPoint> points = new(lines.Length - 1);
            foreach (string line in lines.Skip(1))
            {
                string[] cells = SplitCsvLine(line);
                if (cells.Length < 3)
                {
                    WriteOutput($"Line has insufficient columns: {cells.Length} columns found");
                    continue;
                }

                if (!TryParseCell(cells[0], out double yawPulse)
                    || !TryParseCell(cells[1], out double rollPulse)
                    || !TryParseVector3(cells[2], out double x, out double y, out double z)
                    || !TryNormalize(x, y, z, out double directionX, out double directionY, out double directionZ))
                {
                    WriteOutput($"Failed to parse values in line: {line}");
                    continue;
                }

                points.Add(new MagneticFieldMapPoint(
                    yawPulse,
                    rollPulse,
                    x,
                    y,
                    z,
                    directionX,
                    directionY,
                    directionZ));
            }

            return points;
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

        private static double DirectionDistance(
            double x1,
            double y1,
            double z1,
            double x2,
            double y2,
            double z2)
        {
            double dot = x1 * x2 + y1 * y2 + z1 * z2;
            dot = Math.Min(1.0, Math.Max(-1.0, dot));
            return 1.0 - dot;
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
