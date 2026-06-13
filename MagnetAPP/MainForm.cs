using System;
using System.Threading.Tasks;
using System.Drawing;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Forms;
using DLP;
using log4net;

namespace MotorControl
{
    public partial class MainForm : Form, IDisplayControl
    {
        // Core controllers
        private MagneticFieldController? _magneticFieldController;
        private DisplayController? _displayController;
        private KlipperController? _klipperController;
        private MotorPositionController? _motorPositionController;
        private CSVSearchController? _csvSearchController;
        private GCodeController? _gcodeController;

        // Legacy controllers that still need to be accessed
        private MotorController? _motorController;
        private ArduinoCommunication? _arduinoCom;

        private const int PortScanDelayMilliseconds = SerialPortDiscovery.DefaultStartupScanDelayMs;

        // UNO R3: comboBox2 -> UnoDeviceClient -> UNOslave (MOTOR/ENABLE + UVP/UVOFF on one port)
        private UnoDeviceClient? _unoDeviceClient;
        private UltravioletLightUiController? _ultravioletLightUiController;
        private bool _isScanningPorts;


        private bool _isDisposing = false;
        private readonly object _disposeLock = new object();
        private readonly ILog _log = LogManager.GetLogger(typeof(MainForm));

        // UI Components (these will be initialized by InitializeComponent)
        private GroupBox groupBox2;
        private Button button1;
        private Label labelX;
        private TextBox textBoxZ;
        private TextBox textBoxY;
        private TextBox textBoxX;
        private Label labelZ;
        private Label labelY;
        private IContainer? components = null;
        private Button searchButton;
        private Label label4;
        private Label labelResult2;
        private Label labelResult1;
        private RichTextBox outputTextBox;
        private GroupBox groupBox3;
        private Button btnDisplay;
        private Label label1;
        private TextBox txtY2;
        private TextBox txtX2;
        private TextBox txtY1;
        private TextBox txtX1;
        private Label label5;
        private Label label3;
        private Label label2;
        private Button btnBlack;
        private GroupBox groupBox4;
        private Button btnConnect;
        private Button btnSendCommand;
        private TextBox txtKlipperAddress;
        private RichTextBox rtbLog;
        private Label lblStatus;
        private TextBox txtCommand;
        private Label label6;
        private TextBox textBoxGcode;
        private Button buttonGcode;
        private Label label7;
        private Label label8;
        private TextBox textBoxGcode2;
        private Button buttonGcode2;
        private GroupBox groupBox5;
        private Button button2;
        private Label label9;
        private RichTextBox richTextBox1;
        private GroupBox groupBox1;
        private Button button3;
        private TextBox textBox1;
        private Label label10;
        private RichTextBox richTextBox2;
        private RichTextBox richTextBox3;
        private Label label12;
        private Label label11;
        private GroupBox groupBox6;
        private Label label13;
        private Button button4;
        private TextBox textBox2;
        private Label label15;
        private Label label14;
        private RichTextBox richTextBox5;
        private RichTextBox richTextBox4;
        private GroupBox groupBox7;
        private TextBox textBox3;
        private Label label17;
        private Label label16;
        private Button button6;
        private Button button5;
        private TextBox textBox4;
        private RichTextBox richTextBox6;
        private Button button7;
        private Label label18;
        private Label label21;
        private Label label20;
        private Label label19;
        private Button button10;
        private Button button9;
        private Button button8;
        private TextBox textBox5;
        private TextBox textBox7;
        private TextBox textBox6;
        private Label label22;
        private Label label24;
        private Button button11;
        private TextBox textBox8;
        private TextBox textBox10;
        private TextBox textBox9;
        private Label label25;
        private GroupBox groupBox8;
        private Button button13;
        private Button button12;
        private Label label27;
        private Label label26;
        private TrackBar BrightnessBar;
        private Label label28;
        private ComboBox comboBox1;
        private Label label29;
        private ComboBox comboBox2;
        private Label label30;
        private GroupBox groupBox9;
        private Label label33;
        private Label label32;
        private ComboBox comboBox3;
        private Label label31;
        private Button button18;
        private Button button17;
        private Button button16;
        private Button button15;
        private Button button14;
        private Label label35;
        private Label label34;
        private TextBox textBox12;
        private TextBox textBox11;
        private Button button19;
        private TextBox textBox13;
        private Button button20;
        private Label label36;
        private Label label23;

        // Properties for IDisplayControl interface
        public string ResultColumn1 => _csvSearchController?.ResultColumn1 ?? "";
        public string ResultColumn2 => _csvSearchController?.ResultColumn2 ?? "";

        public KlipperCommunicator? Klipper => _klipperController?.Klipper;

        public string? MagneticFieldPort => comboBox1.SelectedItem?.ToString();
        public string? UnoPort => comboBox2.SelectedItem?.ToString();
        public string? Rs485Port => comboBox3.SelectedItem?.ToString();
        public UnoDeviceClient? UnoDevice => _unoDeviceClient;

        public MainForm()
        {
            InitializeComponent();
            ConfigurePortComboBoxes();
            InitializeControllers();
            InitializeModularControllers();
            Shown += MainForm_Shown;
            comboBox2.SelectedIndexChanged += UnoPortComboBox_SelectedIndexChanged;
            button19.Click += Button19_Click;
        }

        // Public methods to expose controlled access to private controls for 3DPrinter class
        public void SetButton4Enabled(bool enabled)
        {
            if (button4 != null)
                button4.Enabled = enabled;
        }

        public string GetFirstTextBox2Line()
        {
            if (textBox2?.Lines?.Length > 0)
                return textBox2.Lines[0];
            return string.Empty;
        }

        public void RemoveFirstTextBox2Line()
        {
            if (textBox2?.Lines?.Length > 0)
            {
                var lines = textBox2.Lines;
                if (lines.Length > 1)
                {
                    textBox2.Lines = lines[1..];
                }
                else
                {
                    textBox2.Lines = new string[0];
                }
            }
        }

        public int GetTextBox2LinesCount()
        {
            return textBox2?.Lines?.Length ?? 0;
        }

        public void AppendToRichTextBox4(string text)
        {
            richTextBox4?.AppendText(text);
        }

        public void AppendToRichTextBox5(string text)
        {
            richTextBox5?.AppendText(text);
        }

        public bool IsKlipperConnected()
        {
            return Klipper?.IsConnected == true;
        }

        public UnoDeviceClient? GetOrConnectUnoDevice(bool showErrors)
        {
            return EnsureUnoDeviceClient(showErrors);
        }

