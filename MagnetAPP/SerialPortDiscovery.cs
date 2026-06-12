using System;
using System.IO.Ports;
using System.Linq;
using System.Windows.Forms;

namespace MotorControl
{
    internal static class SerialPortDiscovery
    {
        public const int DefaultStartupScanDelayMs = 2000;

        public static string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames()
                .OrderBy(ParseComPortNumber)
                .ThenBy(port => port, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static void RefreshComboBox(ComboBox comboBox)
        {
            string? previousSelection = comboBox.SelectedItem?.ToString();
            string[] ports = GetAvailablePorts();

            comboBox.BeginUpdate();
            try
            {
                comboBox.Items.Clear();
                comboBox.Items.AddRange(ports);

                if (!string.IsNullOrEmpty(previousSelection) && ports.Contains(previousSelection))
                {
                    comboBox.SelectedItem = previousSelection;
                }
                else if (ports.Length > 0)
                {
                    comboBox.SelectedIndex = 0;
                }
                else
                {
                    comboBox.SelectedIndex = -1;
                }
            }
            finally
            {
                comboBox.EndUpdate();
            }
        }

        public static void RefreshComboBoxes(params ComboBox[] comboBoxes)
        {
            foreach (ComboBox comboBox in comboBoxes)
            {
                RefreshComboBox(comboBox);
            }
        }

        private static int ParseComPortNumber(string portName)
        {
            if (portName.Length > 3
                && portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(portName.AsSpan(3), out int number))
            {
                return number;
            }

            return int.MaxValue;
        }
    }
}
