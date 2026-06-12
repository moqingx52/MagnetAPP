using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MotorControl
{
    public sealed class CSVSearchController : IDisposable
    {
        private readonly MainForm _mainForm;
        private readonly CsvSearcher _csvSearcher;

        private readonly TextBox _targetXTextBox;
        private readonly TextBox _targetYTextBox;
        private readonly TextBox _targetZTextBox;
        private readonly Button _searchButton;
        private readonly Label _matchedXLabel;
        private readonly Label _matchedYLabel;
        private readonly Label _matchedZLabel;
        private readonly Label _yawResultLabel;
        private readonly Label _rollResultLabel;
        private readonly RichTextBox _searchLog;

        public string ResultColumn1 { get; private set; } = string.Empty;
        public string ResultColumn2 { get; private set; } = string.Empty;

        public CSVSearchController(MainForm mainForm, TextBox textBoxX, TextBox textBoxY, TextBox textBoxZ,
            Button searchButton, Label labelX, Label labelY, Label labelZ,
            Label labelResult1, Label labelResult2, RichTextBox outputTextBox)
        {
            _mainForm = mainForm;
            _targetXTextBox = textBoxX;
            _targetYTextBox = textBoxY;
            _targetZTextBox = textBoxZ;
            _searchButton = searchButton;
            _matchedXLabel = labelX;
            _matchedYLabel = labelY;
            _matchedZLabel = labelZ;
            _yawResultLabel = labelResult1;
            _rollResultLabel = labelResult2;
            _searchLog = outputTextBox;

            _csvSearcher = new CsvSearcher();
            _csvSearcher.OutputGenerated += OnOutputGenerated;
            _csvSearcher.SearchCompleted += CsvSearcher_SearchCompleted;

            BindEvents();
        }

        private void BindEvents()
        {
            _searchButton.Click += SearchButton_Click;
        }

        private async void SearchButton_Click(object sender, EventArgs e)
        {
            _searchButton.Enabled = false;
            _mainForm.Cursor = Cursors.WaitCursor;
            try
            {
                SearchResultEventArgs? result = await _csvSearcher.SearchInCsvAsync(
                    _targetXTextBox.Text,
                    _targetYTextBox.Text,
                    _targetZTextBox.Text);

                if (result is null)
                {
                    MessageBox.Show("未找到匹配的数据。", "搜索结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(ex.Message, "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                _searchLog.AppendLineSafe($"Error: {ex.Message}");
                MessageBox.Show($"读取CSV文件时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _searchButton.Enabled = true;
                _mainForm.Cursor = Cursors.Default;
            }
        }

        private void OnOutputGenerated(string message)
        {
            _searchLog.AppendLineSafe(message);
        }

        private void CsvSearcher_SearchCompleted(object? sender, SearchResultEventArgs e)
        {
            _yawResultLabel.RunOnUiThread(() => UpdateLabels(e));
        }

        private void UpdateLabels(SearchResultEventArgs e)
        {
            _yawResultLabel.Text = e.ResultColumn1;
            _rollResultLabel.Text = e.ResultColumn2;
            _matchedXLabel.Text = e.MatchedX.ToString("F2");
            _matchedYLabel.Text = e.MatchedY.ToString("F2");
            _matchedZLabel.Text = e.MatchedZ.ToString("F2");

            // 保存结果用于其他模块访问
            ResultColumn1 = e.ResultColumn1;
            ResultColumn2 = e.ResultColumn2;
        }

        public void Dispose()
        {
            _csvSearcher.OutputGenerated -= OnOutputGenerated;
            _csvSearcher.SearchCompleted -= CsvSearcher_SearchCompleted;
            _searchButton.Click -= SearchButton_Click;
        }
    }
}