        private bool InitializeControllers()
        {
            try
            {
                //_motorController = new MotorController("COM10");
                _arduinoCom = null;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing controllers: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void ConfigurePortComboBoxes()
        {
            comboBox1.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox2.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox3.DropDownStyle = ComboBoxStyle.DropDownList;
        }

        private async void MainForm_Shown(object? sender, EventArgs e)
        {
            await ScanSerialPortsAsync();
        }

        private async Task ScanSerialPortsAsync()
        {
            if (_isScanningPorts)
            {
                return;
            }

            _isScanningPorts = true;
            try
            {
                await Task.Delay(PortScanDelayMilliseconds);
                this.RunOnUiThread(RefreshPortComboBoxes);
            }
            catch (Exception ex)
            {
                _log.Warn($"Serial port scan failed: {ex.Message}", ex);
            }
            finally
            {
                _isScanningPorts = false;
            }
        }

        private void RefreshPortComboBoxes()
        {
            SerialPortDiscovery.RefreshComboBoxes(comboBox1, comboBox2, comboBox3);
            _log.Info($"Serial ports refreshed: {string.Join(", ", SerialPortDiscovery.GetAvailablePorts())}");
        }

        private void Button19_Click(object? sender, EventArgs e)
        {
            RefreshPortComboBoxes();
        }

        private void UnoPortComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            DisconnectUnoDevice();
        }

        private void DisconnectUnoDevice()
        {
            if (_unoDeviceClient != null)
            {
                _unoDeviceClient.Dispose();
                _unoDeviceClient = null;
            }
        }

        private UnoDeviceClient? EnsureUnoDeviceClient(bool showErrors)
        {
            if (_unoDeviceClient?.IsOpen == true)
            {
                return _unoDeviceClient;
            }

            return TryConnectUnoDevice(showErrors) ? _unoDeviceClient : null;
        }

        private bool TryConnectUnoDevice(bool showErrors)
        {
            string? portName = UnoPort;
            if (string.IsNullOrWhiteSpace(portName))
            {
                if (showErrors)
                {
                    MessageBox.Show(
                        "请先在 UNO R3 下拉列表中选择串口。",
                        "串口未选择",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                return false;
            }

            DisconnectUnoDevice();

            try
            {
                _unoDeviceClient = new UnoDeviceClient(portName);
                _unoDeviceClient.Open();
                _log.Info($"UNO connected on {portName}");
                return true;
            }
            catch (Exception ex)
            {
                _log.Warn($"Failed to open UNO serial port {portName}: {ex.Message}", ex);
                if (showErrors)
                {
                    MessageBox.Show(
                        $"无法打开 UNO 串口 {portName}：{ex.Message}",
                        "串口错误",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                _unoDeviceClient?.Dispose();
                _unoDeviceClient = null;
                return false;
            }
        }

        private void InitializeModularControllers()
        {
            try
            {
                // Initialize all the modular controllers


                _displayController = new DisplayController(
                    this, txtX1, txtX2, txtY1, txtY2, btnDisplay, btnBlack);

                _klipperController = new KlipperController(
                    this, txtKlipperAddress, txtCommand, textBoxGcode, textBoxGcode2,
                    btnConnect, btnSendCommand, buttonGcode, buttonGcode2, rtbLog, lblStatus,
                    textBox1);

                _motorPositionController = new MotorPositionController(
                    this, _klipperController, textBox5, textBox6, textBox7, textBox9, textBox10,
                    textBox8, button8, button9, button10, button11, label19, label20, label21,
                    txtX1, txtX2, txtY1, txtY2);

                _magneticFieldController = new MagneticFieldController(
                    this, richTextBox1, button2, labelX, labelY, labelZ, _motorController,
                    _arduinoCom, textBox3, textBox4, button5, button6, button7, richTextBox6);

                _csvSearchController = new CSVSearchController(
                    this, textBoxX, textBoxY, textBoxZ, searchButton, labelX, labelY, labelZ,
                    labelResult1, labelResult2, outputTextBox);


                DisplayManager.Instance.Initialize(this);

                _ultravioletLightUiController = new UltravioletLightUiController(
                    this,
                    EnsureUnoDeviceClient,
                    BrightnessBar,
                    button12,
                    button13,
                    label28);

                _gcodeController = new GCodeController(
                    this, _klipperController, _magneticFieldController, _ultravioletLightUiController,
                    textBox2, button4, richTextBox4, richTextBox5, textBox13, button20);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize modular controllers: {ex.Message}",
                    "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void InitializeComponent()
        {
            groupBox2 = new GroupBox();
            outputTextBox = new RichTextBox();
            labelResult2 = new Label();
            labelResult1 = new Label();
            label4 = new Label();
            searchButton = new Button();
            labelZ = new Label();
            labelY = new Label();
            labelX = new Label();
            textBoxZ = new TextBox();
            textBoxY = new TextBox();
            textBoxX = new TextBox();
            button1 = new Button();
            groupBox3 = new GroupBox();
            btnBlack = new Button();
            label5 = new Label();
            label3 = new Label();
            label2 = new Label();
            label1 = new Label();
            txtY2 = new TextBox();
            txtX2 = new TextBox();
            txtY1 = new TextBox();
            txtX1 = new TextBox();
            btnDisplay = new Button();
            groupBox4 = new GroupBox();
            label25 = new Label();
            label23 = new Label();
            textBox10 = new TextBox();
            textBox9 = new TextBox();
            label24 = new Label();
            button11 = new Button();
            textBox8 = new TextBox();
            label22 = new Label();
            textBox7 = new TextBox();
            textBox6 = new TextBox();
            textBox5 = new TextBox();
            label21 = new Label();
            label20 = new Label();
            label19 = new Label();
            button10 = new Button();
            button9 = new Button();
            button8 = new Button();
            buttonGcode2 = new Button();
            label6 = new Label();
            textBoxGcode2 = new TextBox();
            lblStatus = new Label();
            label8 = new Label();
            label7 = new Label();
            txtCommand = new TextBox();
            buttonGcode = new Button();
            rtbLog = new RichTextBox();
            textBoxGcode = new TextBox();
            txtKlipperAddress = new TextBox();
            btnSendCommand = new Button();
            btnConnect = new Button();
            groupBox5 = new GroupBox();
            comboBox1 = new ComboBox();
            label29 = new Label();
            button2 = new Button();
            label9 = new Label();
            richTextBox1 = new RichTextBox();
            groupBox1 = new GroupBox();
            richTextBox3 = new RichTextBox();
            label12 = new Label();
            label11 = new Label();
            richTextBox2 = new RichTextBox();
            button3 = new Button();
            textBox1 = new TextBox();
            label10 = new Label();
            groupBox6 = new GroupBox();
            label36 = new Label();
            textBox13 = new TextBox();
            button20 = new Button();
            label15 = new Label();
            label14 = new Label();
            richTextBox5 = new RichTextBox();
            richTextBox4 = new RichTextBox();
            label13 = new Label();
            button4 = new Button();
            textBox2 = new TextBox();
            groupBox7 = new GroupBox();
            button7 = new Button();
            label18 = new Label();
            richTextBox6 = new RichTextBox();
            textBox4 = new TextBox();
            textBox3 = new TextBox();
            label17 = new Label();
            label16 = new Label();
            button6 = new Button();
            button5 = new Button();
            groupBox8 = new GroupBox();
            label28 = new Label();
            button13 = new Button();
            button12 = new Button();
            label27 = new Label();
            label26 = new Label();
            BrightnessBar = new TrackBar();
            comboBox2 = new ComboBox();
            label30 = new Label();
            groupBox9 = new GroupBox();
            button18 = new Button();
            button17 = new Button();
            button16 = new Button();
            button15 = new Button();
            button14 = new Button();
            label35 = new Label();
            label34 = new Label();
            textBox12 = new TextBox();
            textBox11 = new TextBox();
            label33 = new Label();
            label32 = new Label();
            comboBox3 = new ComboBox();
            label31 = new Label();
            button19 = new Button();
            groupBox2.SuspendLayout();
            groupBox3.SuspendLayout();
            groupBox4.SuspendLayout();
            groupBox5.SuspendLayout();
            groupBox1.SuspendLayout();
            groupBox6.SuspendLayout();
            groupBox7.SuspendLayout();
            groupBox8.SuspendLayout();
            ((ISupportInitialize)BrightnessBar).BeginInit();
            groupBox9.SuspendLayout();
            SuspendLayout();
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(outputTextBox);
            groupBox2.Controls.Add(labelResult2);
            groupBox2.Controls.Add(labelResult1);
            groupBox2.Controls.Add(label4);
            groupBox2.Controls.Add(searchButton);
            groupBox2.Controls.Add(labelZ);
            groupBox2.Controls.Add(labelY);
            groupBox2.Controls.Add(labelX);
            groupBox2.Controls.Add(textBoxZ);
            groupBox2.Controls.Add(textBoxY);
            groupBox2.Controls.Add(textBoxX);
            groupBox2.Controls.Add(button1);
            groupBox2.Location = new Point(24, 286);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(415, 323);
            groupBox2.TabIndex = 17;
            groupBox2.TabStop = false;
            groupBox2.Text = "磁场方向";
            // 
            // outputTextBox
            // 
            outputTextBox.Location = new Point(31, 178);
            outputTextBox.Name = "outputTextBox";
            outputTextBox.ReadOnly = true;
            outputTextBox.ScrollBars = RichTextBoxScrollBars.Vertical;
            outputTextBox.Size = new Size(367, 120);
            outputTextBox.TabIndex = 24;
            outputTextBox.Text = "";
            // 
            // labelResult2
            // 
            labelResult2.AutoSize = true;
            labelResult2.Location = new Point(304, 104);
            labelResult2.Name = "labelResult2";
            labelResult2.Size = new Size(36, 20);
            labelResult2.TabIndex = 23;
            labelResult2.Text = "000";
            // 
            // labelResult1
            // 
            labelResult1.AutoSize = true;
            labelResult1.ForeColor = SystemColors.ActiveCaptionText;
            labelResult1.Location = new Point(304, 71);
            labelResult1.Name = "labelResult1";
            labelResult1.Size = new Size(36, 20);
            labelResult1.TabIndex = 22;
            labelResult1.Text = "000";
            // 
            // label4
            // 
            label4.Location = new Point(0, 0);
            label4.Name = "label4";
            label4.Size = new Size(100, 23);
            label4.TabIndex = 0;
            // 
            // searchButton
            // 
            searchButton.Location = new Point(304, 31);
            searchButton.Name = "searchButton";
            searchButton.Size = new Size(94, 29);
            searchButton.TabIndex = 21;
            searchButton.Text = "搜索";
            searchButton.UseVisualStyleBackColor = true;
            // 
            // labelZ
            // 
            labelZ.AutoSize = true;
            labelZ.Location = new Point(20, 100);
            labelZ.Name = "labelZ";
            labelZ.Size = new Size(61, 20);
            labelZ.TabIndex = 20;
            labelZ.Text = "z轴夹角";
            // 
            // labelY
            // 
            labelY.AutoSize = true;
            labelY.Location = new Point(20, 67);
            labelY.Name = "labelY";
            labelY.Size = new Size(62, 20);
            labelY.TabIndex = 20;
            labelY.Text = "y轴夹角";
            // 
            // labelX
            // 
            labelX.AutoSize = true;
            labelX.Location = new Point(20, 31);
            labelX.Name = "labelX";
            labelX.Size = new Size(62, 20);
            labelX.TabIndex = 20;
            labelX.Text = "x轴夹角";
            // 
            // textBoxZ
            // 
            textBoxZ.Location = new Point(145, 97);
            textBoxZ.Name = "textBoxZ";
            textBoxZ.Size = new Size(125, 27);
            textBoxZ.TabIndex = 19;
            // 
            // textBoxY
            // 
            textBoxY.Location = new Point(145, 64);
            textBoxY.Name = "textBoxY";
            textBoxY.Size = new Size(125, 27);
            textBoxY.TabIndex = 19;
            // 
            // textBoxX
            // 
            textBoxX.Location = new Point(145, 31);
            textBoxX.Name = "textBoxX";
            textBoxX.Size = new Size(125, 27);
            textBoxX.TabIndex = 19;
            // 
            // button1
            // 
            button1.Location = new Point(0, 0);
            button1.Name = "button1";
            button1.Size = new Size(75, 23);
            button1.TabIndex = 20;
            // 
            // groupBox3
            // 
            groupBox3.Controls.Add(btnBlack);
            groupBox3.Controls.Add(label5);
            groupBox3.Controls.Add(label3);
            groupBox3.Controls.Add(label2);
            groupBox3.Controls.Add(label1);
            groupBox3.Controls.Add(txtY2);
            groupBox3.Controls.Add(txtX2);
            groupBox3.Controls.Add(txtY1);
            groupBox3.Controls.Add(txtX1);
            groupBox3.Controls.Add(btnDisplay);
            groupBox3.Location = new Point(24, 640);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(415, 215);
            groupBox3.TabIndex = 18;
            groupBox3.TabStop = false;
            groupBox3.Text = "屏幕控制";
            // 
            // btnBlack
            // 
            btnBlack.Location = new Point(45, 167);
            btnBlack.Name = "btnBlack";
            btnBlack.Size = new Size(94, 29);
            btnBlack.TabIndex = 6;
            btnBlack.Text = "重置";
            btnBlack.UseVisualStyleBackColor = true;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(196, 113);
            label5.Name = "label5";
            label5.Size = new Size(27, 20);
            label5.TabIndex = 5;
            label5.Text = "Y2";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(12, 113);
            label3.Name = "label3";
            label3.Size = new Size(27, 20);
            label3.TabIndex = 4;
            label3.Text = "Y1";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(195, 46);
            label2.Name = "label2";
            label2.Size = new Size(28, 20);
            label2.TabIndex = 3;
            label2.Text = "X2";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(11, 46);
            label1.Name = "label1";
            label1.Size = new Size(28, 20);
            label1.TabIndex = 2;
            label1.Text = "X1";
            // 
            // txtY2
            // 
            txtY2.Location = new Point(229, 113);
            txtY2.Name = "txtY2";
            txtY2.Size = new Size(125, 27);
            txtY2.TabIndex = 1;
            txtY2.Text = "200";
            // 
            // txtX2
            // 
            txtX2.Location = new Point(229, 46);
            txtX2.Name = "txtX2";
            txtX2.Size = new Size(125, 27);
            txtX2.TabIndex = 1;
            txtX2.Text = "200";
            // 
            // txtY1
            // 
            txtY1.Location = new Point(45, 113);
            txtY1.Name = "txtY1";
            txtY1.Size = new Size(125, 27);
            txtY1.TabIndex = 1;
            txtY1.Text = "100";
            // 
            // txtX1
            // 
            txtX1.Location = new Point(45, 46);
            txtX1.Name = "txtX1";
            txtX1.Size = new Size(125, 27);
            txtX1.TabIndex = 1;
            txtX1.Text = "100";
            // 
            // btnDisplay
            // 
            btnDisplay.Location = new Point(229, 167);
            btnDisplay.Name = "btnDisplay";
            btnDisplay.Size = new Size(94, 29);
            btnDisplay.TabIndex = 0;
            btnDisplay.Text = "副屏显示";
            btnDisplay.UseVisualStyleBackColor = true;
            // 
            // groupBox4
            // 
            groupBox4.Controls.Add(label25);
            groupBox4.Controls.Add(label23);
            groupBox4.Controls.Add(textBox10);
            groupBox4.Controls.Add(textBox9);
            groupBox4.Controls.Add(label24);
            groupBox4.Controls.Add(button11);
            groupBox4.Controls.Add(textBox8);
            groupBox4.Controls.Add(label22);
            groupBox4.Controls.Add(textBox7);
            groupBox4.Controls.Add(textBox6);
            groupBox4.Controls.Add(textBox5);
            groupBox4.Controls.Add(label21);
            groupBox4.Controls.Add(label20);
            groupBox4.Controls.Add(label19);
            groupBox4.Controls.Add(button10);
            groupBox4.Controls.Add(button9);
            groupBox4.Controls.Add(button8);
            groupBox4.Controls.Add(buttonGcode2);
            groupBox4.Controls.Add(label6);
            groupBox4.Controls.Add(textBoxGcode2);
            groupBox4.Controls.Add(lblStatus);
            groupBox4.Controls.Add(label8);
            groupBox4.Controls.Add(label7);
            groupBox4.Controls.Add(txtCommand);
            groupBox4.Controls.Add(buttonGcode);
            groupBox4.Controls.Add(rtbLog);
            groupBox4.Controls.Add(textBoxGcode);
            groupBox4.Controls.Add(txtKlipperAddress);
            groupBox4.Controls.Add(btnSendCommand);
            groupBox4.Controls.Add(btnConnect);
            groupBox4.Location = new Point(455, 12);
            groupBox4.Name = "groupBox4";
            groupBox4.Size = new Size(594, 671);
            groupBox4.TabIndex = 19;
            groupBox4.TabStop = false;
            groupBox4.Text = "klipper功能";
            // 
            // label25
            // 
            label25.AutoSize = true;
            label25.Location = new Point(344, 494);
            label25.Name = "label25";
            label25.Size = new Size(48, 20);
            label25.TabIndex = 46;
            label25.Text = "Y位置";
            // 
            // label23
            // 
            label23.AutoSize = true;
            label23.Location = new Point(114, 494);
            label23.Name = "label23";
            label23.Size = new Size(49, 20);
            label23.TabIndex = 45;
            label23.Text = "X位置";
            // 
            // textBox10
            // 
            textBox10.Location = new Point(431, 491);
            textBox10.Name = "textBox10";
            textBox10.Size = new Size(125, 27);
            textBox10.TabIndex = 44;
            // 
            // textBox9
            // 
            textBox9.Location = new Point(194, 494);
            textBox9.Name = "textBox9";
            textBox9.Size = new Size(125, 27);
            textBox9.TabIndex = 43;
            // 
            // label24
            // 
            label24.AutoSize = true;
            label24.Location = new Point(16, 491);
            label24.Name = "label24";
            label24.Size = new Size(69, 20);
            label24.TabIndex = 42;
            label24.Text = "对正计算";
            // 
            // button11
            // 
            button11.Location = new Point(16, 529);
            button11.Name = "button11";
            button11.Size = new Size(94, 29);
            button11.TabIndex = 40;
            button11.Text = "计算";
            button11.UseVisualStyleBackColor = true;
            // 
            // textBox8
            // 
            textBox8.Location = new Point(141, 530);
            textBox8.Name = "textBox8";
            textBox8.Size = new Size(415, 27);
            textBox8.TabIndex = 39;
            // 
            // label22
            // 
            label22.AutoSize = true;
            label22.Location = new Point(4, 352);
            label22.Name = "label22";
            label22.Size = new Size(39, 20);
            label22.TabIndex = 38;
            label22.Text = "位置";
            // 
            // textBox7
            // 
            textBox7.Location = new Point(56, 443);
            textBox7.Name = "textBox7";
            textBox7.Size = new Size(125, 27);
            textBox7.TabIndex = 37;
            textBox7.Text = "0";
            // 
            // textBox6
            // 
            textBox6.Location = new Point(56, 410);
            textBox6.Name = "textBox6";
            textBox6.Size = new Size(125, 27);
            textBox6.TabIndex = 36;
            textBox6.Text = "0";
            // 
            // textBox5
            // 
            textBox5.Location = new Point(56, 378);
            textBox5.Name = "textBox5";
            textBox5.Size = new Size(125, 27);
            textBox5.TabIndex = 35;
            textBox5.Text = "0";
            // 
            // label21
            // 
            label21.AutoSize = true;
            label21.Location = new Point(4, 450);
            label21.Name = "label21";
            label21.Size = new Size(18, 20);
            label21.TabIndex = 34;
            label21.Text = "0";
            // 
            // label20
            // 
            label20.AutoSize = true;
            label20.Location = new Point(4, 417);
            label20.Name = "label20";
            label20.Size = new Size(18, 20);
            label20.TabIndex = 33;
            label20.Text = "0";
            // 
            // label19
            // 
            label19.AutoSize = true;
            label19.Location = new Point(4, 385);
            label19.Name = "label19";
            label19.Size = new Size(18, 20);
            label19.TabIndex = 32;
            label19.Text = "0";
            // 
            // button10
            // 
            button10.Location = new Point(243, 446);
            button10.Name = "button10";
            button10.Size = new Size(94, 29);
            button10.TabIndex = 28;
            button10.Text = "Z位移";
            button10.UseVisualStyleBackColor = true;
            // 
            // button9
            // 
            button9.Location = new Point(243, 411);
            button9.Name = "button9";
            button9.Size = new Size(94, 29);
            button9.TabIndex = 27;
            button9.Text = "Y位移";
            button9.UseVisualStyleBackColor = true;
            // 
            // button8
            // 
            button8.Location = new Point(243, 376);
            button8.Name = "button8";
            button8.Size = new Size(94, 29);
            button8.TabIndex = 26;
            button8.Text = "X位移";
            button8.UseVisualStyleBackColor = true;
            // 
            // buttonGcode2
            // 
            buttonGcode2.Location = new Point(443, 623);
            buttonGcode2.Name = "buttonGcode2";
            buttonGcode2.Size = new Size(94, 29);
            buttonGcode2.TabIndex = 25;
            buttonGcode2.Text = "发送";
            buttonGcode2.UseVisualStyleBackColor = true;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(6, 94);
            label6.Name = "label6";
            label6.Size = new Size(57, 20);
            label6.TabIndex = 6;
            label6.Text = "Gcode";
            // 
            // textBoxGcode2
            // 
            textBoxGcode2.Location = new Point(157, 623);
            textBoxGcode2.Name = "textBoxGcode2";
            textBoxGcode2.Size = new Size(259, 27);
            textBoxGcode2.TabIndex = 24;
            // 
            // lblStatus
            // 
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(6, 42);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(39, 20);
            lblStatus.TabIndex = 5;
            lblStatus.Text = "状态";
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Location = new Point(14, 623);
            label8.Name = "label8";
            label8.Size = new Size(121, 20);
            label8.TabIndex = 23;
            label8.Text = "执行GCODE文件";
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(14, 578);
            label7.Name = "label7";
            label7.Size = new Size(103, 20);
            label7.TabIndex = 22;
            label7.Text = "xlsx转GCODE";
            // 
            // txtCommand
            // 
            txtCommand.Location = new Point(122, 87);
            txtCommand.Name = "txtCommand";
            txtCommand.Size = new Size(326, 27);
            txtCommand.TabIndex = 4;
            // 
            // buttonGcode
            // 
            buttonGcode.Location = new Point(442, 578);
            buttonGcode.Name = "buttonGcode";
            buttonGcode.Size = new Size(94, 29);
            buttonGcode.TabIndex = 21;
            buttonGcode.Text = "处理";
            buttonGcode.UseVisualStyleBackColor = true;
            // 
            // rtbLog
            // 
            rtbLog.Location = new Point(6, 131);
            rtbLog.Name = "rtbLog";
            rtbLog.Size = new Size(570, 209);
            rtbLog.TabIndex = 3;
            rtbLog.Text = "";
            // 
            // textBoxGcode
            // 
            textBoxGcode.Location = new Point(157, 578);
            textBoxGcode.Name = "textBoxGcode";
            textBoxGcode.Size = new Size(259, 27);
            textBoxGcode.TabIndex = 20;
            textBoxGcode.Text = "C:\\Users\\Administrator\\Desktop";
            // 
            // txtKlipperAddress
            // 
            txtKlipperAddress.Location = new Point(122, 39);
            txtKlipperAddress.Name = "txtKlipperAddress";
            txtKlipperAddress.Size = new Size(326, 27);
            txtKlipperAddress.TabIndex = 2;
            txtKlipperAddress.Text = "192.168.1.105";
            // 
            // btnSendCommand
            // 
            btnSendCommand.Location = new Point(484, 85);
            btnSendCommand.Name = "btnSendCommand";
            btnSendCommand.Size = new Size(94, 29);
            btnSendCommand.TabIndex = 1;
            btnSendCommand.Text = "单句发送";
            btnSendCommand.UseVisualStyleBackColor = true;
            // 
            // btnConnect
            // 
            btnConnect.Location = new Point(484, 37);
            btnConnect.Name = "btnConnect";
            btnConnect.Size = new Size(94, 29);
            btnConnect.TabIndex = 0;
            btnConnect.Text = "连接";
            btnConnect.UseVisualStyleBackColor = true;
            // 
            // groupBox5
            // 
            groupBox5.Controls.Add(comboBox1);
            groupBox5.Controls.Add(label29);
            groupBox5.Controls.Add(button2);
            groupBox5.Controls.Add(label9);
            groupBox5.Controls.Add(richTextBox1);
            groupBox5.Location = new Point(12, 12);
            groupBox5.Name = "groupBox5";
            groupBox5.Size = new Size(424, 251);
            groupBox5.TabIndex = 20;
            groupBox5.TabStop = false;
            groupBox5.Text = "磁场读取";
            // 
            // comboBox1
            // 
            comboBox1.FormattingEnabled = true;
            comboBox1.Location = new Point(57, 31);
            comboBox1.Name = "comboBox1";
            comboBox1.Size = new Size(105, 28);
            comboBox1.TabIndex = 4;
            // 
            // label29
            // 
            label29.AutoSize = true;
            label29.Location = new Point(11, 34);
            label29.Name = "label29";
            label29.Size = new Size(39, 20);
            label29.TabIndex = 3;
            label29.Text = "端口";
            // 
            // button2
            // 
            button2.Location = new Point(321, 26);
            button2.Name = "button2";
            button2.Size = new Size(94, 29);
            button2.TabIndex = 2;
            button2.Text = "开始读取";
            button2.UseVisualStyleBackColor = true;
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Location = new Point(168, 46);
            label9.Name = "label9";
            label9.Size = new Size(69, 20);
            label9.TabIndex = 1;
            label9.Text = "磁场数值";
            // 
            // richTextBox1
            // 
            richTextBox1.Location = new Point(11, 75);
            richTextBox1.Name = "richTextBox1";
            richTextBox1.Size = new Size(404, 120);
            richTextBox1.TabIndex = 0;
            richTextBox1.Text = "";
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(richTextBox3);
            groupBox1.Controls.Add(label12);
            groupBox1.Controls.Add(label11);
            groupBox1.Controls.Add(richTextBox2);
            groupBox1.Controls.Add(button3);
            groupBox1.Controls.Add(textBox1);
            groupBox1.Controls.Add(label10);
            groupBox1.Location = new Point(1078, 12);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(579, 26);
            groupBox1.TabIndex = 21;
            groupBox1.TabStop = false;
            groupBox1.Text = "屏幕跟随测试";
            // 
            // richTextBox3
            // 
            richTextBox3.Location = new Point(18, 318);
            richTextBox3.Name = "richTextBox3";
            richTextBox3.Size = new Size(435, 47);
            richTextBox3.TabIndex = 6;
            richTextBox3.Text = "";
            // 
            // label12
            // 
            label12.AutoSize = true;
            label12.Location = new Point(18, 277);
            label12.Name = "label12";
            label12.Size = new Size(99, 20);
            label12.TabIndex = 5;
            label12.Text = "点亮像素范围";
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.Location = new Point(18, 112);
            label11.Name = "label11";
            label11.Size = new Size(80, 20);
            label11.TabIndex = 4;
            label11.Text = "发送G代码";
            // 
            // richTextBox2
            // 
            richTextBox2.Location = new Point(18, 145);
            richTextBox2.Name = "richTextBox2";
            richTextBox2.Size = new Size(435, 120);
            richTextBox2.TabIndex = 3;
            richTextBox2.Text = "";
            // 
            // button3
            // 
            button3.Location = new Point(470, 67);
            button3.Name = "button3";
            button3.Size = new Size(94, 29);
            button3.TabIndex = 2;
            button3.Text = "开始执行";
            button3.UseVisualStyleBackColor = true;
            // 
            // textBox1
            // 
            textBox1.Location = new Point(18, 69);
            textBox1.Name = "textBox1";
            textBox1.Size = new Size(435, 27);
            textBox1.TabIndex = 1;
            // 
            // label10
            // 
            label10.AutoSize = true;
            label10.Location = new Point(18, 46);
            label10.Name = "label10";
            label10.Size = new Size(106, 20);
            label10.TabIndex = 0;
            label10.Text = "处理后GCODE";
            // 
            // groupBox6
            // 
            groupBox6.Controls.Add(label36);
            groupBox6.Controls.Add(textBox13);
            groupBox6.Controls.Add(button20);
            groupBox6.Controls.Add(label15);
            groupBox6.Controls.Add(label14);
            groupBox6.Controls.Add(richTextBox5);
            groupBox6.Controls.Add(richTextBox4);
            groupBox6.Controls.Add(label13);
            groupBox6.Controls.Add(button4);
            groupBox6.Controls.Add(textBox2);
            groupBox6.Location = new Point(1078, 40);
            groupBox6.Name = "groupBox6";
            groupBox6.Size = new Size(579, 385);
            groupBox6.TabIndex = 22;
            groupBox6.TabStop = false;
            groupBox6.Text = "GCODE执行";
            // 
            // label36
            // 
            label36.AutoSize = true;
            label36.Location = new Point(459, 33);
            label36.Name = "label36";
            label36.Size = new Size(18, 20);
            label36.TabIndex = 29;
            label36.Text = "S";
            // 
            // textBox13
            // 
            textBox13.Location = new Point(245, 26);
            textBox13.Name = "textBox13";
            textBox13.Size = new Size(208, 27);
            textBox13.TabIndex = 7;
            textBox13.Text = "60";
            // 
            // button20
            // 
            button20.Location = new Point(479, 28);
            button20.Name = "button20";
            button20.Size = new Size(94, 29);
            button20.TabIndex = 6;
            button20.Text = "确认间隔";
            button20.UseVisualStyleBackColor = true;
            // 
            // label15
            // 
            label15.AutoSize = true;
            label15.Location = new Point(479, 238);
            label15.Name = "label15";
            label15.Size = new Size(69, 20);
            label15.TabIndex = 5;
            label15.Text = "点亮像素";
            // 
            // label14
            // 
            label14.AutoSize = true;
            label14.Location = new Point(479, 109);
            label14.Name = "label14";
            label14.Size = new Size(50, 20);
            label14.TabIndex = 4;
            label14.Text = "G代码";
            // 
            // richTextBox5
            // 
            richTextBox5.Location = new Point(18, 235);
            richTextBox5.Name = "richTextBox5";
            richTextBox5.Size = new Size(435, 115);
            richTextBox5.TabIndex = 3;
            richTextBox5.Text = "";
            // 
            // richTextBox4
            // 
            richTextBox4.Location = new Point(18, 106);
            richTextBox4.Name = "richTextBox4";
            richTextBox4.Size = new Size(435, 115);
            richTextBox4.TabIndex = 3;
            richTextBox4.Text = "";
            // 
            // label13
            // 
            label13.AutoSize = true;
            label13.Location = new Point(18, 34);
            label13.Name = "label13";
            label13.Size = new Size(87, 20);
            label13.TabIndex = 2;
            label13.Text = "Gcode文件";
            // 
            // button4
            // 
            button4.Location = new Point(479, 57);
            button4.Name = "button4";
            button4.Size = new Size(94, 29);
            button4.TabIndex = 1;
            button4.Text = "开始执行";
            button4.UseVisualStyleBackColor = true;
            // 
            // textBox2
            // 
            textBox2.Location = new Point(18, 59);
            textBox2.Name = "textBox2";
            textBox2.Size = new Size(435, 27);
            textBox2.TabIndex = 0;
            textBox2.Text = "C:\\研究生\\klipper\\TEST1.gcode";
            // 
            // groupBox7
            // 
            groupBox7.Controls.Add(button7);
            groupBox7.Controls.Add(label18);
            groupBox7.Controls.Add(richTextBox6);
            groupBox7.Controls.Add(textBox4);
            groupBox7.Controls.Add(textBox3);
            groupBox7.Controls.Add(label17);
            groupBox7.Controls.Add(label16);
            groupBox7.Controls.Add(button6);
            groupBox7.Controls.Add(button5);
            groupBox7.Location = new Point(1078, 628);
            groupBox7.Name = "groupBox7";
            groupBox7.Size = new Size(579, 227);
            groupBox7.TabIndex = 23;
            groupBox7.TabStop = false;
            groupBox7.Text = "磁铁位姿控制";
            // 
            // button7
            // 
            button7.Location = new Point(436, 92);
            button7.Name = "button7";
            button7.Size = new Size(94, 29);
            button7.TabIndex = 10;
            button7.Text = "开始更新";
            button7.UseVisualStyleBackColor = true;
            // 
            // label18
            // 
            label18.AutoSize = true;
            label18.Location = new Point(37, 101);
            label18.Name = "label18";
            label18.Size = new Size(69, 20);
            label18.TabIndex = 9;
            label18.Text = "磁场位置/闭环";
            // 
            // richTextBox6
            // 
            richTextBox6.Location = new Point(34, 134);
            richTextBox6.Name = "richTextBox6";
            richTextBox6.Size = new Size(496, 75);
            richTextBox6.TabIndex = 8;
            richTextBox6.Text = "";
            // 
            // textBox4
            // 
            textBox4.Location = new Point(138, 62);
            textBox4.Name = "textBox4";
            textBox4.Size = new Size(125, 27);
            textBox4.TabIndex = 7;
            // 
            // textBox3
            // 
            textBox3.Location = new Point(138, 29);
            textBox3.Name = "textBox3";
            textBox3.Size = new Size(125, 27);
            textBox3.TabIndex = 6;
            // 
            // label17
            // 
            label17.AutoSize = true;
            label17.Location = new Point(36, 65);
            label17.Name = "label17";
            label17.Size = new Size(84, 20);
            label17.TabIndex = 5;
            label17.Text = "俯仰/滚转位置";
            // 
            // label16
            // 
            label16.AutoSize = true;
            label16.Location = new Point(36, 32);
            label16.Name = "label16";
            label16.Size = new Size(84, 20);
            label16.TabIndex = 4;
            label16.Text = "偏航位置";
            // 
            // button6
            // 
            button6.Location = new Point(287, 63);
            button6.Name = "button6";
            button6.Size = new Size(94, 29);
            button6.TabIndex = 1;
            button6.Text = "设置";
            button6.UseVisualStyleBackColor = true;
            // 
            // button5
            // 
            button5.Location = new Point(287, 26);
            button5.Name = "button5";
            button5.Size = new Size(94, 29);
            button5.TabIndex = 0;
            button5.Text = "设置";
            button5.UseVisualStyleBackColor = true;
            // 
            // groupBox8
            // 
            groupBox8.Controls.Add(label28);
            groupBox8.Controls.Add(button13);
            groupBox8.Controls.Add(button12);
            groupBox8.Controls.Add(label27);
            groupBox8.Controls.Add(label26);
            groupBox8.Controls.Add(BrightnessBar);
            groupBox8.Location = new Point(1078, 469);
            groupBox8.Name = "groupBox8";
            groupBox8.Size = new Size(579, 150);
            groupBox8.TabIndex = 24;
            groupBox8.TabStop = false;
            groupBox8.Text = "紫外光源亮度控制";
            // 
            // label28
            // 
            label28.AutoSize = true;
            label28.Location = new Point(259, 109);
            label28.Name = "label28";
            label28.Size = new Size(54, 20);
            label28.TabIndex = 5;
            label28.Text = "灯：关";
            // 
            // button13
            // 
            button13.Location = new Point(444, 102);
            button13.Name = "button13";
            button13.Size = new Size(94, 29);
            button13.TabIndex = 4;
            button13.Text = "关灯";
            button13.UseVisualStyleBackColor = true;
            // 
            // button12
            // 
            button12.Location = new Point(30, 102);
            button12.Name = "button12";
            button12.Size = new Size(94, 29);
            button12.TabIndex = 3;
            button12.Text = "开灯";
            button12.UseVisualStyleBackColor = true;
            // 
            // label27
            // 
            label27.AutoSize = true;
            label27.Location = new Point(502, 40);
            label27.Name = "label27";
            label27.Size = new Size(36, 20);
            label27.TabIndex = 2;
            label27.Text = "100";
            // 
            // label26
            // 
            label26.AutoSize = true;
            label26.Location = new Point(39, 40);
            label26.Name = "label26";
            label26.Size = new Size(18, 20);
            label26.TabIndex = 1;
            label26.Text = "0";
            // 
            // BrightnessBar
            // 
            BrightnessBar.Location = new Point(107, 38);
            BrightnessBar.Name = "BrightnessBar";
            BrightnessBar.Size = new Size(368, 56);
            BrightnessBar.TabIndex = 0;
            // 
            // comboBox2
            // 
            comboBox2.FormattingEnabled = true;
            comboBox2.Location = new Point(1216, 425);
            comboBox2.Name = "comboBox2";
            comboBox2.Size = new Size(151, 28);
            comboBox2.TabIndex = 25;
            // 
            // label30
            // 
            label30.AutoSize = true;
            label30.Location = new Point(1078, 428);
            label30.Name = "label30";
            label30.Size = new Size(67, 20);
            label30.TabIndex = 26;
            label30.Text = "UNO R3";
            // 
            // groupBox9
            // 
            groupBox9.Controls.Add(button18);
            groupBox9.Controls.Add(button17);
            groupBox9.Controls.Add(button16);
            groupBox9.Controls.Add(button15);
            groupBox9.Controls.Add(button14);
            groupBox9.Controls.Add(label35);
            groupBox9.Controls.Add(label34);
            groupBox9.Controls.Add(textBox12);
            groupBox9.Controls.Add(textBox11);
            groupBox9.Controls.Add(label33);
            groupBox9.Controls.Add(label32);
            groupBox9.Controls.Add(comboBox3);
            groupBox9.Controls.Add(label31);
            groupBox9.Location = new Point(455, 693);
            groupBox9.Name = "groupBox9";
            groupBox9.Size = new Size(594, 252);
            groupBox9.TabIndex = 27;
            groupBox9.TabStop = false;
            groupBox9.Text = "磁场发生器";
            // 
            // button18
            // 
            button18.Location = new Point(237, 91);
            button18.Name = "button18";
            button18.Size = new Size(94, 29);
            button18.TabIndex = 12;
            button18.Text = "后退";
            button18.UseVisualStyleBackColor = true;
            // 
            // button17
            // 
            button17.Location = new Point(125, 91);
            button17.Name = "button17";
            button17.Size = new Size(94, 29);
            button17.TabIndex = 11;
            button17.Text = "前进";
            button17.UseVisualStyleBackColor = true;
            // 
            // button16
            // 
            button16.Location = new Point(127, 144);
            button16.Name = "button16";
            button16.Size = new Size(71, 29);
            button16.TabIndex = 10;
            button16.Text = "stop";
            button16.UseVisualStyleBackColor = true;
            // 
            // button15
            // 
            button15.Location = new Point(479, 144);
            button15.Name = "button15";
            button15.Size = new Size(50, 29);
            button15.TabIndex = 9;
            button15.Text = "-";
            button15.UseVisualStyleBackColor = true;
            // 
            // button14
            // 
            button14.Location = new Point(420, 143);
            button14.Name = "button14";
            button14.Size = new Size(45, 29);
            button14.TabIndex = 8;
            button14.Text = "+";
            button14.UseVisualStyleBackColor = true;
            // 
            // label35
            // 
            label35.AutoSize = true;
            label35.Location = new Point(368, 149);
            label35.Name = "label35";
            label35.Size = new Size(46, 20);
            label35.TabIndex = 7;
            label35.Text = "rad/s";
            // 
            // label34
            // 
            label34.AutoSize = true;
            label34.Location = new Point(22, 199);
            label34.Name = "label34";
            label34.Size = new Size(159, 20);
            label34.TabIndex = 6;
            label34.Text = "使用公式控制磁铁转速";
            // 
            // textBox12
            // 
            textBox12.Location = new Point(218, 196);
            textBox12.Name = "textBox12";
            textBox12.Size = new Size(311, 27);
            textBox12.TabIndex = 5;
            // 
            // textBox11
            // 
            textBox11.Location = new Point(218, 144);
            textBox11.Name = "textBox11";
            textBox11.Size = new Size(125, 27);
            textBox11.TabIndex = 4;
            // 
            // label33
            // 
            label33.AutoSize = true;
            label33.Location = new Point(22, 147);
            label33.Name = "label33";
            label33.Size = new Size(99, 20);
            label33.TabIndex = 3;
            label33.Text = "磁铁当前转速";
            // 
            // label32
            // 
            label32.AutoSize = true;
            label32.Location = new Point(22, 91);
            label32.Name = "label32";
            label32.Size = new Size(69, 20);
            label32.TabIndex = 2;
            label32.Text = "前后位置";
            // 
            // comboBox3
            // 
            comboBox3.FormattingEnabled = true;
            comboBox3.Location = new Point(125, 38);
            comboBox3.Name = "comboBox3";
            comboBox3.Size = new Size(151, 28);
            comboBox3.TabIndex = 1;
            // 
            // label31
            // 
            label31.AutoSize = true;
            label31.Location = new Point(18, 38);
            label31.Name = "label31";
            label31.Size = new Size(66, 20);
            label31.TabIndex = 0;
            label31.Text = "485端口";
            // 
            // button19
            // 
            button19.Location = new Point(22, 870);
            button19.Name = "button19";
            button19.Size = new Size(152, 62);
            button19.TabIndex = 28;
            button19.Text = "刷新全局com";
            button19.UseVisualStyleBackColor = true;
            // 
            // MainForm
            // 
            ClientSize = new Size(1694, 949);
            Controls.Add(button19);
            Controls.Add(groupBox9);
            Controls.Add(label30);
            Controls.Add(comboBox2);
            Controls.Add(groupBox8);
            Controls.Add(groupBox7);
            Controls.Add(groupBox6);
            Controls.Add(groupBox1);
            Controls.Add(groupBox5);
            Controls.Add(groupBox4);
            Controls.Add(groupBox3);
            Controls.Add(groupBox2);
            Name = "MainForm";
            Text = "打印机监控制";
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            groupBox4.ResumeLayout(false);
            groupBox4.PerformLayout();
            groupBox5.ResumeLayout(false);
            groupBox5.PerformLayout();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            groupBox6.ResumeLayout(false);
            groupBox6.PerformLayout();
            groupBox7.ResumeLayout(false);
            groupBox7.PerformLayout();
            groupBox8.ResumeLayout(false);
            groupBox8.PerformLayout();
            ((ISupportInitialize)BrightnessBar).EndInit();
            groupBox9.ResumeLayout(false);
            groupBox9.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        // IDisplayControl interface implementations
        public void UpdateDisplay1(int x1, int x2, int y1, int y2)
        {
            _displayController?.UpdateDisplay(x1, x2, y1, y2);
        }

        public void UpdateDisplay(int x1, int x2, int y1, int y2)
        {
            _displayController?.UpdateDisplay(x1, x2, y1, y2);
        }

        public void ShowBlackScreen()
        {
            _displayController?.ShowBlackScreen();
        }

        public void EnableAutoUpdate()
        {
            _displayController?.EnableAutoUpdate();
        }

        public void DisableAutoUpdate()
        {
            _displayController?.DisableAutoUpdate();
        }

        public void CloseDisplay()
        {
            _displayController?.CloseDisplay();
        }

        protected override void Dispose(bool disposing)
        {
            if (!_isDisposing)
            {
                lock (_disposeLock)
                {
                    if (!_isDisposing)
                    {
                        _isDisposing = true;

                        if (disposing)
                        {
                            try
                            {
                                // 1. 首先停止所有活动的操作
                                StopAllOperations();

                                // 2. 等待操作完全停止
                                Task.Delay(200).Wait();

                                // 3. 按顺序释放资源
                                DisposeManagedResources();

                                // 4. 清理UI资源
                                CleanupUIResources();
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error during form disposal: {ex.Message}");
                                _log?.Error($"Error during form disposal: {ex}");
                            }
                        }
                    }
                }
            }

            base.Dispose(disposing);
        }

        private void StopAllOperations()
        {
            // Stop operations in all controllers
        }

        private void DisposeManagedResources()
        {
            try
            {
                // Dispose all controllers
                _magneticFieldController?.Dispose();
                _displayController?.Dispose();
                _klipperController?.Dispose();
                _motorPositionController?.Dispose();
                _csvSearchController?.Dispose();
                _gcodeController?.Dispose();
                _ultravioletLightUiController?.Dispose();
                _ultravioletLightUiController = null;

                DisconnectUnoDevice();

                // Dispose legacy controllers
                if (_arduinoCom != null)
                {
                    _arduinoCom.Dispose();
                    _arduinoCom = null;
                }

                if (_motorController != null)
                {
                    _motorController.Dispose();
                    _motorController = null;
                }

                // Dispose other managed resources
                components?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disposing managed resources: {ex.Message}");
            }
        }

        private void CleanupUIResources()
        {
            // UI resources are now handled by individual controllers
        }

        // Any additional methods that need to be exposed
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    StopAllOperations();
                    Task.Delay(200).Wait();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OnFormClosing: {ex.Message}");
                _log?.Error($"Error in OnFormClosing: {ex}");
            }
            finally
            {
                base.OnFormClosing(e);
            }
        }

    }
}
