using System.Globalization;
using System.IO.Ports;

namespace Ad7606Host;

public sealed class MainForm : Form
{
    private static readonly byte[] UsbStartCmd = { 0xBB, 0x01, 0x00, 0x08 };
    private static readonly byte[] UsbStopCmd = { 0xBB, 0x02 };

    // --- 顶部工具条 ---
    private readonly ComboBox portBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly ComboBox usbBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Visible = false };
    private readonly Label portLabel = new() { Text = "串口", AutoSize = true, Padding = new Padding(4, 7, 0, 0) };
    private readonly Label usbLabel = new() { Text = "USB 序列号", AutoSize = true, Padding = new Padding(4, 7, 0, 0), Visible = false };
    private readonly ComboBox connectionBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85 };
    private readonly ComboBox baudBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly Button refreshButton = new() { Text = "刷新串口", AutoSize = true };
    private readonly Button usbRefreshButton = new() { Text = "刷新 USB", AutoSize = true, Visible = false };
    private readonly Button connectButton = new() { Text = "连接", AutoSize = true };
    private readonly Button allRunButton = new() { Text = "开始读取全部", AutoSize = true, Enabled = false };
    private readonly Button allOnceButton = new() { Text = "全部单次读取", AutoSize = true, Enabled = false };
    private readonly ComboBox channelBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85 };
    private readonly Button oneRunButton = new() { Text = "开始单通道", AutoSize = true, Enabled = false };
    private readonly Button oneOnceButton = new() { Text = "单通道单次读取", AutoSize = true, Enabled = false };
    private readonly ComboBox rangeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly NumericUpDown intervalBox = new() { Minimum = 50, Maximum = 5000, Increment = 50, Value = 100, Width = 72 };
    private readonly NumericUpDown sampleRateBox = new() { Minimum = 1000, Maximum = 2000000, Increment = 1000, Value = 200000, Width = 90 };
    private readonly Label statusLabel = new() { AutoSize = true, Text = "未连接", ForeColor = Color.DimGray, Padding = new Padding(5, 7, 0, 0) };
    private readonly Label rateLabel = new() { AutoSize = true, Text = "速率：—", ForeColor = Color.DimGray, Padding = new Padding(8, 7, 0, 0) };
    private readonly Label usbRateLabel = new() { AutoSize = true, Text = "USB：—", ForeColor = Color.DimGray, Padding = new Padding(8, 7, 0, 0) };
    private readonly Label lastReadLabel = new() { AutoSize = true, Text = "最近读取：—", ForeColor = Color.DimGray, Padding = new Padding(0, 3, 0, 8) };
    private readonly Button errorFramesButton = new() { Text = "查看错误帧", AutoSize = true };

    // --- 通道卡片 ---
    private readonly Label[] rawLabels = new Label[8];
    private readonly Label[] signedLabels = new Label[8];
    private readonly Label[] voltageLabels = new Label[8];

    // --- 波形页 ---
    private readonly CheckBox[] waveChecks = new CheckBox[8];
    private readonly WaveformPanel waveform = new();
    private readonly ComboBox windowBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 95 };
    private readonly ComboBox waveRangeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 95 };
    private readonly NumericUpDown waveCenterBox = new() { Minimum = -10, Maximum = 10, DecimalPlaces = 2, Increment = 0.1M, Value = 2.5M, Width = 72 };
    private readonly NumericUpDown expectCenterBox = new() { Minimum = -10, Maximum = 10, DecimalPlaces = 3, Increment = 0.1M, Value = 2.5M, Width = 72 };
    private readonly NumericUpDown expectCenterTolBox = new() { Minimum = 0, Maximum = 10, DecimalPlaces = 3, Increment = 0.01M, Value = 0.1M, Width = 62 };
    private readonly NumericUpDown expectVppBox = new() { Minimum = 0, Maximum = 20, DecimalPlaces = 3, Increment = 0.1M, Value = 0.5M, Width = 72 };
    private readonly NumericUpDown expectVppTolBox = new() { Minimum = 0, Maximum = 20, DecimalPlaces = 3, Increment = 0.01M, Value = 0.1M, Width = 62 };
    private readonly CheckBox enableAlarmCheck = new() { Text = "启用超限报警", AutoSize = true, Checked = false };
    private readonly Button resetWaveStatsButton = new() { Text = "重置统计", AutoSize = true };
    private readonly Button waveRunButton = new() { Text = "开始波形采集", AutoSize = true, Enabled = false };
    private readonly ComboBox scopeTriggerModeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 72 };
    private readonly ComboBox scopeTriggerSourceBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 72 };
    private readonly ComboBox scopeTriggerEdgeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 72 };
    private readonly NumericUpDown scopeTriggerLevelBox = new() { Minimum = -10, Maximum = 10, DecimalPlaces = 3, Increment = 0.01M, Value = 0, Width = 78 };
    private readonly NumericUpDown scopeTriggerPositionBox = new() { Minimum = 0, Maximum = 100, Value = 50, Width = 58 };
    private readonly Button scopeRunHoldButton = new() { Text = "Hold", AutoSize = true };
    private readonly Button scopeSingleButton = new() { Text = "Single", AutoSize = true };
    private readonly Label waveInfo = new() { AutoSize = true, Text = "选择通道后开始采集" };
    private TableLayoutPanel? waveStatsTable;
    private Label[,]? waveStatsLabels;

    // --- 频谱页 ---
    private ComboBox? spectrumChannelBox;
    private ComboBox? spectrumNBox;
    private Button? spectrumRefreshButton;
    private CheckBox? spectrumAutoCheck;
    private SpectrumPanel? spectrumPanel;
    private Label? spectrumInfo;
    private readonly System.Windows.Forms.Timer spectrumTimer = new() { Interval = 500, Enabled = false };

    // --- 李萨如页 ---
    private ComboBox? lissajousXBox;
    private ComboBox? lissajousYBox;
    private ComboBox? lissajousNBox;
    private Button? lissajousRefreshButton;
    private CheckBox? lissajousAutoCheck;
    private LissajousPanel? lissajousPanel;
    private Label? lissajousInfo;
    private readonly System.Windows.Forms.Timer lissajousTimer = new() { Interval = 300, Enabled = false };

    // --- 编码器 UI ---
    private readonly CheckBox encoderEnableCheck = new() { Text = "启用角度解算", AutoSize = true, Padding = new Padding(0, 6, 8, 0) };
    private readonly ComboBox[] encoderBoxes = new ComboBox[10];      // A+/A-/B+/B-/Z+/Z-/C+/C-/D+/D-
    private readonly NumericUpDown encoderLinesBox = new() { Minimum = 1, Maximum = 100000, Value = 1250, Width = 90 };
    private readonly CheckBox encoderInvertZCheck = new() { Text = "Z 极性反转", AutoSize = true, Padding = new Padding(0, 6, 8, 0) };
    private readonly CheckBox encoderAutoCenterCheck = new() { Text = "自动慢速去偏", AutoSize = true, Padding = new Padding(0, 6, 8, 0) };
    private readonly NumericUpDown offsetABox = new() { Minimum = -10, Maximum = 10, DecimalPlaces = 3, Increment = 0.1M, Value = 0, Width = 80 };
    private readonly NumericUpDown offsetBBox = new() { Minimum = -10, Maximum = 10, DecimalPlaces = 3, Increment = 0.1M, Value = 0, Width = 80 };
    private readonly NumericUpDown offsetZBox = new() { Minimum = -10, Maximum = 10, DecimalPlaces = 3, Increment = 0.1M, Value = 0, Width = 80 };
    private readonly NumericUpDown offsetCBox = new() { Minimum = -10, Maximum = 10, DecimalPlaces = 3, Increment = 0.1M, Value = 0, Width = 80 };
    private readonly NumericUpDown offsetDBox = new() { Minimum = -10, Maximum = 10, DecimalPlaces = 3, Increment = 0.1M, Value = 0, Width = 80 };
    private readonly Button encoderResetButton = new() { Text = "角度清零", AutoSize = true };
    private readonly ComboBox encoderModeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly Button encoderCaptureOffsetsButton = new() { Text = "取当前均值", AutoSize = true };
    private readonly Label encoderAngleLabel = new() { AutoSize = false, Text = "—", Font = new Font("Consolas", 30, FontStyle.Bold), ForeColor = Color.FromArgb(35, 55, 80), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly DialPanel abzDial = new();
    private readonly DialPanel cdDial = new();
    private readonly DialPanel correctedDial = new();
    private readonly Label encoderSpeedLabel = new() { AutoSize = false, Text = "—", Font = new Font("Consolas", 30, FontStyle.Bold), ForeColor = Color.FromArgb(35, 55, 80), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label encoderDirLabel = new() { AutoSize = false, Text = "未启用", Font = new Font("Microsoft YaHei UI", 30, FontStyle.Bold), ForeColor = Color.DimGray, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label encoderCdPosLabel = new() { AutoSize = false, Text = "—", Font = new Font("Consolas", 26, FontStyle.Bold), ForeColor = Color.FromArgb(35, 55, 80), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label encoderErrLabel = new() { AutoSize = false, Text = "—", Font = new Font("Consolas", 26, FontStyle.Bold), ForeColor = Color.FromArgb(35, 55, 80), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label encoderMaxErrLabel = new() { AutoSize = false, Text = "—", Font = new Font("Consolas", 26, FontStyle.Bold), ForeColor = Color.FromArgb(35, 55, 80), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label encoderMinErrLabel = new() { AutoSize = false, Text = "—", Font = new Font("Consolas", 26, FontStyle.Bold), ForeColor = Color.FromArgb(35, 55, 80), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label encoderMeanErrLabel = new() { AutoSize = false, Text = "—", Font = new Font("Consolas", 26, FontStyle.Bold), ForeColor = Color.FromArgb(35, 55, 80), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label encoderErrCountLabel = new() { AutoSize = false, Text = "—", Font = new Font("Consolas", 26, FontStyle.Bold), ForeColor = Color.FromArgb(35, 55, 80), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label encoderTurnsLabel = new() { AutoSize = true, Text = "累计 — · Z 脉冲 —", ForeColor = Color.DimGray, Padding = new Padding(0, 4, 20, 0) };
    private readonly Label encoderABZLabel = new() { AutoSize = true, Text = "A=— B=— Z=—", ForeColor = Color.DimGray, Padding = new Padding(0, 4, 0, 0) };
    private readonly Label encoderDiagLabel = new() { AutoSize = true, Text = "", ForeColor = Color.DimGray, Font = new Font("Microsoft YaHei UI", 8.5f), Padding = new Padding(0, 4, 0, 0) };
    private readonly Button encoderErrResetButton = new() { Text = "误差清零", AutoSize = true };

    // S120 动态漂移监控参数
    private readonly NumericUpDown encoderLimitDegBox = new() { Minimum = 0.1M, Maximum = 180M, DecimalPlaces = 2, Increment = 0.5M, Value = 15M, Width = 70 };
    private readonly NumericUpDown encoderPolePairsBox = new() { Minimum = 0, Maximum = 1000, Value = 0, Width = 60 };
    private readonly NumericUpDown encoderBaselineLearnBox = new() { Minimum = 0.1M, Maximum = 10M, DecimalPlaces = 2, Increment = 0.1M, Value = 0.5M, Width = 60 };
    private readonly Button encoderReloadBaselineButton = new() { Text = "重新学习基准", AutoSize = true };
    private readonly Button encoderResetAlarmButton = new() { Text = "报警清零", AutoSize = true };

    private readonly Label encoderBaselineLabel = new() { AutoSize = false, Text = "—", Font = new Font("Consolas", 26, FontStyle.Bold), ForeColor = Color.FromArgb(35, 55, 80), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label encoderDriftLabel = new() { AutoSize = false, Text = "—", Font = new Font("Consolas", 26, FontStyle.Bold), ForeColor = Color.FromArgb(35, 55, 80), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label encoderMaxDriftLabel = new() { AutoSize = false, Text = "—", Font = new Font("Consolas", 26, FontStyle.Bold), ForeColor = Color.FromArgb(35, 55, 80), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label encoderAlarmStatusLabel = new() { AutoSize = false, Text = "—", Font = new Font("Microsoft YaHei UI", 28, FontStyle.Bold), ForeColor = Color.DimGray, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };

    // --- 数据 ---
    private SerialPort? serial;
    private Ftd2xxUsb? usb;
    private bool IsUsb => connectionBox.SelectedIndex == 1;
    private bool busy;
    private ReadMode mode;
    private readonly object historyLock = new();
    private readonly List<Sample> history = new();
    private readonly object scopeLock = new();
    private readonly List<Sample> scopePreBuffer = new();
    private readonly List<Sample> scopeCaptureBuffer = new();
    private Sample[] scopePaintBuffer = Array.Empty<Sample>();
    private PointF[] wavePaintPoints = Array.Empty<PointF>();
    private bool scopeHold;
    private bool scopeArmed;
    private bool scopeCollectingPost;
    private int scopePostRemaining;
    private Sample? scopePreviousSample;
    private long scopeTriggerIndex = -1;
    private int scopeModeSetting;
    private int scopeSourceSetting;
    private bool scopeRisingSetting = true;
    private double scopeLevelSetting;
    private int scopePositionSetting = 50;
    private double scopeWindowSeconds = 0.01;
    private double scopeConfiguredSampleRate = 200000;
    private readonly object ioLock = new();
    private long frameIndexCounter;
    private readonly EncoderState encoder = new();
    private readonly SignalStats signalStats = new();
    private static readonly Color[] ChannelColors = { Color.DodgerBlue, Color.OrangeRed, Color.SeaGreen, Color.MediumPurple, Color.DarkOrange, Color.DeepPink, Color.Teal, Color.SaddleBrown };

    private CancellationTokenSource? pollCts;
    private Task? pollTask;
    private double currentRate;
    private double waveformSampleRate;
    private volatile FrameParser? activeParser;
    private int streamUiUpdatePending;
    private int streamWaveUpdatePending;

    private enum ReadMode { Stopped, All, Single, Waveform }

    private readonly struct Sample
    {
        public readonly long Index;
        private readonly ulong low;
        private readonly ulong high;
        private readonly double fullScale;
        public Sample(long index, ReadOnlySpan<double> volts, double scale)
        {
            Index = index; fullScale = scale;
            low = Pack(volts, 0, scale); high = Pack(volts, 4, scale);
        }
        private static ulong Pack(ReadOnlySpan<double> values, int start, double scale)
        {
            ulong packed = 0;
            for (int i = 0; i < 4; i++)
            {
                short value = (short)Math.Clamp((int)Math.Round(values[start + i] * 32768.0 / scale), short.MinValue, short.MaxValue);
                packed |= (ulong)(ushort)value << (i * 16);
            }
            return packed;
        }
        public double VoltageAt(int channel)
        {
            ulong packed = channel < 4 ? low : high;
            short raw = unchecked((short)(packed >> ((channel & 3) * 16)));
            return raw * fullScale / 32768.0;
        }
    }

    private sealed record PollOptions(int StartChannel, int Count, int IntervalMs, double FullScale, bool IsUsb, double HistorySpanSeconds, int SampleRate);

    public MainForm()
    {
        Text = "AD7606 电压采集上位机";
        MinimumSize = new Size(1100, 720); Size = new Size(1280, 860);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(245, 247, 250);

        baudBox.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200", "2000000" });
        baudBox.SelectedItem = "2000000";
        connectionBox.Items.AddRange(new object[] { "UART", "USB FIFO" });
        connectionBox.SelectedIndex = 0;
        connectionBox.SelectedIndexChanged += (_, _) =>
        {
            portBox.Visible = !IsUsb; refreshButton.Visible = !IsUsb;
            portLabel.Visible = !IsUsb; usbBox.Visible = IsUsb; usbLabel.Visible = IsUsb; usbRefreshButton.Visible = IsUsb;
            portBox.Enabled = !IsUsb; baudBox.Enabled = !IsUsb; refreshButton.Enabled = !IsUsb;
            intervalBox.Enabled = !IsUsb;
            AdjustIntervalRange();
            UpdateModeButtons();
        };

        rangeBox.Items.AddRange(new object[] { "±5 V", "±10 V" }); rangeBox.SelectedIndex = 0;

        waveRangeBox.Items.AddRange(new object[] { "0.25 Vpp", "0.5 Vpp", "1 Vpp", "2 Vpp", "5 Vpp", "10 Vpp", "20 Vpp" });
        waveRangeBox.SelectedIndex = 1;
        waveRangeBox.SelectedIndexChanged += (_, _) => waveform.Invalidate();
        waveCenterBox.ValueChanged += (_, _) => waveform.Invalidate();
        windowBox.Items.AddRange(new object[] {
            "50 µs", "100 µs", "500 µs", "1 ms", "5 ms", "10 ms", "20 ms", "50 ms", "100 ms", "500 ms", "1 s", "5 s"
        });
        windowBox.SelectedIndex = 5;
        scopeWindowSeconds = WindowSpanSeconds();
        windowBox.SelectedIndexChanged += (_, _) => { scopeWindowSeconds = WindowSpanSeconds(); ArmScope(); waveform.Invalidate(); };

        scopeTriggerModeBox.Items.AddRange(new object[] { "Auto", "Normal", "Single" });
        scopeTriggerModeBox.SelectedIndex = 0;
        scopeTriggerSourceBox.Items.AddRange(Enumerable.Range(1, 8).Select(i => $"CH{i}").Cast<object>().ToArray());
        scopeTriggerSourceBox.SelectedIndex = 0;
        scopeTriggerEdgeBox.Items.AddRange(new object[] { "上升沿", "下降沿" });
        scopeTriggerEdgeBox.SelectedIndex = 0;
        scopeTriggerModeBox.SelectedIndexChanged += (_, _) => { UpdateScopeSettings(); ArmScope(); waveform.Invalidate(); };
        scopeTriggerSourceBox.SelectedIndexChanged += (_, _) => { UpdateScopeSettings(); ArmScope(); waveform.Invalidate(); };
        scopeTriggerEdgeBox.SelectedIndexChanged += (_, _) => { UpdateScopeSettings(); ArmScope(); waveform.Invalidate(); };
        scopeTriggerLevelBox.ValueChanged += (_, _) => { UpdateScopeSettings(); ArmScope(); waveform.Invalidate(); };
        scopeTriggerPositionBox.ValueChanged += (_, _) => { UpdateScopeSettings(); ArmScope(); waveform.Invalidate(); };

        for (int i = 0; i < 8; i++)
        {
            channelBox.Items.Add($"通道 {i + 1}");
            waveChecks[i] = new CheckBox { Text = $"CH{i + 1}", AutoSize = true, Checked = i == 0, ForeColor = ChannelColors[i], Font = new Font(Font, FontStyle.Bold) };
            waveChecks[i].CheckedChanged += (_, _) => waveform.Invalidate();
        }
        channelBox.SelectedIndex = 0;

        for (int i = 0; i < 10; i++)
        {
            encoderBoxes[i] = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 88 };
            encoderBoxes[i].Items.Add("未使用");
            for (int ch = 0; ch < 8; ch++) encoderBoxes[i].Items.Add($"通道 {ch + 1}");
            encoderBoxes[i].SelectedIndex = 0;
        }
        // 默认：A+/A-/B+/B-/Z+/Z- 指向 CH1..CH6
        for (int i = 0; i < 6; i++) encoderBoxes[i].SelectedIndex = i + 1;

        BuildLayout();
        RefreshPorts(); RefreshUsbDevices();

        refreshButton.Click += (_, _) => RefreshPorts();
        usbRefreshButton.Click += (_, _) => RefreshUsbDevices();
        connectButton.Click += (_, _) => ToggleConnection();
        allRunButton.Click += (_, _) => ToggleMode(ReadMode.All);
        oneRunButton.Click += (_, _) => ToggleMode(ReadMode.Single);
        allOnceButton.Click += async (_, _) => await ReadChannelsAsync(false);
        oneOnceButton.Click += async (_, _) => await ReadChannelsAsync(false);
        waveRunButton.Click += (_, _) => { if (mode == ReadMode.Waveform) StopPolling(); else SetMode(ReadMode.Waveform); };
        scopeRunHoldButton.Click += (_, _) => { if (mode != ReadMode.Waveform) SetMode(ReadMode.Waveform); else ToggleScopeHold(); };
        scopeSingleButton.Click += (_, _) => { if (mode != ReadMode.Waveform) SetMode(ReadMode.Waveform); else ArmScope(true); };
        encoderResetButton.Click += (_, _) => encoder.ZeroAngle();
        errorFramesButton.Click += (_, _) => ShowErrorFrames();
        waveform.Paint += DrawWaveform;

        encoderLimitDegBox.ValueChanged += (_, _) => UpdateEncoderLimits();
        encoderPolePairsBox.ValueChanged += (_, _) => UpdateEncoderLimits();
        encoderBaselineLearnBox.ValueChanged += (_, _) => UpdateEncoderLimits();
        encoderReloadBaselineButton.Click += (_, _) => { encoder.ReloadBaseline(); UpdateEncoderUi(); };
        encoderResetAlarmButton.Click += (_, _) => { encoder.ResetAlarm(); UpdateEncoderUi(); };

        resetWaveStatsButton.Click += (_, _) =>
        {
            signalStats.ResetCumulative();
            UpdateWaveStats();
        };

        spectrumTimer.Tick += (_, _) => { if (spectrumAutoCheck?.Checked == true) RefreshSpectrum(); };
        lissajousTimer.Tick += (_, _) => { if (lissajousAutoCheck?.Checked == true) RefreshLissajous(); };

        FormClosing += (_, _) =>
        {
            StopPolling();
            try { pollTask?.Wait(2000); } catch { }
            ClosePort();
        };
        AdjustIntervalRange();
    }

    private void AdjustIntervalRange()
    {
        intervalBox.Minimum = IsUsb ? 1 : 50;
        intervalBox.Increment = IsUsb ? 1 : 50;
        if (intervalBox.Value < intervalBox.Minimum) intervalBox.Value = intervalBox.Minimum;
    }

    // ============================ 布局 ============================
    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 10), Margin = Padding.Empty };
        top.Controls.Add(new Label { Text = "AD7606 电压采集", Font = new Font(Font.FontFamily, 17, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 6, 18, 0) });
        AddLabeled(top, "连接方式", connectionBox);
        top.Controls.Add(portLabel); top.Controls.Add(portBox); top.Controls.Add(refreshButton);
        top.Controls.Add(usbLabel); top.Controls.Add(usbBox); top.Controls.Add(usbRefreshButton);
        AddLabeled(top, "波特率", baudBox); top.Controls.Add(connectButton);
        top.Controls.Add(new Label { Width = 8 }); top.Controls.Add(allRunButton); top.Controls.Add(allOnceButton);
        AddLabeled(top, "单通道", channelBox); top.Controls.Add(oneRunButton); top.Controls.Add(oneOnceButton);
        AddLabeled(top, "量程", rangeBox); AddLabeled(top, "采样间隔 ms", intervalBox); AddLabeled(top, "标称采样率 Hz", sampleRateBox);
        top.Controls.Add(statusLabel); top.Controls.Add(rateLabel); top.Controls.Add(usbRateLabel);
        top.Controls.Add(errorFramesButton);
        root.Controls.Add(top, 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildValuesTab());
        tabs.TabPages.Add(BuildWaveformTab());
        tabs.TabPages.Add(BuildEncoderTab());
        tabs.TabPages.Add(BuildSpectrumTab());
        tabs.TabPages.Add(BuildLissajousTab());
        root.Controls.Add(tabs, 0, 1);
    }

    private static void AddLabeled(FlowLayoutPanel panel, string title, Control control)
    {
        panel.Controls.Add(new Label { Text = title, AutoSize = true, Padding = new Padding(4, 7, 0, 0) });
        panel.Controls.Add(control);
    }

    private TabPage BuildValuesTab()
    {
        var tab = new TabPage("通道数据") { BackColor = BackColor };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(4) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(lastReadLabel, 0, 0);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
        for (int c = 0; c < 4; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        for (int r = 0; r < 2; r++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        for (int i = 0; i < 8; i++) grid.Controls.Add(MakeCard(i), i % 4, i / 4);
        layout.Controls.Add(grid, 0, 1);
        tab.Controls.Add(layout);
        return tab;
    }

    private TabPage BuildWaveformTab()
    {
        var tab = new TabPage("波形显示") { BackColor = BackColor };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // ---- 第 0 行：显示工具条（原有） ----
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        tools.Controls.Add(new Label { Text = "显示通道：", AutoSize = true, Padding = new Padding(0, 5, 4, 0) });
        foreach (var c in waveChecks) tools.Controls.Add(c);
        tools.Controls.Add(new Label { Text = "时间窗：", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        tools.Controls.Add(windowBox);
        tools.Controls.Add(new Label { Text = "电压范围(Vpp)：", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        tools.Controls.Add(waveRangeBox);
        tools.Controls.Add(new Label { Text = "垂直中心(V)：", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        tools.Controls.Add(waveCenterBox);
        tools.Controls.Add(new Label { Text = "触发：", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        tools.Controls.Add(scopeTriggerModeBox);
        tools.Controls.Add(new Label { Text = "源", AutoSize = true, Padding = new Padding(4, 5, 0, 0) });
        tools.Controls.Add(scopeTriggerSourceBox);
        tools.Controls.Add(scopeTriggerEdgeBox);
        tools.Controls.Add(new Label { Text = "电平", AutoSize = true, Padding = new Padding(4, 5, 0, 0) });
        tools.Controls.Add(scopeTriggerLevelBox);
        tools.Controls.Add(new Label { Text = "位置%", AutoSize = true, Padding = new Padding(4, 5, 0, 0) });
        tools.Controls.Add(scopeTriggerPositionBox);
        tools.Controls.Add(scopeRunHoldButton);
        tools.Controls.Add(scopeSingleButton);
        tools.Controls.Add(waveRunButton);
        var clearBtn = new Button { Text = "清空波形", AutoSize = true };
        clearBtn.Click += (_, _) => { lock (historyLock) history.Clear(); ArmScope(); waveform.Invalidate(); };
        tools.Controls.Add(clearBtn);
        layout.Controls.Add(tools, 0, 0);

        // ---- 第 1 行：新增的阈值设置工具条 ----
        var alarmTools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 4) };
        alarmTools.Controls.Add(new Label { Text = "期望中心电压：", AutoSize = true, Padding = new Padding(0, 5, 4, 0) });
        alarmTools.Controls.Add(expectCenterBox);
        alarmTools.Controls.Add(new Label { Text = "±", AutoSize = true, Padding = new Padding(2, 5, 2, 0) });
        alarmTools.Controls.Add(expectCenterTolBox);
        alarmTools.Controls.Add(new Label { Text = "V", AutoSize = true, Padding = new Padding(2, 5, 14, 0) });
        alarmTools.Controls.Add(new Label { Text = "期望 Vpp：", AutoSize = true, Padding = new Padding(0, 5, 4, 0) });
        alarmTools.Controls.Add(expectVppBox);
        alarmTools.Controls.Add(new Label { Text = "±", AutoSize = true, Padding = new Padding(2, 5, 2, 0) });
        alarmTools.Controls.Add(expectVppTolBox);
        alarmTools.Controls.Add(new Label { Text = "Vpp", AutoSize = true, Padding = new Padding(2, 5, 14, 0) });
        alarmTools.Controls.Add(enableAlarmCheck);
        alarmTools.Controls.Add(resetWaveStatsButton);
        layout.Controls.Add(alarmTools, 0, 1);

        // ---- 第 2 行：波形画布 ----
        waveform.Dock = DockStyle.Fill; waveform.BackColor = Color.White; waveform.Margin = new Padding(0, 8, 0, 8);
        layout.Controls.Add(waveform, 0, 2);

        // ---- 第 3 行：信息 ----
        waveInfo.Padding = new Padding(0, 4, 0, 0);
        layout.Controls.Add(waveInfo, 0, 3);

        // ---- 第 4 行：统计表 ----
        BuildWaveStatsPanel(layout, 4);
        tab.Controls.Add(layout);
        return tab;
    }
    private void BuildWaveStatsPanel(TableLayoutPanel parent, int row)
    {
        waveStatsTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 9,
            RowCount = 9,
            Padding = new Padding(0, 6, 0, 0),
        };

        int[] widths = { 52, 92, 82, 82, 82, 172, 92, 172, 92 };
        foreach (var w in widths)
            waveStatsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, w));

        string[] headers = { "通道", "DC", "Vpp", "AC RMS", "主频", "DC 范围", "DC 平均", "Vpp 范围", "Vpp 平均" };
        for (int c = 0; c < headers.Length; c++)
        {
            waveStatsTable.Controls.Add(new Label
            {
                Text = headers[c],
                AutoSize = false,
                Height = 20,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(Font, FontStyle.Bold),
                ForeColor = Color.DimGray,
            }, c, 0);
        }

        waveStatsLabels = new Label[8, 9];
        for (int ch = 0; ch < 8; ch++)
        {
            waveStatsLabels[ch, 0] = new Label
            {
                Text = $"CH{ch + 1}",
                AutoSize = false,
                Height = 18,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9, FontStyle.Bold),
                ForeColor = ChannelColors[ch]
            };
            for (int c = 1; c < 9; c++)
            {
                waveStatsLabels[ch, c] = new Label
                {
                    Text = "—",
                    AutoSize = false,
                    Height = 18,
                    Dock = DockStyle.Fill,
                    Font = new Font("Consolas", 9),
                    ForeColor = Color.DimGray
                };
            }
            for (int c = 0; c < 9; c++) waveStatsTable.Controls.Add(waveStatsLabels[ch, c], c, ch + 1);
        }
        parent.Controls.Add(waveStatsTable, 0, row);
    }
    private TabPage BuildEncoderTab()
    {
        var page = new TabPage("编码器解算") { BackColor = BackColor };
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            RowCount = 7,
            ColumnCount = 1,
        };

        // 显式定义 7 行的尺寸
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // 0: 通道选择工具条
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // 1: 偏置工具条
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // 2: S120 参数工具条
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));      // 3: 卡片区（占满剩余）
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));     // 4: 三个圆盘
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // 5: 累计/诊断行
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // 6: 说明文字

        // ---------- row 0: 通道选择 ----------
        var row1 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        row1.Controls.Add(encoderEnableCheck);
        row1.Controls.Add(new Label { Text = "模式：", AutoSize = true, Padding = new Padding(8, 7, 2, 0) });
        row1.Controls.Add(encoderModeBox);
        AddEncChannels(row1, "A+", "A-", 0, 1, Color.DodgerBlue);
        AddEncChannels(row1, "B+", "B-", 2, 3, Color.OrangeRed);
        AddEncChannels(row1, "Z+", "Z-", 4, 5, Color.SeaGreen);
        AddEncChannels(row1, "C+", "C-", 6, 7, Color.MediumPurple);
        AddEncChannels(row1, "D+", "D-", 8, 9, Color.DarkOrange);
        root.Controls.Add(row1, 0, 0);

        // 一次性初始化（如果还没在构造函数里初始化，就放在这里）
        if (encoderModeBox.Items.Count == 0)
        {
            encoderModeBox.Items.AddRange(new object[] { "单端", "差分" });
            encoderModeBox.SelectedIndex = 0;
            encoderModeBox.SelectedIndexChanged += (_, _) => UpdateEncoderModeUi();

            offsetABox.Value = 2.5m;
            offsetBBox.Value = 2.5m;
            offsetZBox.Value = 2.5m;
            offsetCBox.Value = 2.5m;
            offsetDBox.Value = 2.5m;

            offsetABox.ValueChanged += (_, _) => SyncEncoderOffsets();
            offsetBBox.ValueChanged += (_, _) => SyncEncoderOffsets();
            offsetZBox.ValueChanged += (_, _) => SyncEncoderOffsets();
            offsetCBox.ValueChanged += (_, _) => SyncEncoderOffsets();
            offsetDBox.ValueChanged += (_, _) => SyncEncoderOffsets();

            encoderCaptureOffsetsButton.Click += (_, _) => CaptureEncoderOffsets();
        }

        // ---------- row 1: 偏置 ----------
        var row2 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 6, 0, 6) };
        row2.Controls.Add(new Label { Text = "每转线数：", AutoSize = true, Padding = new Padding(0, 6, 2, 0) });
        row2.Controls.Add(encoderLinesBox);
        AddSpin(row2, "偏置A(V)：", offsetABox);
        AddSpin(row2, "B(V)：", offsetBBox);
        AddSpin(row2, "Z(V)：", offsetZBox);
        AddSpin(row2, "C(V)：", offsetCBox);
        AddSpin(row2, "D(V)：", offsetDBox);
        row2.Controls.Add(encoderInvertZCheck);
        row2.Controls.Add(encoderAutoCenterCheck);
        row2.Controls.Add(encoderResetButton);
        row2.Controls.Add(encoderCaptureOffsetsButton);
        root.Controls.Add(row2, 0, 1);

        // ---------- row 2: S120 参数 ----------
        var row3 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 6) };
        row3.Controls.Add(new Label { Text = "报警阈值(机械角°)：", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        row3.Controls.Add(encoderLimitDegBox);
        row3.Controls.Add(new Label { Text = "极对数：", AutoSize = true, Padding = new Padding(12, 6, 4, 0) });
        row3.Controls.Add(encoderPolePairsBox);
        row3.Controls.Add(new Label { Text = "基准学习时长(s)：", AutoSize = true, Padding = new Padding(12, 6, 4, 0) });
        row3.Controls.Add(encoderBaselineLearnBox);
        row3.Controls.Add(encoderReloadBaselineButton);
        row3.Controls.Add(encoderResetAlarmButton);
        root.Controls.Add(row3, 0, 2);

        // ---------- row 3: 4×3 卡片（占满剩余高度）----------
        var big = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4 };
        for (int c = 0; c < 3; c++) big.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        for (int r = 0; r < 4; r++) big.RowStyles.Add(new RowStyle(SizeType.Percent, 25));

        big.Controls.Add(MakeBigCard("ABZ 绝对角度", encoderAngleLabel, Color.DodgerBlue), 0, 0);
        big.Controls.Add(MakeBigCard("转速", encoderSpeedLabel, Color.OrangeRed), 1, 0);
        big.Controls.Add(MakeBigCard("旋转方向", encoderDirLabel, Color.SeaGreen), 2, 0);

        big.Controls.Add(MakeBigCard("CD 绝对位置", encoderCdPosLabel, Color.MediumPurple), 0, 1);
        big.Controls.Add(MakeBigCard("瞬时误差 (ABZ−CD)", encoderErrLabel, Color.DarkOrange), 1, 1);
        big.Controls.Add(MakeBigCard("基准偏差（学习值）", encoderBaselineLabel, Color.SteelBlue), 2, 1);

        big.Controls.Add(MakeBigCard("当前漂移", encoderDriftLabel, Color.Crimson), 0, 2);
        big.Controls.Add(MakeBigCard("最大漂移 / 阈值", encoderMaxDriftLabel, Color.DeepPink), 1, 2);
        big.Controls.Add(MakeBigCard("报警状态", encoderAlarmStatusLabel, Color.Firebrick), 2, 2);

        big.Controls.Add(MakeBigCard("误差最小值 (诊断)", encoderMinErrLabel, Color.SteelBlue), 0, 3);
        big.Controls.Add(MakeBigCard("误差最大值 (诊断)", encoderMaxErrLabel, Color.DeepPink), 1, 3);
        big.Controls.Add(MakeBigCard("误差平均 |·| (诊断)", encoderMeanErrLabel, Color.Crimson), 2, 3);

        root.Controls.Add(big, 0, 3);

        // ---------- row 4: 三个圆盘（固定 220 高）----------
        var dials = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
        for (int c = 0; c < 3; c++) dials.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        dials.Controls.Add(MakeDialCard("ABZ 当前角度", abzDial), 0, 0);
        dials.Controls.Add(MakeDialCard("CD 当前角度", cdDial), 1, 0);
        dials.Controls.Add(MakeDialCard("误差修正绝对角度", correctedDial), 2, 0);
        root.Controls.Add(dials, 0, 4);

        // ---------- row 5: 累计/诊断行 ----------
        var info = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        info.Controls.Add(encoderTurnsLabel);
        info.Controls.Add(encoderABZLabel);
        info.Controls.Add(encoderDiagLabel);
        root.Controls.Add(info, 0, 5);

        // ---------- row 6: 说明 ----------
        var note = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "说明：\r\n" +
                   "  · CD 为粗绝对位置轨道（每转一个周期）；ABZ 的累计位置通过 Z 对齐。\r\n" +
                   "  · 「ABZ − CD 误差」= ABZ 单圈位置 − CD 位置，归一化到 [-180°, +180°]。\r\n" +
                   "  · 上电后转一整圈让 Z 出现，「已同步」才会变 True，误差与最大误差开始工作。\r\n" +
                   "  · 手动测试前建议勾「Z 极性反转」验证一下；不正确时 Z 对齐会跳 180°。",
            ForeColor = Color.DimGray,
            Font = new Font(Font.FontFamily, 8.5f),
            Padding = new Padding(0, 10, 0, 0),
        };
        root.Controls.Add(note, 0, 6);

        page.Controls.Add(root);
        return page;
    }

    private void InitializeComponent()
    {

    }

    private static Control MakeDialCard(string title, DialPanel dial)
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(6), Padding = new Padding(6) };
        var label = new Label { Text = title, Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.DimGray };
        dial.Dock = DockStyle.Fill;
        card.Controls.Add(dial); card.Controls.Add(label);
        return card;
    }

    private sealed class DialPanel : Panel
    {
        private double angle;
        private bool valid;
        private string valueText = "—";
        public DialPanel() { DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.White; }
        public void SetAngle(double value, bool isValid, string text)
        {
            if (Math.Abs(angle - value) < 0.01 && valid == isValid && valueText == text) return;
            angle = value; valid = isValid; valueText = text; Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float size = Math.Max(20, Math.Min(ClientSize.Width - 28, ClientSize.Height - 52));
            float cx = ClientSize.Width / 2f, cy = 12 + size / 2f, r = size / 2f;
            using var pen = new Pen(Color.FromArgb(220, 226, 234), 2);
            g.DrawEllipse(pen, cx - r, cy - r, size, size);
            using var tick = new Pen(Color.Silver, 1);
            for (int i = 0; i < 12; i++) { double a = i * Math.PI / 6 - Math.PI / 2; g.DrawLine(tick, cx + (float)Math.Cos(a) * (r - 3), cy + (float)Math.Sin(a) * (r - 3), cx + (float)Math.Cos(a) * (r - 10), cy + (float)Math.Sin(a) * (r - 10)); }
            if (valid) { double a = angle * Math.PI / 180 - Math.PI / 2; using var needle = new Pen(Color.DodgerBlue, 3); g.DrawLine(needle, cx, cy, cx + (float)Math.Cos(a) * (r - 14), cy + (float)Math.Sin(a) * (r - 14)); }
            using var brush = new SolidBrush(valid ? Color.FromArgb(35, 55, 80) : Color.Gray);
            using var font = new Font("Consolas", 12, FontStyle.Bold);
            var measured = g.MeasureString(valueText, font); g.DrawString(valueText, font, brush, cx - measured.Width / 2, cy + r + 4);
        }
    }



    /// <summary>单端模式下禁用“-”负极下拉框（视觉上告诉用户不用管）</summary>
    private void UpdateEncoderModeUi()
    {
        bool single = encoderModeBox.SelectedIndex == 0;
        for (int i = 1; i < 10; i += 2)              // A-, B-, Z-, C-, D- 索引
            encoderBoxes[i].Enabled = !single;
    }

    /// <summary>UI offset 改动后同步给 EncoderState（热更新）</summary>
    private void SyncEncoderOffsets()
    {
        encoder.SetOffsets(
            (double)offsetABox.Value,
            (double)offsetBBox.Value,
            (double)offsetZBox.Value,
            (double)offsetCBox.Value,
            (double)offsetDBox.Value);
    }

    /// <summary>静置 0.5 秒后按一下，把 A/B/C/D 均值填入 offset 框。</summary>
    private void CaptureEncoderOffsets()
    {
        var (a, b, c, d) = encoder.GetAverageVoltage();
        if (a != 0 || b != 0)
        {
            offsetABox.Value = Math.Clamp((decimal)a, offsetABox.Minimum, offsetABox.Maximum);
            offsetBBox.Value = Math.Clamp((decimal)b, offsetBBox.Minimum, offsetBBox.Maximum);
        }
        if (c != 0 || d != 0)
        {
            offsetCBox.Value = Math.Clamp((decimal)c, offsetCBox.Minimum, offsetCBox.Maximum);
            offsetDBox.Value = Math.Clamp((decimal)d, offsetDBox.Minimum, offsetDBox.Maximum);
        }
        SyncEncoderOffsets();
        SetStatus($"已取均值：A={a:F3}V B={b:F3}V C={c:F3}V D={d:F3}V", false);
    }

    private void UpdateEncoderLimits()
    {
        encoder.UpdateLimits(
            (double)encoderLimitDegBox.Value,
            (int)encoderPolePairsBox.Value,
            (double)encoderBaselineLearnBox.Value);
    }
    private void AddEncChannels(FlowLayoutPanel row, string pLabel, string nLabel, int pi, int ni, Color c)
    {
        row.Controls.Add(new Label { Text = pLabel, AutoSize = true, Padding = new Padding(12, 7, 2, 0), ForeColor = c, Font = new Font(Font, FontStyle.Bold) });
        row.Controls.Add(encoderBoxes[pi]);
        row.Controls.Add(new Label { Text = nLabel, AutoSize = true, Padding = new Padding(6, 7, 2, 0), ForeColor = c });
        row.Controls.Add(encoderBoxes[ni]);
    }

    private static void AddSpin(FlowLayoutPanel row, string label, NumericUpDown box)
    {
        row.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(12, 6, 2, 0) });
        row.Controls.Add(box);
    }

    private TabPage BuildSpectrumTab()
    {
        var tab = new TabPage("频谱分析") { BackColor = BackColor };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        tools.Controls.Add(new Label { Text = "通道：", AutoSize = true, Padding = new Padding(0, 5, 4, 0) });
        spectrumChannelBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        for (int i = 0; i < 8; i++) spectrumChannelBox.Items.Add($"通道 {i + 1}");
        spectrumChannelBox.SelectedIndex = 0;
        tools.Controls.Add(spectrumChannelBox);

        tools.Controls.Add(new Label { Text = "点数：", AutoSize = true, Padding = new Padding(8, 5, 4, 0) });
        spectrumNBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        spectrumNBox.Items.AddRange(new object[] { "1024", "2048", "4096", "8192", "16384" });
        spectrumNBox.SelectedIndex = 2;
        tools.Controls.Add(spectrumNBox);

        spectrumRefreshButton = new Button { Text = "刷新频谱", AutoSize = true };
        spectrumRefreshButton.Click += (_, _) => RefreshSpectrum();
        tools.Controls.Add(spectrumRefreshButton);

        spectrumAutoCheck = new CheckBox { Text = "自动刷新", AutoSize = true, Padding = new Padding(8, 5, 0, 0) };
        spectrumAutoCheck.CheckedChanged += (_, _) => spectrumTimer.Enabled = spectrumAutoCheck.Checked;
        tools.Controls.Add(spectrumAutoCheck);

        layout.Controls.Add(tools, 0, 0);

        spectrumPanel = new SpectrumPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 6) };
        layout.Controls.Add(spectrumPanel, 0, 1);

        spectrumInfo = new Label { AutoSize = true, Padding = new Padding(0, 4, 0, 0), ForeColor = Color.DimGray };
        layout.Controls.Add(spectrumInfo, 0, 2);

        tab.Controls.Add(layout);
        return tab;
    }

    private TabPage BuildLissajousTab()
    {
        var tab = new TabPage("李萨如") { BackColor = BackColor };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        tools.Controls.Add(new Label { Text = "X 通道：", AutoSize = true, Padding = new Padding(0, 5, 4, 0) });
        lissajousXBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        for (int i = 0; i < 8; i++) lissajousXBox.Items.Add($"通道 {i + 1}");
        lissajousXBox.SelectedIndex = 0;
        tools.Controls.Add(lissajousXBox);

        tools.Controls.Add(new Label { Text = "Y 通道：", AutoSize = true, Padding = new Padding(8, 5, 4, 0) });
        lissajousYBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        for (int i = 0; i < 8; i++) lissajousYBox.Items.Add($"通道 {i + 1}");
        lissajousYBox.SelectedIndex = 1;
        tools.Controls.Add(lissajousYBox);

        tools.Controls.Add(new Label { Text = "点数：", AutoSize = true, Padding = new Padding(8, 5, 4, 0) });
        lissajousNBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        lissajousNBox.Items.AddRange(new object[] { "1000", "2000", "4000", "8000", "16384" });
        lissajousNBox.SelectedIndex = 2;
        tools.Controls.Add(lissajousNBox);

        lissajousRefreshButton = new Button { Text = "刷新", AutoSize = true };
        lissajousRefreshButton.Click += (_, _) => RefreshLissajous();
        tools.Controls.Add(lissajousRefreshButton);

        lissajousAutoCheck = new CheckBox { Text = "自动刷新", AutoSize = true, Padding = new Padding(8, 5, 0, 0) };
        lissajousAutoCheck.CheckedChanged += (_, _) => lissajousTimer.Enabled = lissajousAutoCheck.Checked;
        tools.Controls.Add(lissajousAutoCheck);

        layout.Controls.Add(tools, 0, 0);

        lissajousPanel = new LissajousPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 6) };
        layout.Controls.Add(lissajousPanel, 0, 1);

        lissajousInfo = new Label { AutoSize = true, Padding = new Padding(0, 4, 0, 0), ForeColor = Color.DimGray };
        layout.Controls.Add(lissajousInfo, 0, 2);

        tab.Controls.Add(layout);
        return tab;
    }

    private Panel MakeBigCard(string title, Label valueLabel, Color accent)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(5), BorderStyle = BorderStyle.FixedSingle };
        panel.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Location = new Point(14, 8),
            ForeColor = accent,
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
        });
        valueLabel.Location = new Point(0, 32);
        valueLabel.Size = new Size(panel.Width - 2, panel.Height - 34);
        valueLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        panel.Controls.Add(valueLabel);
        return panel;
    }

    private Panel MakeCard(int i)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(6), Padding = new Padding(14), BorderStyle = BorderStyle.FixedSingle };
        panel.Controls.Add(new Label { Text = $"CH{i + 1:00}", Font = new Font(Font.FontFamily, 11, FontStyle.Bold), ForeColor = ChannelColors[i], AutoSize = true, Location = new Point(14, 14) });
        rawLabels[i] = new Label { Text = "原始值  0x----", AutoSize = true, Location = new Point(14, 50), ForeColor = Color.DimGray };
        signedLabels[i] = new Label { Text = "有符号值  0", AutoSize = true, Location = new Point(14, 77), ForeColor = Color.DimGray };
        voltageLabels[i] = new Label { Text = "0.0000 V", AutoSize = true, Location = new Point(14, 111), Font = new Font(Font.FontFamily, 17, FontStyle.Bold), ForeColor = Color.FromArgb(35, 55, 80) };
        panel.Controls.Add(rawLabels[i]); panel.Controls.Add(signedLabels[i]); panel.Controls.Add(voltageLabels[i]);
        return panel;
    }

    // ============================ 连接 ============================
    private void RefreshPorts()
    {
        var old = portBox.SelectedItem as string; portBox.Items.Clear();
        try { portBox.Items.AddRange(SerialPort.GetPortNames().OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray()); }
        catch (Exception ex) { SetStatus($"枚举串口失败：{ex.Message}", true); }
        if (portBox.Items.Count > 0)
            portBox.SelectedItem = old is not null && portBox.Items.Contains(old) ? old : portBox.Items[0];
    }

    private void RefreshUsbDevices()
    {
        var old = usbBox.SelectedItem as string;
        usbBox.Items.Clear();
        try { usbBox.Items.AddRange(Ftd2xxUsb.GetSerialNumbers().Cast<object>().ToArray()); }
        catch (Exception ex) { SetStatus($"枚举 USB 失败：{ex.Message}", true); }
        if (usbBox.Items.Count > 0)
            usbBox.SelectedItem = old is not null && usbBox.Items.Contains(old) ? old : usbBox.Items[0];
    }

    private void ToggleConnection()
    {
        if (serial?.IsOpen == true || usb is not null)
        {
            StopPolling(); ClosePort(); SetConnected(false); SetStatus("已断开", false);
            return;
        }

        if (IsUsb)
        {
            if (usbBox.SelectedItem is not string serialNumber) { SetStatus("请先选择 USB 设备", true); return; }
            try
            {
                usb = new Ftd2xxUsb();
                usb.OpenBySerial(serialNumber);
                SetConnected(true);
                SetStatus($"已连接 USB FIFO · 序列号 {serialNumber}", false);
            }
            catch (Exception ex)
            {
                ClosePort(); SetConnected(false);
                SetStatus($"USB 连接失败：{ex.Message}", true);
            }
            return;
        }

        if (portBox.SelectedItem is not string port) { SetStatus("请先选择串口", true); return; }
        try
        {
            serial = new SerialPort(port, int.Parse((string)baudBox.SelectedItem!, CultureInfo.InvariantCulture), Parity.None, 8, StopBits.One)
            { ReadTimeout = 1000, WriteTimeout = 1000, DtrEnable = false, RtsEnable = false, Handshake = Handshake.None };
            serial.Open(); serial.DiscardInBuffer(); serial.DiscardOutBuffer();
            SetConnected(true); SetStatus($"已连接 {port}", false);
        }
        catch (Exception ex) { ClosePort(); SetConnected(false); SetStatus($"连接失败：{ex.Message}", true); }
    }

    private void ToggleMode(ReadMode requested) { if (mode == requested) StopPolling(); else SetMode(requested); }

    private void SetMode(ReadMode requested)
    {
        if (serial?.IsOpen != true && usb is null) { SetStatus("请先连接 UART 或 USB", true); return; }
        StopPolling();
        mode = requested;
        if (requested == ReadMode.Waveform) ArmScope();

        var options = new PollOptions(
            StartChannel: requested == ReadMode.Single ? channelBox.SelectedIndex : 0,
            Count: requested == ReadMode.Single ? 1 : 8,
            IntervalMs: (int)intervalBox.Value,
            FullScale: rangeBox.SelectedIndex == 1 ? 10.0 : 5.0,
            IsUsb: IsUsb,
            HistorySpanSeconds: WindowSpanSeconds(),
            SampleRate: (int)sampleRateBox.Value);

        var cfg = new EncoderConfig
        {
            Enabled = encoderEnableCheck.Checked,
            SingleEnded = encoderModeBox.SelectedIndex == 0,
            ChAp = GetEncChannel(0),
            ChAn = GetEncChannel(1),
            ChBp = GetEncChannel(2),
            ChBn = GetEncChannel(3),
            ChZp = GetEncChannel(4),
            ChZn = GetEncChannel(5),
            ChCp = GetEncChannel(6),
            ChCn = GetEncChannel(7),
            ChDp = GetEncChannel(8),
            ChDn = GetEncChannel(9),
            Lines = (double)encoderLinesBox.Value,
            InvertZ = encoderInvertZCheck.Checked,
            AutoCenter = encoderAutoCenterCheck.Checked,
            OffsetA = (double)offsetABox.Value,
            OffsetB = (double)offsetBBox.Value,
            OffsetZ = (double)offsetZBox.Value,
            OffsetC = (double)offsetCBox.Value,
            OffsetD = (double)offsetDBox.Value,
            // 新增
            ErrorLimitDeg = (double)encoderLimitDegBox.Value,
            BaselineLearnSeconds = (double)encoderBaselineLearnBox.Value,
            PolePairs = (int)encoderPolePairsBox.Value,
        };
        double initialRate = options.IsUsb ? options.SampleRate : 1000.0 / Math.Max(1, options.IntervalMs);
        waveformSampleRate = initialRate;
        scopeConfiguredSampleRate = initialRate;
        encoder.Configure(cfg, initialRate);
        signalStats.SetSampleRate(initialRate);

        pollCts = new CancellationTokenSource();
        var token = pollCts.Token;

        if (IsUsb) pollTask = Task.Run(() => StreamLoopAsync(options, token));
        else pollTask = Task.Run(() => PollLoopAsync(options, token));

        UpdateModeButtons();
        SetStatus(requested switch
        {
            ReadMode.All => IsUsb ? "USB 流模式连续采集中" : "连续读取全部通道中",
            ReadMode.Single => $"连续读取通道 {channelBox.SelectedIndex + 1} 中",
            _ => IsUsb ? "USB 流模式波形采集中" : "波形采集中"
        }, false);
    }

    private int GetEncChannel(int idx)
    {
        int sel = encoderBoxes[idx].SelectedIndex;
        return sel <= 0 ? -1 : sel - 1;
    }

    private void StopPolling()
    {
        var cts = pollCts;
        pollCts = null;
        mode = ReadMode.Stopped;
        try { cts?.Cancel(); } catch { }
        UpdateModeButtons();
    }

    private void UpdateModeButtons()
    {
        allRunButton.Text = mode == ReadMode.All ? "停止读取全部" : "开始读取全部";
        oneRunButton.Text = mode == ReadMode.Single ? "停止单通道" : "开始单通道";
        waveRunButton.Text = mode == ReadMode.Waveform ? "停止波形采集" : "开始波形采集";
        bool connected = serial?.IsOpen == true || usb is not null;
        bool usbMode = IsUsb;
        allRunButton.Enabled = connected && (mode == ReadMode.Stopped || mode == ReadMode.All);
        allOnceButton.Enabled = connected && mode == ReadMode.Stopped && !busy;
        oneRunButton.Enabled = connected && !usbMode && (mode == ReadMode.Stopped || mode == ReadMode.Single);
        oneOnceButton.Enabled = connected && !usbMode && mode == ReadMode.Stopped && !busy;
        waveRunButton.Enabled = connected && (mode == ReadMode.Stopped || mode == ReadMode.Waveform);
        channelBox.Enabled = mode != ReadMode.Single;
        errorFramesButton.Enabled = activeParser is not null;
    }

    private void UpdateScopeSettings()
    {
        scopeModeSetting = Math.Max(0, scopeTriggerModeBox.SelectedIndex);
        scopeSourceSetting = Math.Max(0, scopeTriggerSourceBox.SelectedIndex);
        scopeRisingSetting = scopeTriggerEdgeBox.SelectedIndex != 1;
        scopeLevelSetting = (double)scopeTriggerLevelBox.Value;
        scopePositionSetting = (int)scopeTriggerPositionBox.Value;
    }

    private int ScopeWindowPoints()
    {
        double rate = waveformSampleRate > 0 ? waveformSampleRate : scopeConfiguredSampleRate;
        return Math.Max(2, (int)Math.Min(2000000, Math.Ceiling(scopeWindowSeconds * Math.Max(1, rate))));
    }

    private void ArmScope(bool forceSingle = false)
    {
        if (forceSingle)
        {
            scopeTriggerModeBox.SelectedIndex = 2;
            UpdateScopeSettings();
        }
        lock (scopeLock)
        {
            scopeHold = false;
            scopeArmed = true;
            scopeCollectingPost = false;
            scopePostRemaining = 0;
            scopePreviousSample = null;
            scopeTriggerIndex = -1;
            scopePreBuffer.Clear();
            scopeCaptureBuffer.Clear();
        }
        scopeRunHoldButton.Text = "Hold";
        waveform.Invalidate();
    }

    private void ToggleScopeHold()
    {
        lock (scopeLock) scopeHold = !scopeHold;
        scopeRunHoldButton.Text = scopeHold ? "Run" : "Hold";
        if (!scopeHold) ArmScope();
        waveform.Invalidate();
    }

    private void ProcessScopeSample(Sample sample)
    {
        lock (scopeLock)
        {
            if (scopeHold) return;

            int totalPoints = ScopeWindowPoints();
            int prePoints = Math.Min(totalPoints - 1, (int)Math.Round(totalPoints * scopePositionSetting / 100.0));
            int postPoints = totalPoints - prePoints;
            TrimScopeBuffer(scopePreBuffer, prePoints);

            if (scopeModeSetting == 0)
            {
                scopeCaptureBuffer.Add(sample);
                TrimScopeBuffer(scopeCaptureBuffer, totalPoints);
                scopePreviousSample = sample;
                return;
            }

            if (!scopeArmed)
            {
                scopePreviousSample = sample;
                return;
            }

            if (scopeCollectingPost)
            {
                scopeCaptureBuffer.Add(sample);
                scopePostRemaining--;
                if (scopePostRemaining <= 0)
                {
                    scopeCollectingPost = false;
                    scopeArmed = scopeModeSetting == 1;
                    if (scopeArmed) scopePreBuffer.Clear();
                }
                scopePreviousSample = sample;
                return;
            }

            bool crossed = false;
            if (scopePreviousSample.HasValue)
            {
                double previous = scopePreviousSample.Value.VoltageAt(scopeSourceSetting);
                double current = sample.VoltageAt(scopeSourceSetting);
                crossed = scopeRisingSetting
                    ? previous < scopeLevelSetting && current >= scopeLevelSetting
                    : previous > scopeLevelSetting && current <= scopeLevelSetting;
            }

            scopePreBuffer.Add(sample);
            TrimScopeBuffer(scopePreBuffer, prePoints + 1);

            if (crossed)
            {
                scopeCaptureBuffer.Clear();
                int copyCount = Math.Min(prePoints, scopePreBuffer.Count - 1);
                for (int i = scopePreBuffer.Count - 1 - copyCount; i < scopePreBuffer.Count - 1; i++)
                    scopeCaptureBuffer.Add(scopePreBuffer[i]);
                scopeCaptureBuffer.Add(sample);
                scopeTriggerIndex = sample.Index;
                scopePostRemaining = Math.Max(0, postPoints - 1);
                scopeCollectingPost = scopePostRemaining > 0;
                if (!scopeCollectingPost) scopeArmed = false;
            }

            scopePreviousSample = sample;
        }
    }

    // List.RemoveAt(0) shifts every item. Trim a small batch instead, which keeps long captures responsive.
    private static void TrimScopeBuffer(List<Sample> buffer, int maxCount)
    {
        const int slack = 1024;
        if (buffer.Count <= maxCount + slack) return;
        buffer.RemoveRange(0, Math.Min(slack, buffer.Count - maxCount));
    }

    private double WindowSpanSeconds()
    {
        string s = (string)(windowBox.SelectedItem ?? "10 ms");
        var parts = s.Split(' ');
        double v = double.Parse(parts[0], CultureInfo.InvariantCulture);
        return parts[1] switch { "µs" => v / 1e6, "ms" => v / 1e3, _ => v };
    }

    // ============================ 帧解析器 ============================
    internal sealed class FrameParser
    {
        public const int PayloadSize = 16;
        public const int FrameSize = PayloadSize + 3;
        private const byte Header0 = 0xAB, Header1 = 0xCD;
        private const int MaxRecentErrors = 10;
        private static readonly byte[] CrcTable = BuildCrcTable();

        private byte[] buf = new byte[64 * 1024];
        private int len;
        public long CrcErrors;
        public long DroppedBytes;
        public long StreamOffset;

        public readonly struct ErrorFrame
        {
            public readonly DateTime Time;
            public readonly byte[] Raw;
            public readonly byte ExpectedCrc;
            public readonly byte ActualCrc;
            public readonly long Offset;
            public ErrorFrame(DateTime t, byte[] raw, byte exp, byte act, long off)
            { Time = t; Raw = raw; ExpectedCrc = exp; ActualCrc = act; Offset = off; }
        }

        private readonly Queue<ErrorFrame> recentErrors = new();
        public ErrorFrame[] GetRecentErrors() { lock (recentErrors) return recentErrors.ToArray(); }
        public void ClearRecentErrors() { lock (recentErrors) recentErrors.Clear(); }

        public List<byte[]> Feed(byte[] data)
        {
            var frames = new List<byte[]>();
            Feed(data, data.Length, (source, offset) =>
            {
                var payload = new byte[PayloadSize];
                Buffer.BlockCopy(source, offset, payload, 0, PayloadSize);
                frames.Add(payload);
            });
            return frames;
        }

        // Delivers valid payloads directly from the parser buffer. Streaming callers can process them
        // immediately without allocating a List or one byte[] per frame.
        public int Feed(byte[] data, int count, Action<byte[], int> onPayload)
        {
            if (count < 0 || count > data.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (len + count > buf.Length)
            {
                int newSize = Math.Max(buf.Length * 2, len + count);
                Array.Resize(ref buf, newSize);
            }
            Buffer.BlockCopy(data, 0, buf, len, count);
            len += count;

            int frameCount = 0;
            int i = 0;
            while (i + FrameSize <= len)
            {
                if (buf[i] != Header0 || buf[i + 1] != Header1) { i++; DroppedBytes++; continue; }
                byte crc = Crc8(buf, i + 2, PayloadSize);
                if (crc != buf[i + FrameSize - 1])
                {
                    var raw = new byte[FrameSize];
                    Buffer.BlockCopy(buf, i, raw, 0, FrameSize);
                    lock (recentErrors)
                    {
                        recentErrors.Enqueue(new ErrorFrame(DateTime.Now, raw, crc, buf[i + FrameSize - 1], StreamOffset + i));
                        while (recentErrors.Count > MaxRecentErrors) recentErrors.Dequeue();
                    }
                    i++; CrcErrors++; continue;
                }
                onPayload(buf, i + 2);
                frameCount++;
                i += FrameSize;
            }
            if (i > 0)
            {
                Buffer.BlockCopy(buf, i, buf, 0, len - i);
                len -= i;
                StreamOffset += i;
            }
            return frameCount;
        }

        public static byte Crc8(byte[] data, int offset, int count)
        {
            byte crc = 0x00;
            for (int k = 0; k < count; k++)
                crc = CrcTable[crc ^ data[offset + k]];
            return crc;
        }

        private static byte[] BuildCrcTable()
        {
            var table = new byte[256];
            for (int i = 0; i < table.Length; i++)
            {
                int crc = i;
                for (int b = 0; b < 8; b++)
                    crc = (crc & 0x80) != 0 ? ((crc << 1) ^ 0x07) : (crc << 1);
                table[i] = (byte)crc;
            }
            return table;
        }
    }

    // ============================ USB 流模式 ============================
    private sealed class StreamState
    {
        public DateTime LastUiUpdate = DateTime.MinValue;
        public DateTime LastWaveUpdate = DateTime.MinValue;
        public DateTime RateWindowStart = DateTime.Now;
        public long BytesWindow;
        public int FramesWindow;
        public long TotalBytes;
        public long TotalFrames;
    }

    private async Task StreamLoopAsync(PollOptions options, CancellationToken token)
    {
        var dev = usb ?? throw new InvalidOperationException("USB 设备未连接");
        var parser = new FrameParser();
        activeParser = parser;
        var state = new StreamState();
        // A modest block bounds how long one parser pass can occupy the CPU on slower PCs.
        const int MaxBlockBytes = 16 * 1024;
        var readBuffer = new byte[MaxBlockBytes];
        var lastVolts = new double[8];
        double fullScale = options.FullScale;

        lock (ioLock) { dev.Purge(); dev.Write(UsbStartCmd); }

        try
        {
            while (!token.IsCancellationRequested)
            {
                int blockLength;
                lock (ioLock) blockLength = dev.ReadAvailable(readBuffer, MaxBlockBytes);
                if (blockLength == 0) continue;

                state.BytesWindow += blockLength;
                state.TotalBytes += blockLength;

                long lastIndex = 0;
                int frameCount;

                lock (historyLock)
                {
                    frameCount = parser.Feed(readBuffer, blockLength, (payload, offset) =>
                    {
                        Span<double> volts = stackalloc double[8];
                        for (int ch = 0; ch < 8; ch++)
                        {
                            short v = unchecked((short)((payload[offset + ch * 2] << 8) | payload[offset + ch * 2 + 1]));
                            volts[ch] = v * fullScale / 32768.0;
                        }
                        long idx = Interlocked.Increment(ref frameIndexCounter);
                        var sample = new Sample(idx, volts, fullScale);
                        history.Add(sample);
                        ProcessScopeSample(sample);
                        encoder.ProcessFrame(volts);
                        signalStats.Push(volts);
                        volts.CopyTo(lastVolts); lastIndex = idx;
                    });
                    if (frameCount > 0)
                    {
                        int actualRate = Math.Max(1, (int)Math.Round(waveformSampleRate > 0 ? waveformSampleRate : options.SampleRate));
                        TrimHistory(lastIndex, options.HistorySpanSeconds, actualRate);
                    }
                }
                if (frameCount == 0) continue;

                state.FramesWindow += frameCount;
                state.TotalFrames += frameCount;

                var now = DateTime.Now;
                if ((now - state.LastUiUpdate).TotalMilliseconds >= 50)
                {
                    state.LastUiUpdate = now;
                    double rateHz = 0, mbPerSec = 0;
                    var elapsed = (now - state.RateWindowStart).TotalSeconds;
                    if (elapsed > 0.4)
                    {
                        rateHz = state.FramesWindow / elapsed;
                        mbPerSec = state.BytesWindow / elapsed / (1024.0 * 1024.0);
                        state.RateWindowStart = now;
                        state.BytesWindow = 0;
                        state.FramesWindow = 0;
                        if (rateHz > 0)
                        {
                            waveformSampleRate = rateHz;
                            currentRate = rateHz;
                            encoder.UpdateFrameInterval(1.0 / rateHz);
                            signalStats.SetSampleRate(rateHz);
                        }
                    }
                    long crcErrors = parser.CrcErrors;
                    long droppedBytes = parser.DroppedBytes;
                    long totalFrames = state.TotalFrames;
                    long totalBytes = state.TotalBytes;
                    var lastSnapshot = lastVolts;

                    if (Interlocked.Exchange(ref streamUiUpdatePending, 1) == 0) SafeInvoke(() =>
                    {
                        try
                        {
                            ApplyChannelsToUi(0, 8, lastSnapshot, fullScale);
                            lastReadLabel.Text = $"流模式 · 累计 {totalFrames:N0} 帧 / {totalBytes / 1024.0 / 1024.0:F2} MB";
                            if (rateHz > 0)
                            {
                                double expectedFrameRate = mbPerSec * 1024.0 * 1024.0 / FrameParser.FrameSize;
                                rateLabel.Text = $"实际帧率：{rateHz:F1} Hz";
                                usbRateLabel.Text = $"USB：{mbPerSec:F2} MiB/s · 有效帧率：{rateHz:F0} Hz · 理论字节帧率：{expectedFrameRate:F0} Hz · CRC错 {crcErrors} · 丢弃 {droppedBytes}";
                            }
                            if (mode == ReadMode.Waveform)
                            {
                                int cached;
                                lock (historyLock) cached = history.Count;
                                waveInfo.Text = $"流采集中 · 缓存 {cached:N0} 点 · 实际 {rateHz:F0} Hz · {mbPerSec:F2} MB/s";
                            }
                            UpdateWaveStats();
                            UpdateEncoderUi();
                            errorFramesButton.Enabled = true;
                        }
                        finally { Volatile.Write(ref streamUiUpdatePending, 0); }
                    });
                }

                if ((now - state.LastWaveUpdate).TotalMilliseconds >= 40)
                {
                    state.LastWaveUpdate = now;
                    if (Interlocked.Exchange(ref streamWaveUpdatePending, 1) == 0) SafeInvoke(() =>
                    {
                        try { waveform.Invalidate(); }
                        finally { Volatile.Write(ref streamWaveUpdatePending, 0); }
                    });
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SafeInvoke(() => { SetStatus($"USB 流读取失败：{ex.Message}", true); StopPolling(); });
        }
        finally
        {
            if (ReferenceEquals(activeParser, parser)) activeParser = null;
            try { lock (ioLock) dev.Write(UsbStopCmd); } catch { }
            try { lock (ioLock) dev.Purge(); } catch { }
        }
    }

    private void ApplyChannelsToUi(int startChannel, int count, double[] volts, double fullScale)
    {
        for (int i = 0; i < count; i++)
        {
            int ch = startChannel + i;
            double v = volts[ch];
            short signedVal = (short)Math.Clamp((int)Math.Round(v / fullScale * 32768.0), -32768, 32767);
            ushort rawVal = unchecked((ushort)signedVal);
            voltageLabels[ch].Text = $"{v:+0.0000;-0.0000;0.0000} V";
            signedLabels[ch].Text = $"有符号值  {signedVal}";
            rawLabels[ch].Text = $"原始值  0x{rawVal:X4}";
        }
    }

    private void UpdateWaveStats()
    {
        if (waveStatsLabels is null) return;

        bool alarm = enableAlarmCheck.Checked;
        double expectCenter = (double)expectCenterBox.Value;
        double centerTol = (double)expectCenterTolBox.Value;
        double expectVpp = (double)expectVppBox.Value;
        double vppTol = (double)expectVppTolBox.Value;

        for (int ch = 0; ch < 8; ch++)
        {
            var s = signalStats.GetStats(ch);
            var c = signalStats.GetCumulativeStats(ch);

            if (!s.Valid)
            {
                for (int k = 1; k < 9; k++) waveStatsLabels[ch, k].Text = "—";
                for (int k = 1; k < 9; k++) waveStatsLabels[ch, k].ForeColor = Color.DimGray;
                continue;
            }

            // 实时值
            waveStatsLabels[ch, 1].Text = $"{s.Dc:+0.0000;-0.0000;0.0000} V";
            waveStatsLabels[ch, 2].Text = $"{s.Vpp:F4} V";
            waveStatsLabels[ch, 3].Text = $"{s.AcRms:F4} V";
            waveStatsLabels[ch, 4].Text = s.Frequency > 0.5 ? $"{s.Frequency:F1} Hz" : "—";

            // 超限报警着色
            bool dcBad = alarm && Math.Abs(s.Dc - expectCenter) > centerTol;
            bool vppBad = alarm && Math.Abs(s.Vpp - expectVpp) > vppTol;
            waveStatsLabels[ch, 1].ForeColor = dcBad ? Color.Firebrick : Color.DimGray;
            waveStatsLabels[ch, 2].ForeColor = vppBad ? Color.Firebrick : Color.DimGray;
            waveStatsLabels[ch, 3].ForeColor = Color.DimGray;
            waveStatsLabels[ch, 4].ForeColor = Color.DimGray;

            // 累计值
            if (c.Valid)
            {
                waveStatsLabels[ch, 5].Text = $"{c.MinDc:+0.0000;-0.0000;0.0000} ~ {c.MaxDc:+0.0000;-0.0000;0.0000}";
                waveStatsLabels[ch, 6].Text = $"{c.AvgDc:+0.0000;-0.0000;0.0000}";
                waveStatsLabels[ch, 7].Text = $"{c.MinVpp:F4} ~ {c.MaxVpp:F4}";
                waveStatsLabels[ch, 8].Text = $"{c.AvgVpp:F4}";
            }
            else
            {
                for (int k = 5; k < 9; k++) waveStatsLabels[ch, k].Text = "—";
            }
            for (int k = 5; k < 9; k++) waveStatsLabels[ch, k].ForeColor = Color.DimGray;
        }
    }
    private void UpdateEncoderUi()
    {
        var snap = encoder.GetSnapshot();
        if (!snap.Valid)
        {
            abzDial.SetAngle(0, false, "等待采集");
            cdDial.SetAngle(0, false, "未配置");
            correctedDial.SetAngle(0, false, "等待采集");
            encoderAngleLabel.Text = "—";
            encoderSpeedLabel.Text = "—";
            encoderDirLabel.Text = "未启用";
            encoderDirLabel.ForeColor = Color.DimGray;
            encoderCdPosLabel.Text = "—";
            encoderErrLabel.Text = "—";
            encoderMaxErrLabel.Text = "—";
            encoderMinErrLabel.Text = "—";
            encoderMeanErrLabel.Text = "—";
            encoderBaselineLabel.Text = "—";
            encoderDriftLabel.Text = "—";
            encoderMaxDriftLabel.Text = "—";
            encoderAlarmStatusLabel.Text = "—";
            encoderTurnsLabel.Text = "累计 — · Z 脉冲 —";
            encoderABZLabel.Text = "A=— B=— Z=—";
            encoderDiagLabel.Text = "";
            return;
        }

        if (!snap.Synced) encoderAngleLabel.Text = "—(等待Z)";
        else encoderAngleLabel.Text = $"{snap.AngleDeg:F2}°";
        abzDial.SetAngle(snap.AbzAngleDeg, snap.Synced, snap.Synced ? $"{snap.AbzAngleDeg:F2}°" : "等待 Z");
        cdDial.SetAngle(snap.CdAngleDeg, snap.CdValid, snap.CdValid ? $"{snap.CdAngleDeg:F2}°" : "未配置");
        double corrected = (snap.AbzAngleDeg - snap.InstantErrorDeg + 360) % 360;
        correctedDial.SetAngle(corrected, snap.CdValid && snap.Synced, snap.CdValid && snap.Synced ? $"{corrected:F2}°" : "等待同步");
        encoderSpeedLabel.Text = $"{snap.SpeedRpm:F2} rpm";

        if (snap.Direction > 0) { encoderDirLabel.Text = "CW ↻"; encoderDirLabel.ForeColor = Color.SeaGreen; }
        else if (snap.Direction < 0) { encoderDirLabel.Text = "CCW ↺"; encoderDirLabel.ForeColor = Color.OrangeRed; }
        else { encoderDirLabel.Text = "静止"; encoderDirLabel.ForeColor = Color.DimGray; }

        bool hasErr = snap.CdValid && snap.AngleErrorSamples > 0;

        if (!snap.CdValid)
        {
            encoderCdPosLabel.Text = "未配置";
            encoderErrLabel.Text = "未配置";
            encoderBaselineLabel.Text = "未配置";
            encoderDriftLabel.Text = "未配置";
            encoderMaxDriftLabel.Text = "未配置";
            encoderAlarmStatusLabel.Text = "未配置";
            encoderMinErrLabel.Text = "未配置";
            encoderMaxErrLabel.Text = "未配置";
            encoderMeanErrLabel.Text = "未配置";

            Color gray = Color.DimGray;
            encoderCdPosLabel.ForeColor = gray;
            encoderErrLabel.ForeColor = gray;
            encoderBaselineLabel.ForeColor = gray;
            encoderDriftLabel.ForeColor = gray;
            encoderMaxDriftLabel.ForeColor = gray;
            encoderAlarmStatusLabel.ForeColor = gray;
            encoderMinErrLabel.ForeColor = gray;
            encoderMaxErrLabel.ForeColor = gray;
            encoderMeanErrLabel.ForeColor = gray;
        }
        else
        {
            // ===== CD 绝对位置 =====
            encoderCdPosLabel.Text = $"{snap.CdAngleDeg:F2}°";
            encoderCdPosLabel.ForeColor = Color.FromArgb(35, 55, 80);

            // ===== 瞬时误差（诊断：只要 C/D 有信号就显示）=====
            if (snap.AngleErrorSamples > 0)
            {
                encoderErrLabel.Text = $"{snap.InstantErrorDeg:+0.00;-0.00;0.00}°";
                encoderErrLabel.ForeColor = snap.Synced ? Color.FromArgb(35, 55, 80) : Color.DarkOrange;
            }
            else
            {
                encoderErrLabel.Text = "—";
                encoderErrLabel.ForeColor = Color.DimGray;
            }

            // ===== 基准偏差（学习值）=====
            if (snap.BaselineReady)
            {
                encoderBaselineLabel.Text = $"{snap.BaselineErrorDeg:+0.00;-0.00;0.00}°";
                encoderBaselineLabel.ForeColor = Color.FromArgb(35, 55, 80);
            }
            else if (snap.Synced)
            {
                double pct = snap.BaselineLearnTarget > 0
                    ? Math.Min(100, snap.BaselineLearnProgress * 100.0 / snap.BaselineLearnTarget)
                    : 0;
                encoderBaselineLabel.Text = $"学习中 {pct:F0}%";
                encoderBaselineLabel.ForeColor = Color.DarkOrange;
            }
            else
            {
                encoderBaselineLabel.Text = "—(等待Z)";
                encoderBaselineLabel.ForeColor = Color.DimGray;
            }

            // ===== 当前漂移 =====
            if (snap.BaselineReady)
            {
                encoderDriftLabel.Text = $"{snap.DriftDeg:+0.00;-0.00;0.00}°";
                encoderDriftLabel.ForeColor = snap.AlarmActive ? Color.Firebrick : Color.FromArgb(35, 55, 80);
            }
            else
            {
                encoderDriftLabel.Text = "—(等待基准)";
                encoderDriftLabel.ForeColor = Color.DimGray;
            }

            // ===== 最大漂移 =====
            if (snap.BaselineReady)
            {
                string txt = $"{snap.MaxDriftDeg:F2}° / ±{snap.LimitDeg:F1}°";
                if (snap.PolePairs > 0)
                    txt += $"\n(电 {snap.ElectricDriftDeg:F1}° / ±{snap.ElectricLimitDeg:F0}°)";
                encoderMaxDriftLabel.Text = txt;
                encoderMaxDriftLabel.ForeColor = snap.MaxDriftDeg > snap.LimitDeg
                    ? Color.Firebrick : Color.FromArgb(35, 55, 80);
            }
            else
            {
                encoderMaxDriftLabel.Text = "—";
                encoderMaxDriftLabel.ForeColor = Color.DimGray;
            }

            // ===== 报警状态 =====
            if (!snap.Synced)
            {
                encoderAlarmStatusLabel.Text = "等待Z";
                encoderAlarmStatusLabel.ForeColor = Color.DimGray;
            }
            else if (!snap.BaselineReady)
            {
                encoderAlarmStatusLabel.Text = "学习基准中";
                encoderAlarmStatusLabel.ForeColor = Color.DarkOrange;
            }
            else if (snap.AlarmActive)
            {
                encoderAlarmStatusLabel.Text = "⚠ 报警";
                encoderAlarmStatusLabel.ForeColor = Color.Firebrick;
            }
            else
            {
                encoderAlarmStatusLabel.Text = "正常";
                encoderAlarmStatusLabel.ForeColor = Color.SeaGreen;
            }

            // ===== 关键：诊断统计 min/max/mean 三张卡的赋值 =====
            if (hasErr)
            {
                encoderMinErrLabel.Text = $"{snap.MinAngleErrorDeg:+0.00;-0.00;0.00}°";
                encoderMaxErrLabel.Text = $"{snap.MaxAngleErrorDeg:+0.00;-0.00;0.00}°";
                encoderMeanErrLabel.Text = $"{snap.MeanAbsAngleErrorDeg:F2}°";
                encoderMinErrLabel.ForeColor = Color.FromArgb(35, 55, 80);
                encoderMaxErrLabel.ForeColor = Color.FromArgb(35, 55, 80);
                encoderMeanErrLabel.ForeColor = Color.FromArgb(35, 55, 80);
            }
            else
            {
                encoderMinErrLabel.Text = "—";
                encoderMaxErrLabel.Text = "—";
                encoderMeanErrLabel.Text = "—";
                encoderMinErrLabel.ForeColor = Color.DimGray;
                encoderMaxErrLabel.ForeColor = Color.DimGray;
                encoderMeanErrLabel.ForeColor = Color.DimGray;
            }
        }

        // 诊断行
        encoderDiagLabel.Text = $"原始 A={snap.RawA:+0.000;-0.000;0.000}V B={snap.RawB:+0.000;-0.000;0.000}V Z={snap.RawZ:+0.000;-0.000;0.000}V · 峰值 A={snap.PeakA:F3}V B={snap.PeakB:F3}V · dt={snap.DtPerFrameMs:F4}ms · {(snap.Synced ? "已同步" : "未同步")} · 模式：{(snap.SingleEnded ? "单端" : "差分")}";
        if (snap.CdValid && snap.AngleErrorSamples > 0)
        {
            encoderDiagLabel.Text += $" · N={snap.AngleErrorSamples:N0} (min/max/mean = {snap.MinAngleErrorDeg:+0.00;-0.00;0.00}° / {snap.MaxAngleErrorDeg:+0.00;-0.00;0.00}° / {snap.MeanAbsAngleErrorDeg:F2}°)";
        }
    }

    // ============================ UART 轮询 ============================
    private async Task PollLoopAsync(PollOptions options, CancellationToken token)
    {
        var state = new PollState();
        while (!token.IsCancellationRequested)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { await ReadOnceAsync(options, state, token); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                SafeInvoke(() => { SetStatus($"读取失败：{ex.Message}", true); StopPolling(); });
                break;
            }
            UpdateRate(state);
            int wait = options.IntervalMs - (int)sw.ElapsedMilliseconds;
            if (wait > 0)
            {
                try { await Task.Delay(wait, token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private sealed class PollState
    {
        public DateTime LastUiUpdate = DateTime.MinValue;
        public DateTime LastWaveUpdate = DateTime.MinValue;
        public DateTime RateWindowStart = DateTime.Now;
        public int RateWindowCount;
    }

    private async Task ReadOnceAsync(PollOptions options, PollState state, CancellationToken token)
    {
        byte[] cmd = new byte[] { 0xAA, 0x01, (byte)options.StartChannel, (byte)options.Count };

        byte[] response = await Task.Run(() =>
        {
            var port = serial ?? throw new InvalidOperationException("串口未连接");
            lock (ioLock)
            {
                port.DiscardInBuffer();
                port.Write(cmd, 0, cmd.Length);
                return ReadExact(port, options.Count * 2, TimeSpan.FromSeconds(1.5));
            }
        }, token);

        var rawArr = new ushort[8]; var signedArr = new short[8]; var voltsArr = new double[8];
        for (int i = 0; i < options.Count; i++)
        {
            ushort raw = (ushort)((response[i * 2] << 8) | response[i * 2 + 1]);
            short value = unchecked((short)raw);
            int ch = options.StartChannel + i;
            rawArr[ch] = raw; signedArr[ch] = value; voltsArr[ch] = value * options.FullScale / 32768.0;
        }

        if (options.Count == 8)
        {
            long idx = Interlocked.Increment(ref frameIndexCounter);
            lock (historyLock)
            {
                var sample = new Sample(idx, voltsArr, options.FullScale);
                history.Add(sample);
                ProcessScopeSample(sample);
                encoder.ProcessFrame(voltsArr);
                signalStats.Push(voltsArr);
                TrimHistory(idx, options.HistorySpanSeconds, options.SampleRate);
            }
        }
        state.RateWindowCount++;

        var nowUi = DateTime.Now;
        if ((nowUi - state.LastUiUpdate).TotalMilliseconds >= 50)
        {
            state.LastUiUpdate = nowUi;
            SafeInvoke(() =>
            {
                for (int i = 0; i < options.Count; i++)
                {
                    int ch = options.StartChannel + i;
                    rawLabels[ch].Text = $"原始值  0x{rawArr[ch]:X4}";
                    signedLabels[ch].Text = $"有符号值  {signedArr[ch]}";
                    voltageLabels[ch].Text = $"{voltsArr[ch]:+0.0000;-0.0000;0.0000} V";
                }
                lastReadLabel.Text = $"最近读取：{nowUi:yyyy-MM-dd HH:mm:ss.fff} · {(options.Count == 8 ? "全部 8 通道" : $"通道 {options.StartChannel + 1}")}";
                UpdateWaveStats();
                UpdateEncoderUi();
            });
        }
        if (options.Count == 8 && (nowUi - state.LastWaveUpdate).TotalMilliseconds >= 40)
        {
            state.LastWaveUpdate = nowUi;
            SafeInvoke(() => waveform.Invalidate());
        }
    }

    private void UpdateRate(PollState state)
    {
        var elapsed = (DateTime.Now - state.RateWindowStart).TotalSeconds;
        if (elapsed < 0.5) return;
        double rate = state.RateWindowCount / elapsed;
        state.RateWindowStart = DateTime.Now;
        state.RateWindowCount = 0;
        SafeInvoke(() => { currentRate = rate; rateLabel.Text = $"实际帧率：{rate:F1} Hz"; });
    }

    private void SafeInvoke(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { if (InvokeRequired) BeginInvoke(action); else action(); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    // ============================ 单次读取 ============================
    private async Task ReadChannelsAsync(bool background)
    {
        if (busy || (serial?.IsOpen != true && usb is null)) return;
        busy = true; UpdateModeButtons();
        try
        {
            double fullScale = rangeBox.SelectedIndex == 1 ? 10.0 : 5.0;
            var voltsByChannel = new double[8];
            bool fullFrame = true;

            if (IsUsb)
            {
                var dev = usb ?? throw new InvalidOperationException("USB 设备未连接");
                var parser = new FrameParser();
                activeParser = parser;
                byte[]? payload = null;
                await Task.Run(() =>
                {
                    lock (ioLock)
                    {
                        dev.Purge();
                        dev.Write(UsbStartCmd);
                        try
                        {
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            while (sw.Elapsed < TimeSpan.FromSeconds(2))
                            {
                                var block = dev.ReadAvailable(16384);
                                if (block.Length == 0) continue;
                                var frames = parser.Feed(block);
                                if (frames.Count > 0) { payload = frames[0]; break; }
                            }
                        }
                        finally
                        {
                            try { dev.Write(UsbStopCmd); } catch { }
                            dev.Purge();
                        }
                    }
                });
                if (payload is null) throw new TimeoutException("USB 单次读取未收到有效帧");
                for (int ch = 0; ch < 8; ch++)
                {
                    short v = unchecked((short)((payload[ch * 2] << 8) | payload[ch * 2 + 1]));
                    voltsByChannel[ch] = v * fullScale / 32768.0;
                }
                ApplyChannelsToUi(0, 8, voltsByChannel, fullScale);
                signalStats.Push(voltsByChannel);
                encoder.ProcessFrame(voltsByChannel);
            }
            else
            {
                int startChannel = channelBox.SelectedIndex;
                int count = ModifierKeys.HasFlag(Keys.Control) ? 1 : 8;
                fullFrame = count == 8;
                byte[] cmd = new byte[] { 0xAA, 0x01, (byte)startChannel, (byte)count };
                byte[] response = await Task.Run(() =>
                {
                    var port = serial ?? throw new InvalidOperationException("串口未连接");
                    lock (ioLock)
                    {
                        port.DiscardInBuffer();
                        port.Write(cmd, 0, cmd.Length);
                        return ReadExact(port, count * 2, TimeSpan.FromSeconds(1.5));
                    }
                });
                for (int i = 0; i < count; i++)
                {
                    ushort raw = (ushort)((response[i * 2] << 8) | response[i * 2 + 1]);
                    short value = unchecked((short)raw);
                    int ch = startChannel + i;
                    voltsByChannel[ch] = value * fullScale / 32768.0;
                    rawLabels[ch].Text = $"原始值  0x{raw:X4}";
                    signedLabels[ch].Text = $"有符号值  {value}";
                    voltageLabels[ch].Text = $"{voltsByChannel[ch]:+0.0000;-0.0000;0.0000} V";
                }
                if (count == 8)
                {
                    signalStats.Push(voltsByChannel);
                    encoder.ProcessFrame(voltsByChannel);
                }
            }

            long idx = Interlocked.Increment(ref frameIndexCounter);
            int sampleRate = Math.Max(1, (int)Math.Round(IsUsb && waveformSampleRate > 0 ? waveformSampleRate : (double)sampleRateBox.Value));
            lock (historyLock)
            {
                var sample = new Sample(idx, voltsByChannel, fullScale);
                history.Add(sample);
                if (fullFrame) ProcessScopeSample(sample);
                TrimHistory(idx, WindowSpanSeconds(), sampleRate);
            }
            waveform.Invalidate();
            UpdateWaveStats();
            UpdateEncoderUi();
            lastReadLabel.Text = $"最近读取：{DateTime.Now:HH:mm:ss.fff} · 单次";
            if (!background) SetStatus("单次读取成功", false);
        }
        catch (TimeoutException ex) { SetStatus($"响应超时：{ex.Message}", true); if (mode != ReadMode.Stopped) StopPolling(); }
        catch (Exception ex) { SetStatus($"读取失败：{ex.Message}", true); if (mode != ReadMode.Stopped) StopPolling(); }
        finally
        {
            activeParser = null;
            busy = false; UpdateModeButtons();
        }
    }

    private void TrimHistory(long maxIndex, double spanSeconds, int sampleRate)
    {
        long keepPoints = (long)(spanSeconds * sampleRate * 2) + 10000;
        if (keepPoints < 100000) keepPoints = 100000;
        if (keepPoints > 2000000) keepPoints = 2000000;
        long minIndex = maxIndex - keepPoints;
        int lo = 0, hi = history.Count;
        while (lo < hi) { int mid = (lo + hi) / 2; if (history[mid].Index < minIndex) lo = mid + 1; else hi = mid; }
        if (lo > 0) history.RemoveRange(0, lo);
    }

    private static byte[] ReadExact(SerialPort port, int length, TimeSpan timeout)
    {
        byte[] data = new byte[length]; int offset = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
        while (offset < length)
        {
            if (sw.Elapsed >= timeout) throw new TimeoutException($"已收到 {offset}/{length} 字节");
            int available = port.BytesToRead;
            if (available == 0) { Thread.Sleep(1); continue; }
            offset += port.Read(data, offset, Math.Min(available, length - offset));
        }
        return data;
    }

    private static string FormatSpan(double seconds)
    {
        if (seconds >= 1) return $"{seconds:0.##}s";
        if (seconds >= 1e-3) return $"{seconds * 1e3:0.#}ms";
        return $"{seconds * 1e6:0.#}µs";
    }

    // ============================ 波形绘制 ============================
    private void DrawWaveform(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        Rectangle r = waveform.ClientRectangle; if (r.Width < 100 || r.Height < 100) return;
        const int left = 75, right = 18, top = 20, bottom = 42;
        var plot = new Rectangle(left, top, r.Width - left - right, r.Height - top - bottom);
        using var gridPen = new Pen(Color.FromArgb(225, 230, 236));
        using var axisPen = new Pen(Color.Gray);
        using var font = new Font("Microsoft YaHei UI", 8.5f);
        using var labelBrush = new SolidBrush(Color.DimGray);

        string rangeText = (string)(waveRangeBox.SelectedItem ?? "0.5 Vpp");
        double halfRange = double.Parse(rangeText.Split(" Vpp")[0], CultureInfo.InvariantCulture) / 2.0;
        double centerVoltage = (double)waveCenterBox.Value;
        double spanSeconds = WindowSpanSeconds();
        double sampleRate = IsUsb && waveformSampleRate > 0 ? waveformSampleRate : (double)sampleRateBox.Value;
        long windowPoints = Math.Max(1, (long)Math.Ceiling(spanSeconds * sampleRate));

        for (int i = 0; i <= 4; i++)
        {
            float y = plot.Top + plot.Height * i / 4f; g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            double v = centerVoltage + halfRange - 2 * halfRange * i / 4.0;
            g.DrawString($"{v:0.###} V", font, labelBrush, 2, y - 8);
        }
        for (int i = 0; i <= 4; i++)
        {
            float x = plot.Left + plot.Width * i / 4f; g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            double t = spanSeconds * (4 - i) / 4.0;
            g.DrawString(t <= 0 ? "0" : $"-{FormatSpan(t)}", font, labelBrush, x - 16, plot.Bottom + 7);
        }
        g.DrawRectangle(axisPen, plot);

        Sample[] points;
        int pointCount;
        int sourcePointCount;
        long triggerIndex;
        lock (scopeLock)
        {
            sourcePointCount = scopeCaptureBuffer.Count;
            pointCount = Math.Min(sourcePointCount, 8192);
            if (scopePaintBuffer.Length < pointCount)
                scopePaintBuffer = new Sample[Math.Max(pointCount, scopePaintBuffer.Length * 2 + 1024)];
            if (pointCount == sourcePointCount)
                scopeCaptureBuffer.CopyTo(scopePaintBuffer, 0);
            else
                for (int i = 0; i < pointCount; i++)
                    scopePaintBuffer[i] = scopeCaptureBuffer[(int)((long)i * (sourcePointCount - 1) / (pointCount - 1))];
            points = scopePaintBuffer;
            triggerIndex = scopeTriggerIndex;
        }

        long startIndex = pointCount == 0 ? 0 : points[0].Index;
        int width = plot.Width;

        for (int ch = 0; ch < 8; ch++)
        {
            if (!waveChecks[ch].Checked) continue;
            if (pointCount == 0) continue;

            if (pointCount <= width)
            {
                using var pen = new Pen(ChannelColors[ch], 1.6f);
                if (wavePaintPoints.Length < pointCount)
                    wavePaintPoints = new PointF[Math.Max(pointCount, wavePaintPoints.Length * 2 + 256)];
                var pts = wavePaintPoints;
                int drawn = 0;
                for (int p = 0; p < pointCount; p++)
                {
                    var s = points[p];
                    if (s.Index < startIndex) continue;
                    float x = plot.Left + (float)((s.Index - startIndex) / (double)windowPoints * width);
                    float y = plot.Top + (float)((centerVoltage + halfRange - s.VoltageAt(ch)) / (2 * halfRange) * plot.Height);
                    if (y < plot.Top) y = plot.Top;
                    if (y > plot.Bottom) y = plot.Bottom;
                    pts[drawn++] = new PointF(x, y);
                }
                if (drawn >= 2)
                    for (int p = 1; p < drawn; p++) g.DrawLine(pen, pts[p - 1], pts[p]);
                else if (drawn == 1)
                {
                    using var brush = new SolidBrush(ChannelColors[ch]);
                    g.FillEllipse(brush, pts[0].X - 2.5f, pts[0].Y - 2.5f, 5, 5);
                }
            }
            else
            {
                using var pen = new Pen(ChannelColors[ch], 1f);
                int prevX = int.MinValue;
                float colMin = 0, colMax = 0;
                bool hasPrev = false;
                for (int p = 0; p < pointCount; p++)
                {
                    var s = points[p];
                    if (s.Index < startIndex) continue;
                    int x = plot.Left + (int)((s.Index - startIndex) / (double)windowPoints * width);
                    if (x < plot.Left) x = plot.Left;
                    if (x > plot.Right) x = plot.Right;
                    float y = plot.Top + (float)((centerVoltage + halfRange - s.VoltageAt(ch)) / (2 * halfRange) * plot.Height);
                    if (y < plot.Top) y = plot.Top;
                    if (y > plot.Bottom) y = plot.Bottom;
                    if (!hasPrev) { prevX = x; colMin = colMax = y; hasPrev = true; continue; }
                    if (x == prevX) { if (y < colMin) colMin = y; if (y > colMax) colMax = y; }
                    else { g.DrawLine(pen, prevX, colMin, prevX, colMax); prevX = x; colMin = colMax = y; }
                }
                if (hasPrev) g.DrawLine(pen, prevX, colMin, prevX, colMax);
            }
        }

        string hint = $"时间窗 {FormatSpan(spanSeconds)} · 采样率 {sampleRate:F0} Hz · 理论 {windowPoints:N0} 点 · 实际 {sourcePointCount:N0} 点";
        g.DrawString(hint, font, labelBrush, plot.Left, plot.Top + 2);

        if (pointCount > 1 && triggerIndex >= 0)
        {
            int triggerOffset = 0;
            while (triggerOffset < pointCount && points[triggerOffset].Index < triggerIndex) triggerOffset++;
            float tx = plot.Left + triggerOffset / (float)Math.Max(1, pointCount - 1) * width;
            using var triggerPen = new Pen(Color.Firebrick, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            g.DrawLine(triggerPen, tx, plot.Top, tx, plot.Bottom);
            g.DrawString("触发", font, Brushes.Firebrick, tx + 3, plot.Top + 18);
        }

        if (pointCount == 0)
            g.DrawString("开始波形采集后将在此显示所选通道", new Font(Font, FontStyle.Bold), Brushes.Gray, plot.Left + 25, plot.Top + plot.Height / 2);
        else if (pointCount < 10)
            g.DrawString("窗口内点数不足 10 个，请把时间窗调大；若采样率确实不够，请先解决 FPGA 帧率", new Font(Font, FontStyle.Bold), Brushes.Firebrick, plot.Left + 25, plot.Top + plot.Height / 2);
    }

    // ============================ 频谱 / 李萨如 ============================
    private void RefreshSpectrum()
    {
        if (spectrumPanel is null || spectrumChannelBox is null || spectrumNBox is null || spectrumInfo is null) return;
        int ch = spectrumChannelBox.SelectedIndex;
        if (ch < 0) ch = 0;
        int n = int.Parse((string)spectrumNBox.SelectedItem!);
        if (n > signalStats.WindowPoints) n = signalStats.WindowPoints;

        var samples = new double[n];
        int count = signalStats.CopyRecent(ch, samples);
        if (count < 32)
        {
            spectrumInfo.Text = "采集数据不足，请先开始读取";
            return;
        }
        double sr = IsUsb && waveformSampleRate > 0 ? waveformSampleRate : (double)sampleRateBox.Value;
        spectrumPanel.Channel = ch;
        spectrumPanel.ComputeAndSet(samples, count, sr);
        spectrumInfo.Text = spectrumPanel.Info;
    }

    private void RefreshLissajous()
    {
        if (lissajousPanel is null || lissajousXBox is null || lissajousYBox is null || lissajousNBox is null || lissajousInfo is null) return;
        int x = lissajousXBox.SelectedIndex;
        int y = lissajousYBox.SelectedIndex;
        if (x < 0) x = 0;
        if (y < 0) y = 1;
        int n = int.Parse((string)lissajousNBox.SelectedItem!);
        if (n > signalStats.WindowPoints) n = signalStats.WindowPoints;

        var xs = new double[n];
        var ys = new double[n];
        int cx = signalStats.CopyRecent(x, xs);
        int cy = signalStats.CopyRecent(y, ys);
        int count = Math.Min(cx, cy);
        if (count < 32)
        {
            lissajousInfo.Text = "采集数据不足，请先开始读取";
            return;
        }
        lissajousPanel.ChannelX = x;
        lissajousPanel.ChannelY = y;
        lissajousPanel.SetData(xs, ys, count);
        lissajousInfo.Text = lissajousPanel.Info;
    }

    // ============================ 错误帧窗口 ============================
    private void ShowErrorFrames()
    {
        var p = activeParser;
        if (p is null)
        {
            SetStatus("当前没有活跃的 USB 流，请先连接 USB 并开始采集", true);
            return;
        }
        using var f = new ErrorFramesForm(p);
        f.ShowDialog(this);
    }

    private void SetConnected(bool connected)
    {
        connectionBox.Enabled = portBox.Enabled = usbBox.Enabled = baudBox.Enabled = refreshButton.Enabled = usbRefreshButton.Enabled = !connected;
        connectButton.Text = connected ? "断开" : "连接";
        if (!connected) StopPolling();
        UpdateModeButtons();
    }

    private void SetStatus(string text, bool error)
    {
        statusLabel.Text = text;
        statusLabel.ForeColor = error ? Color.Firebrick : Color.SeaGreen;
    }

    private void ClosePort()
    {
        lock (ioLock)
        {
            try { if (serial?.IsOpen == true) serial.Close(); } catch { }
            serial?.Dispose(); serial = null;
            usb?.Dispose(); usb = null;
        }
    }
}

internal sealed class WaveformPanel : Panel
{
    public WaveformPanel() { DoubleBuffered = true; ResizeRedraw = true; }
}

internal sealed class ErrorFramesForm : Form
{
    private readonly MainForm.FrameParser parser;
    private readonly ListView lv = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        Font = new Font("Consolas", 9F),
    };
    private readonly Label summary = new()
    {
        Dock = DockStyle.Top,
        Height = 60,
        Padding = new Padding(8, 6, 8, 0),
        TextAlign = ContentAlignment.MiddleLeft,
    };

    public ErrorFramesForm(MainForm.FrameParser parser)
    {
        this.parser = parser;
        Text = "最近错误帧（最多 10 条）";
        Size = new Size(1080, 440);
        MinimumSize = new Size(820, 320);
        StartPosition = FormStartPosition.CenterParent;

        lv.Columns.Add("#", 60, HorizontalAlignment.Right);
        lv.Columns.Add("时间", 130);
        lv.Columns.Add("缓冲区偏移", 100, HorizontalAlignment.Right);
        lv.Columns.Add("原始字节 (Hex)", 590);
        lv.Columns.Add("期望 CRC", 90, HorizontalAlignment.Center);
        lv.Columns.Add("实际 CRC", 90, HorizontalAlignment.Center);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(8, 6, 8, 6) };
        var closeBtn = new Button { Text = "关闭", AutoSize = true, Dock = DockStyle.Right };
        var refreshBtn = new Button { Text = "刷新", AutoSize = true, Dock = DockStyle.Right };
        var clearBtn = new Button { Text = "清空记录", AutoSize = true, Dock = DockStyle.Left };
        closeBtn.Click += (_, _) => Close();
        refreshBtn.Click += (_, _) => Reload();
        clearBtn.Click += (_, _) => { parser.ClearRecentErrors(); Reload(); };
        bottom.Controls.Add(closeBtn);
        bottom.Controls.Add(refreshBtn);
        bottom.Controls.Add(clearBtn);

        Controls.Add(lv);
        Controls.Add(summary);
        Controls.Add(bottom);

        Reload();
    }

    private void Reload()
    {
        lv.BeginUpdate();
        lv.Items.Clear();
        var errors = parser.GetRecentErrors();
        for (int k = errors.Length - 1; k >= 0; k--)
        {
            var e = errors[k];
            var it = new ListViewItem((errors.Length - k).ToString());
            it.SubItems.Add(e.Time.ToString("HH:mm:ss.fff"));
            it.SubItems.Add(e.Offset.ToString("N0"));
            it.SubItems.Add(BitConverter.ToString(e.Raw).Replace('-', ' '));
            it.SubItems.Add($"0x{e.ExpectedCrc:X2}").ForeColor = Color.SeaGreen;
            it.SubItems.Add($"0x{e.ActualCrc:X2}").ForeColor = Color.Firebrick;
            lv.Items.Add(it);
        }
        lv.EndUpdate();

        summary.Text =
            $"累计 CRC 错误 {parser.CrcErrors:N0}   ·   丢弃字节 {parser.DroppedBytes:N0}   ·   显示最近 {errors.Length} 条。\r\n" +
            $"头仍是 AB CD，载荷像错位 → 丢字节；头仍是 AB CD，个别位翻转 → 噪声；偏移接近 19 的倍数 → 同一帧反复失败。";
    }
}