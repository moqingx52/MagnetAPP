using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MotorControl
{
    /// <summary>
    /// Evaluates magnet rotation speed formulas ω(t) with sin/cos support.
    /// </summary>
    public sealed class MagnetSpeedFormulaEvaluator
    {
        public const double MaxSpeedRadPerSec = 10.0;

        private readonly string _expression;
        private int _position;
        private double _timeSeconds;

        private MagnetSpeedFormulaEvaluator(string expression)
        {
            _expression = expression;
        }

        public static bool TryCreate(string rawFormula, out MagnetSpeedFormulaEvaluator? evaluator, out string error)
        {
            evaluator = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(rawFormula))
            {
                error = "公式不能为空。";
                return false;
            }

            string normalized = Normalize(rawFormula);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                error = "公式不能为空。";
                return false;
            }

            if (!IsSafeExpression(normalized))
            {
                error = "公式包含不支持的字符或语法。";
                return false;
            }

            try
            {
                var parser = new MagnetSpeedFormulaEvaluator(normalized);
                parser.EvaluateAt(0);
                evaluator = parser;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public double EvaluateAt(double tSeconds)
        {
            _timeSeconds = tSeconds;
            _position = 0;
            double value = ParseExpression();
            if (_position != _expression.Length)
            {
                throw new FormatException("公式存在无法解析的部分。");
            }

            if (!IsFinite(value))
            {
                throw new FormatException("公式求值结果无效。");
            }

            return Math.Clamp(value, -MaxSpeedRadPerSec, MaxSpeedRadPerSec);
        }

        private static string Normalize(string rawFormula)
        {
            string text = rawFormula.Normalize(NormalizationForm.FormKC);
            text = text.Replace('（', '(')
                .Replace('）', ')')
                .Replace('＋', '+')
                .Replace('－', '-')
                .Replace('−', '-')
                .Replace('×', '*')
                .Replace('÷', '/')
                .Replace('，', ',')
                .Replace('。', '.');

            text = Regex.Replace(text, @"\s+", string.Empty);
            return InsertImplicitMultiplication(text);
        }

        private static string InsertImplicitMultiplication(string text)
        {
            text = Regex.Replace(text, @"(\d)([t(])", "$1*$2", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"(\))([t\d(])", "$1*$2", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"(t)(\()", "$1*$2", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"(\d)(sin|cos)", "$1*$2", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"(\))(sin|cos)", "$1*$2", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"(t)(sin|cos)", "$1*$2", RegexOptions.IgnoreCase);
            return text;
        }

        private static bool IsSafeExpression(string text)
        {
            return Regex.IsMatch(text, @"^[0-9t+\-*/().sinoc]+$", RegexOptions.IgnoreCase);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private double ParseExpression()
        {
            double value = ParseTerm();
            while (true)
            {
                if (Match('+'))
                {
                    value += ParseTerm();
                }
                else if (Match('-'))
                {
                    value -= ParseTerm();
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParseTerm()
        {
            double value = ParseFactor();
            while (true)
            {
                if (Match('*'))
                {
                    value *= ParseFactor();
                }
                else if (Match('/'))
                {
                    double divisor = ParseFactor();
                    if (Math.Abs(divisor) < 1e-12)
                    {
                        throw new DivideByZeroException("公式出现除以零。");
                    }

                    value /= divisor;
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParseFactor()
        {
            if (Match('+'))
            {
                return ParseFactor();
            }

            if (Match('-'))
            {
                return -ParseFactor();
            }

            if (Match('('))
            {
                double value = ParseExpression();
                Expect(')');
                return value;
            }

            if (TryMatchIdentifier("sin"))
            {
                Expect('(');
                double argument = ParseExpression();
                Expect(')');
                return Math.Sin(argument);
            }

            if (TryMatchIdentifier("cos"))
            {
                Expect('(');
                double argument = ParseExpression();
                Expect(')');
                return Math.Cos(argument);
            }

            if (TryMatchVariableT())
            {
                return _timeSeconds;
            }

            if (TryReadNumber(out double number))
            {
                return number;
            }

            throw new FormatException($"无法解析：{_expression[_position..]}");
        }

        private bool TryMatchVariableT()
        {
            if (_position < _expression.Length &&
                (_expression[_position] == 't' || _expression[_position] == 'T'))
            {
                if (_position + 1 < _expression.Length && char.IsLetter(_expression[_position + 1]))
                {
                    return false;
                }

                _position++;
                return true;
            }

            return false;
        }

        private bool TryReadNumber(out double number)
        {
            number = 0;
            int start = _position;
            while (_position < _expression.Length &&
                   (char.IsDigit(_expression[_position]) || _expression[_position] == '.'))
            {
                _position++;
            }

            if (start == _position)
            {
                return false;
            }

            string slice = _expression.Substring(start, _position - start);
            if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                throw new FormatException($"无效数字：{slice}");
            }

            return true;
        }

        private bool Match(char expected)
        {
            if (_position < _expression.Length && _expression[_position] == expected)
            {
                _position++;
                return true;
            }

            return false;
        }

        private void Expect(char expected)
        {
            if (!Match(expected))
            {
                throw new FormatException($"缺少 '{expected}'。");
            }
        }

        private bool TryMatchIdentifier(string identifier)
        {
            if (_position + identifier.Length > _expression.Length)
            {
                return false;
            }

            if (!_expression.AsSpan(_position, identifier.Length)
                    .Equals(identifier.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (_position + identifier.Length < _expression.Length &&
                char.IsLetter(_expression[_position + identifier.Length]))
            {
                return false;
            }

            _position += identifier.Length;
            return true;
        }
    }
}
