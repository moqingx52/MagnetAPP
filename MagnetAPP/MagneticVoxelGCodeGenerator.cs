using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OfficeOpenXml;

namespace MotorControl
{
    public sealed class MagneticVoxelGCodeGenerator
    {
        private const string BottomLayerSheetName = "bottom_layer";
        private const string MagneticLayerSheetName = "magnetic_layer";
        private const double OriginXMillimeters = 50.0;
        private const double OriginYMillimeters = 15.0;
        private const double VoxelPitchMillimeters = 1.1;
        private const double FeedRate = 600.0;
        private const double RotationWeight = 0.02;

        private readonly CsvSearcher _csvSearcher;

        public MagneticVoxelGCodeGenerator()
            : this(new CsvSearcher())
        {
        }

        public MagneticVoxelGCodeGenerator(CsvSearcher csvSearcher)
        {
            _csvSearcher = csvSearcher;
        }

        public async Task<string> GenerateAsync(string xlsxPath)
        {
            if (string.IsNullOrWhiteSpace(xlsxPath))
            {
                throw new ArgumentException("请输入 xlsx 文件路径。", nameof(xlsxPath));
            }

            if (!File.Exists(xlsxPath))
            {
                throw new FileNotFoundException("文件不存在，请检查文件路径。", xlsxPath);
            }

            if (!string.Equals(Path.GetExtension(xlsxPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("仅支持 .xlsx 文件作为输入。");
            }

            IReadOnlyList<MagneticVoxelCommand> commands = await ReadCommandsAsync(xlsxPath);
            IReadOnlyList<MagneticVoxelCommand> planned = PlanGreedy(commands);
            string outputPath = Path.ChangeExtension(xlsxPath, ".gcode");
            WriteGCode(outputPath, planned);
            return outputPath;
        }

        private async Task<IReadOnlyList<MagneticVoxelCommand>> ReadCommandsAsync(string xlsxPath)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using ExcelPackage package = new(new FileInfo(xlsxPath));
            ExcelWorksheet magneticSheet = package.Workbook.Worksheets[MagneticLayerSheetName]
                ?? throw new InvalidDataException($"缺少 sheet: {MagneticLayerSheetName}");
            ExcelWorksheet? bottomSheet = package.Workbook.Worksheets[BottomLayerSheetName];

            int rows = Math.Max(magneticSheet.Dimension?.Rows ?? 0, 0);
            int cols = Math.Max(magneticSheet.Dimension?.Columns ?? 0, 0);
            if (rows <= 0 || cols <= 0)
            {
                throw new InvalidDataException($"{MagneticLayerSheetName} 为空。");
            }

            List<MagneticVoxelCommand> commands = new();
            for (int row = 1; row <= rows; row++)
            {
                for (int col = 1; col <= cols; col++)
                {
                    if (bottomSheet is not null && !IsOccupied(bottomSheet.Cells[row, col].Value))
                    {
                        continue;
                    }

                    object? rawVector = magneticSheet.Cells[row, col].Value;
                    if (!TryParseVector3(rawVector, out double mx, out double my, out double mz))
                    {
                        continue;
                    }

                    if (IsZeroVector(mx, my, mz))
                    {
                        continue;
                    }

                    SearchResultEventArgs? fieldMatch = await _csvSearcher.SearchInCsvAsync(
                        FormatNumber(mx),
                        FormatNumber(my),
                        FormatNumber(mz));
                    if (fieldMatch is null)
                    {
                        throw new InvalidDataException(
                            $"CSV 中找不到磁场方向近似值: [{mx},{my},{mz}] at R{row}C{col}");
                    }

                    commands.Add(new MagneticVoxelCommand(
                        Row: row - 1,
                        Col: col - 1,
                        X: OriginXMillimeters + (col - 1) * VoxelPitchMillimeters,
                        Y: OriginYMillimeters + (row - 1) * VoxelPitchMillimeters,
                        Mx: mx,
                        My: my,
                        Mz: mz,
                        Yaw: ParseCsvAngle(fieldMatch.ResultColumn1),
                        Roll: ParseCsvAngle(fieldMatch.ResultColumn2)));
                }
            }

            if (commands.Count == 0)
            {
                throw new InvalidDataException($"{MagneticLayerSheetName} 中没有有效磁化体素。");
            }

            return commands;
        }

        private static IReadOnlyList<MagneticVoxelCommand> PlanGreedy(IReadOnlyList<MagneticVoxelCommand> commands)
        {
            List<MagneticVoxelCommand> remaining = commands.ToList();
            List<MagneticVoxelCommand> planned = new(remaining.Count);

            double currentX = OriginXMillimeters;
            double currentY = OriginYMillimeters;
            double currentYaw = 0;
            double currentRoll = 0;

            while (remaining.Count > 0)
            {
                int bestIndex = 0;
                double bestCost = double.MaxValue;

                for (int i = 0; i < remaining.Count; i++)
                {
                    MagneticVoxelCommand candidate = remaining[i];
                    double travel = Distance(currentX, currentY, candidate.X, candidate.Y);
                    double rotation = PulseDistance(currentYaw, candidate.Yaw) + PulseDistance(currentRoll, candidate.Roll);
                    double cost = travel + RotationWeight * rotation;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestIndex = i;
                    }
                }

                MagneticVoxelCommand selected = remaining[bestIndex];
                remaining.RemoveAt(bestIndex);
                planned.Add(selected);
                currentX = selected.X;
                currentY = selected.Y;
                currentYaw = selected.Yaw;
                currentRoll = selected.Roll;
            }

            return planned;
        }

