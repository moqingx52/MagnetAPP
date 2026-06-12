using System;
using System.Linq;
using System.Windows.Forms;

namespace MotorControl
{
    internal static class WinFormsUi
    {
        public static void RunOnUiThread(this Control control, Action action)
        {
            if (control.InvokeRequired)
            {
                control.Invoke(action);
                return;
            }

            action();
        }

        public static void AppendLineSafe(this RichTextBox textBox, string message)
        {
            textBox.RunOnUiThread(() =>
            {
                textBox.AppendText(message + Environment.NewLine);
                textBox.ScrollToCaret();
            });
        }

        public static void KeepLastLines(this RichTextBox textBox, int maxLines)
        {
            if (textBox.Lines.Length <= maxLines)
            {
                return;
            }

            textBox.Lines = textBox.Lines.Skip(textBox.Lines.Length - maxLines).ToArray();
        }
    }
}