        private static void WriteGCode(string outputPath, IReadOnlyList<MagneticVoxelCommand> commands)
        {
            List<string> lines = new(commands.Count);

            foreach (MagneticVoxelCommand command in commands)
            {
                lines.Add(FormattableString.Invariant(
                    $"G0 F{FeedRate:0} X{command.X:0.###} Y{command.Y:0.###}//[{command.Mx:0.###},{command.My:0.###},{command.Mz:0.###}]; yawPulse3200={command.Yaw:0.###}; rollPulse3200={command.Roll:0.###}; row={command.Row}; col={command.Col}"));
            }

            File.WriteAllLines(outputPath, lines);
        }

        private static bool TryParseVector3(object? value, out double x, out double y, out double z)
        {
            x = y = z = 0;
            if (value is null)
            {
                return false;
            }

            string text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                return false;
            }

            text = text.Trim('[', ']', '(', ')').Replace(",", " ").Replace(";", " ");
            string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
            {
                throw new InvalidDataException($"磁化向量必须为 [x,y,z] 格式，实际为: {value}");
            }

            return TryParseDouble(parts[0], out x)
                && TryParseDouble(parts[1], out y)
                && TryParseDouble(parts[2], out z);
        }

        private static bool IsOccupied(object? value)
        {
            if (value is null)
            {
                return false;
            }

            if (value is bool boolValue)
            {
                return boolValue;
            }

            string text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                return false;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double numeric)
                || double.TryParse(text, out numeric))
            {
                return Math.Abs(numeric) > 1e-12;
            }

            return text.Equals("true", StringComparison.OrdinalIgnoreCase)
                || text.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || text.Equals("y", StringComparison.OrdinalIgnoreCase)
                || text.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseDouble(string value, out double result)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                || double.TryParse(value, out result);
        }

        private static double ParseCsvAngle(string value)
        {
            if (!TryParseDouble(value, out double result))
            {
                throw new InvalidDataException($"CSV pulse 解析失败: {value}");
            }

            return result;
        }

        private static bool IsZeroVector(double x, double y, double z)
        {
            return Math.Sqrt(x * x + y * y + z * z) <= 1e-9;
        }

        private static double Distance(double x1, double y1, double x2, double y2)
        {
            double dx = x1 - x2;
            double dy = y1 - y2;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double PulseDistance(double a, double b)
        {
            return Math.Abs(CsvSearcher.ShortestSignedPulseDelta(a, b));
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        private sealed record MagneticVoxelCommand(
            int Row,
            int Col,
            double X,
            double Y,
            double Mx,
            double My,
            double Mz,
            double Yaw,
            double Roll);
    }
}
