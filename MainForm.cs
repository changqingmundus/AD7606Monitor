using System.Globalization;
using System.IO.Ports;

namespace Ad7606Host;

public sealed class MainForm : Form
{
    private const string UsbSerialNumber = "FTBUT9NG";

    private static readonly byte[] UsbStartCmd = { 0xBB, 0x01, 0x00, 0x08 };
    private static readonly byte[] UsbStopCmd = { 0xBB, 0x02 };

    private readonly ComboBox portBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly ComboBox connectionBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85 };
    private readonly ComboBox baudBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly Button refreshButton = new() { Text = "刷新串口", AutoSize = true };
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
    private readonly Label[] rawLabels = new Label[8];
    private readonly Label[] signedLabels = new Label[8];
    private readonly Label[] voltageLabels = new Label[8];
    private readonly CheckBox[] waveChecks = new CheckBox[8];
    private readonly WaveformPanel waveform = new();
    private readonly ComboBox windowBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 95 };
    private readonly ComboBox waveRangeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 95 };
    private readonly NumericUpDown waveCenterBox = new() { Minimum = -10, Maximum = 10, DecimalPlaces = 2, Increment = 0.1M, Value = 2.5M, Width = 72 };
    private readonly Button waveRunButton = new() { Text = "开始波形采集", AutoSize = true, Enabled = false };
    private readonly Label waveInfo = new() { AutoSize = true, Text = "选择通道后开始采集" };

    private SerialPort? serial;
    private Ftd2xxUsb? usb;
    private bool IsUsb => connectionBox.SelectedIndex == 1;
    private bool busy;
    private ReadMode mode;
    private readonly object historyLock = new();
    private readonly List<Sample> history = new();
    private readonly object ioLock = new();
    private long frameIndexCounter;   // 全局帧序号（Interlocked），UART/USB 通用
    private static readonly Color[] ChannelColors = { Color.DodgerBlue, Color.OrangeRed, Color.SeaGreen, Color.MediumPurple, Color.DarkOrange, Color.DeepPink, Color.Teal, Color.SaddleBrown };

    private CancellationTokenSource? pollCts;
    private Task? pollTask;
    private double currentRate;

    private enum ReadMode { Stopped, All, Single, Waveform }

    private readonly struct Sample
    {
        public readonly long Index;
        public readonly double[] Volts;
        public Sample(long i, double[] v) { Index = i; Volts = v; }
    }

    private sealed record PollOptions(int StartChannel, int Count, int IntervalMs, double FullScale, bool IsUsb, double HistorySpanSeconds, int SampleRate);

    public MainForm()
    {
        Text = "AD7606 电压采集上位机";
        MinimumSize = new Size(960, 650); Size = new Size(1120, 760);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(245, 247, 250);
        baudBox.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200", "2000000" }); baudBox.SelectedItem = "2000000";
        connectionBox.Items.AddRange(new object[] { "UART", "USB FIFO" }); connectionBox.SelectedIndex = 0;
        connectionBox.SelectedIndexChanged += (_, _) =>
        {
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
            "50 µs", "100 µs", "500 µs",
            "1 ms", "5 ms", "10 ms", "20 ms", "50 ms",
            "100 ms", "500 ms", "1 s", "5 s"
        });
        windowBox.SelectedIndex = 5;   // 默认 10 ms
        windowBox.SelectedIndexChanged += (_, _) => waveform.Invalidate();
        for (int i = 0; i < 8; i++)
        {
            channelBox.Items.Add($"通道 {i + 1}");
            waveChecks[i] = new CheckBox { Text = $"CH{i + 1}", AutoSize = true, Checked = i == 0, ForeColor = ChannelColors[i], Font = new Font(Font, FontStyle.Bold) };
            waveChecks[i].CheckedChanged += (_, _) => waveform.Invalidate();
        }
        channelBox.SelectedIndex = 0;
        BuildLayout(); RefreshPorts();
        refreshButton.Click += (_, _) => RefreshPorts();
        connectButton.Click += (_, _) => ToggleConnection();
        allRunButton.Click += (_, _) => ToggleMode(ReadMode.All);
        oneRunButton.Click += (_, _) => ToggleMode(ReadMode.Single);
        allOnceButton.Click += async (_, _) => await ReadChannelsAsync(false);
        oneOnceButton.Click += async (_, _) => await ReadChannelsAsync(false);
        waveRunButton.Click += (_, _) => { if (mode == ReadMode.Waveform) StopPolling(); else SetMode(ReadMode.Waveform); };
        waveform.Paint += DrawWaveform;
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

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); Controls.Add(root);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 10), Margin = Padding.Empty };
        top.Controls.Add(new Label { Text = "AD7606 电压采集", Font = new Font(Font.FontFamily, 17, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 6, 18, 0) });
        AddLabeled(top, "连接方式", connectionBox); AddLabeled(top, "串口", portBox); top.Controls.Add(refreshButton); AddLabeled(top, "波特率", baudBox); top.Controls.Add(connectButton);
        top.Controls.Add(new Label { Width = 8 }); top.Controls.Add(allRunButton); top.Controls.Add(allOnceButton);
        AddLabeled(top, "单通道", channelBox); top.Controls.Add(oneRunButton); top.Controls.Add(oneOnceButton);
        AddLabeled(top, "量程", rangeBox); AddLabeled(top, "采样间隔 ms", intervalBox); AddLabeled(top, "标称采样率 Hz", sampleRateBox);
        top.Controls.Add(statusLabel); top.Controls.Add(rateLabel); top.Controls.Add(usbRateLabel);
        root.Controls.Add(top, 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var valuesTab = new TabPage("通道数据") { BackColor = BackColor };
        var valuesLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(4) };
        valuesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); valuesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        valuesLayout.Controls.Add(lastReadLabel, 0, 0);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
        for (int c = 0; c < 4; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        for (int r = 0; r < 2; r++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        for (int i = 0; i < 8; i++) grid.Controls.Add(MakeCard(i), i % 4, i / 4);
        valuesLayout.Controls.Add(grid, 0, 1); valuesTab.Controls.Add(valuesLayout); tabs.TabPages.Add(valuesTab);

        var waveTab = new TabPage("波形显示") { BackColor = BackColor };
        var waveLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(10) };
        waveLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); waveLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); waveLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var waveTools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        waveTools.Controls.Add(new Label { Text = "显示通道：", AutoSize = true, Padding = new Padding(0, 5, 4, 0) });
        foreach (var check in waveChecks) waveTools.Controls.Add(check);
        waveTools.Controls.Add(new Label { Text = "时间窗：", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        waveTools.Controls.Add(windowBox);
        waveTools.Controls.Add(new Label { Text = "电压范围(Vpp)：", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        waveTools.Controls.Add(waveRangeBox);
        waveTools.Controls.Add(new Label { Text = "垂直中心(V)：", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        waveTools.Controls.Add(waveCenterBox); waveTools.Controls.Add(waveRunButton);
        var clearButton = new Button { Text = "清空波形", AutoSize = true };
        clearButton.Click += (_, _) => { lock (historyLock) history.Clear(); waveform.Invalidate(); };
        waveTools.Controls.Add(clearButton);
        waveLayout.Controls.Add(waveTools, 0, 0);
        waveform.Dock = DockStyle.Fill; waveform.BackColor = Color.White; waveform.Margin = new Padding(0, 8, 0, 8); waveLayout.Controls.Add(waveform, 0, 1);
        waveInfo.Padding = new Padding(0, 4, 0, 0); waveLayout.Controls.Add(waveInfo, 0, 2);
        waveTab.Controls.Add(waveLayout); tabs.TabPages.Add(waveTab); root.Controls.Add(tabs, 0, 1);
    }

    private static void AddLabeled(FlowLayoutPanel panel, string title, Control control)
    { panel.Controls.Add(new Label { Text = title, AutoSize = true, Padding = new Padding(4, 7, 0, 0) }); panel.Controls.Add(control); }

    private Panel MakeCard(int i)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(6), Padding = new Padding(14), BorderStyle = BorderStyle.FixedSingle };
        panel.Controls.Add(new Label { Text = $"CH{i + 1:00}", Font = new Font(Font.FontFamily, 11, FontStyle.Bold), ForeColor = ChannelColors[i], AutoSize = true, Location = new Point(14, 14) });
        rawLabels[i] = new Label { Text = "原始值  0x----", AutoSize = true, Location = new Point(14, 50), ForeColor = Color.DimGray };
        signedLabels[i] = new Label { Text = "有符号值  0", AutoSize = true, Location = new Point(14, 77), ForeColor = Color.DimGray };
        voltageLabels[i] = new Label { Text = "0.0000 V", AutoSize = true, Location = new Point(14, 111), Font = new Font(Font.FontFamily, 17, FontStyle.Bold), ForeColor = Color.FromArgb(35, 55, 80) };
        panel.Controls.Add(rawLabels[i]); panel.Controls.Add(signedLabels[i]); panel.Controls.Add(voltageLabels[i]); return panel;
    }

    private void RefreshPorts()
    {
        var old = portBox.SelectedItem as string; portBox.Items.Clear();
        try { portBox.Items.AddRange(SerialPort.GetPortNames().OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray()); }
        catch (Exception ex) { SetStatus($"枚举串口失败：{ex.Message}", true); }
        if (portBox.Items.Count > 0) portBox.SelectedItem = old is not null && portBox.Items.Contains(old) ? old : portBox.Items[0];
    }

    private void ToggleConnection()
    {
        if (serial?.IsOpen == true || usb is not null) { StopPolling(); ClosePort(); SetConnected(false); SetStatus("已断开", false); return; }

        if (IsUsb)
        {
            try
            {
                usb = new Ftd2xxUsb();
                usb.OpenBySerial(UsbSerialNumber);
                SetConnected(true);
                SetStatus($"已连接 USB FIFO · 序列号 {UsbSerialNumber}", false);
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

    private void ToggleMode(ReadMode requested)
    { if (mode == requested) StopPolling(); else SetMode(requested); }

    private void SetMode(ReadMode requested)
    {
        if (serial?.IsOpen != true && usb is null) { SetStatus("请先连接 UART 或 USB", true); return; }
        StopPolling();
        mode = requested;

        var options = new PollOptions(
            StartChannel: requested == ReadMode.Single ? channelBox.SelectedIndex : 0,
            Count: requested == ReadMode.Single ? 1 : 8,
            IntervalMs: (int)intervalBox.Value,
            FullScale: rangeBox.SelectedIndex == 1 ? 10.0 : 5.0,
            IsUsb: IsUsb,
            HistorySpanSeconds: WindowSpanSeconds(),
            SampleRate: (int)sampleRateBox.Value);

        pollCts = new CancellationTokenSource();
        var token = pollCts.Token;

        if (IsUsb)
            pollTask = Task.Run(() => StreamLoopAsync(options, token));
        else
            pollTask = Task.Run(() => PollLoopAsync(options, token));

        UpdateModeButtons();
        SetStatus(requested switch
        {
            ReadMode.All => IsUsb ? "USB 流模式连续采集中" : "连续读取全部通道中",
            ReadMode.Single => $"连续读取通道 {channelBox.SelectedIndex + 1} 中",
            _ => IsUsb ? "USB 流模式波形采集中" : "波形采集中"
        }, false);
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
    }

    private double WindowSpanSeconds()
    {
        string s = (string)(windowBox.SelectedItem ?? "10 ms");
        var parts = s.Split(' ');
        double v = double.Parse(parts[0], CultureInfo.InvariantCulture);
        return parts[1] switch { "µs" => v / 1e6, "ms" => v / 1e3, _ => v };
    }

    // ---------- 帧解析器：AB CD + 16B payload + CRC-8(poly=0x07) ----------

    private sealed class FrameParser
    {
        public const int PayloadSize = 16;
        public const int FrameSize = PayloadSize + 3;
        private const byte Header0 = 0xAB, Header1 = 0xCD;

        private byte[] buf = new byte[64 * 1024];
        private int len;
        public long CrcErrors;
        public long DroppedBytes;

        public List<byte[]> Feed(byte[] data)
        {
            if (len + data.Length > buf.Length)
            {
                int newSize = Math.Max(buf.Length * 2, len + data.Length);
                Array.Resize(ref buf, newSize);
            }
            Buffer.BlockCopy(data, 0, buf, len, data.Length);
            len += data.Length;

            var frames = new List<byte[]>();
            int i = 0;
            while (i + FrameSize <= len)
            {
                if (buf[i] != Header0 || buf[i + 1] != Header1)
                {
                    i++; DroppedBytes++;
                    continue;
                }
                byte crc = Crc8(buf, i + 2, PayloadSize);
                if (crc != buf[i + FrameSize - 1])
                {
                    i++; CrcErrors++;
                    continue;
                }
                var payload = new byte[PayloadSize];
                Buffer.BlockCopy(buf, i + 2, payload, 0, PayloadSize);
                frames.Add(payload);
                i += FrameSize;
            }
            if (i > 0)
            {
                Buffer.BlockCopy(buf, i, buf, 0, len - i);
                len -= i;
            }
            return frames;
        }

        public void Reset() { len = 0; }

        public static byte Crc8(byte[] data, int offset, int count)
        {
            byte crc = 0x00;
            for (int k = 0; k < count; k++)
            {
                crc ^= data[offset + k];
                for (int b = 0; b < 8; b++)
                    crc = (byte)(((crc & 0x80) != 0) ? ((crc << 1) ^ 0x07) : (crc << 1));
            }
            return crc;
        }
    }

    // ---------- USB 流模式 ----------

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
        var state = new StreamState();
        var parser = new FrameParser();
        const int MaxBlockBytes = 128 * 1024;
        double fullScale = options.FullScale;

        await Task.Run(() => { lock (ioLock) { dev.Purge(); dev.Write(UsbStartCmd); } }, token);

        try
        {
            while (!token.IsCancellationRequested)
            {
                byte[] block = await Task.Run(() =>
                {
                    lock (ioLock) return dev.ReadAvailable(MaxBlockBytes);
                }, token);

                if (block.Length == 0) continue;

                state.BytesWindow += block.Length;
                state.TotalBytes += block.Length;

                List<byte[]> frames = parser.Feed(block);
                if (frames.Count == 0) continue;

                double[] lastVolts = new double[8];
                long lastIndex = 0;

                lock (historyLock)
                {
                    for (int f = 0; f < frames.Count; f++)
                    {
                        byte[] payload = frames[f];
                        var volts = new double[8];
                        for (int ch = 0; ch < 8; ch++)
                        {
                            short v = unchecked((short)((payload[ch * 2] << 8) | payload[ch * 2 + 1]));
                            volts[ch] = v * fullScale / 32768.0;
                        }
                        long idx = Interlocked.Increment(ref frameIndexCounter);
                        history.Add(new Sample(idx, volts));
                        if (f == frames.Count - 1) { lastVolts = volts; lastIndex = idx; }
                    }
                    TrimHistory(lastIndex, options.HistorySpanSeconds, options.SampleRate);
                }

                state.FramesWindow += frames.Count;
                state.TotalFrames += frames.Count;

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
                    }
                    long crcErrors = parser.CrcErrors;
                    long droppedBytes = parser.DroppedBytes;
                    long totalFrames = state.TotalFrames;
                    long totalBytes = state.TotalBytes;
                    double lastV = 0;
                    var lastSnapshot = new double[8];
                    Array.Copy(lastVolts, lastSnapshot, 8);

                    SafeInvoke(() =>
                    {
                        ApplyChannelsToUi(0, 8, lastSnapshot, fullScale);
                        lastReadLabel.Text = $"流模式 · 累计 {totalFrames:N0} 帧 / {totalBytes / 1024.0 / 1024.0:F2} MB";
                        if (rateHz > 0)
                        {
                            currentRate = rateHz;
                            rateLabel.Text = $"实际帧率：{rateHz:F1} Hz";
                            usbRateLabel.Text = $"USB：{mbPerSec:F2} MB/s ({mbPerSec * 8:F1} Mbps) · CRC错 {crcErrors} · 丢弃 {droppedBytes}";
                        }
                        if (mode == ReadMode.Waveform)
                        {
                            int cached;
                            lock (historyLock) cached = history.Count;
                            waveInfo.Text = $"流采集中 · 缓存 {cached:N0} 点 · 实际 {rateHz:F0} Hz · {mbPerSec:F2} MB/s";
                        }
                    });
                }

                if ((now - state.LastWaveUpdate).TotalMilliseconds >= 40)
                {
                    state.LastWaveUpdate = now;
                    SafeInvoke(() => waveform.Invalidate());
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
            try { await Task.Run(() => { lock (ioLock) dev.Write(UsbStopCmd); }); } catch { }
            try { await Task.Run(() => { lock (ioLock) dev.Purge(); }); } catch { }
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

    // ---------- UART 轮询 ----------

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
            lock (historyLock) { history.Add(new Sample(idx, voltsArr)); TrimHistory(idx, options.HistorySpanSeconds, options.SampleRate); }
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

    // ---------- 单次读取 ----------

    private async Task ReadChannelsAsync(bool background)
    {
        if (busy || (serial?.IsOpen != true && usb is null)) return;
        busy = true; UpdateModeButtons();
        try
        {
            double fullScale = rangeBox.SelectedIndex == 1 ? 10.0 : 5.0;
            var voltsByChannel = new double[8];

            if (IsUsb)
            {
                var dev = usb ?? throw new InvalidOperationException("USB 设备未连接");
                byte[]? payload = null;
                await Task.Run(() =>
                {
                    lock (ioLock)
                    {
                        dev.Purge();
                        dev.Write(UsbStartCmd);
                        try
                        {
                            var parser = new FrameParser();
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
            }
            else
            {
                int startChannel = channelBox.SelectedIndex;
                int count = ModifierKeys.HasFlag(Keys.Control) ? 1 : 8;
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
            }

            long idx = Interlocked.Increment(ref frameIndexCounter);
            lock (historyLock) { history.Add(new Sample(idx, voltsByChannel)); TrimHistory(idx, WindowSpanSeconds(), (int)sampleRateBox.Value); }
            waveform.Invalidate();
            lastReadLabel.Text = $"最近读取：{DateTime.Now:HH:mm:ss.fff} · 单次";
            if (!background) SetStatus("单次读取成功", false);
        }
        catch (TimeoutException ex) { SetStatus($"响应超时：{ex.Message}", true); if (mode != ReadMode.Stopped) StopPolling(); }
        catch (Exception ex) { SetStatus($"读取失败：{ex.Message}", true); if (mode != ReadMode.Stopped) StopPolling(); }
        finally { busy = false; UpdateModeButtons(); }
    }

    // ---------- 历史缓冲 & 波形绘制 ----------

    private void TrimHistory(long maxIndex, double spanSeconds, int sampleRate)
    {
        // 保留 2 倍窗口 + 余量，避免频繁裁剪
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
        int sampleRate = (int)sampleRateBox.Value;

        // 时间轴：用帧序号映射，不再依赖 DateTime（避免采样率填错导致波形被压缩）
        long windowPoints = Math.Max(10, (long)(spanSeconds * sampleRate));

        // 网格与纵轴
        for (int i = 0; i <= 4; i++)
        {
            float y = plot.Top + plot.Height * i / 4f; g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            double v = centerVoltage + halfRange - 2 * halfRange * i / 4.0;
            g.DrawString($"{v:0.###} V", font, labelBrush, 2, y - 8);
        }
        // 横轴标签：按标称采样率换算名义时间
        for (int i = 0; i <= 4; i++)
        {
            float x = plot.Left + plot.Width * i / 4f; g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            double t = spanSeconds * (4 - i) / 4.0;
            g.DrawString(t <= 0 ? "0" : $"-{FormatSpan(t)}", font, labelBrush, x - 16, plot.Bottom + 7);
        }
        g.DrawRectangle(axisPen, plot);

        bool[] checkedCh = new bool[8];
        for (int i = 0; i < 8; i++) checkedCh[i] = waveChecks[i].Checked;

        // 取时间窗内的点
        Sample[] points;
        long maxIndex;
        lock (historyLock)
        {
            if (history.Count == 0) { points = Array.Empty<Sample>(); maxIndex = 0; }
            else
            {
                maxIndex = history[history.Count - 1].Index;
                long minIndex = maxIndex - windowPoints;
                int lo = 0, hi = history.Count;
                while (lo < hi) { int mid = (lo + hi) / 2; if (history[mid].Index < minIndex) lo = mid + 1; else hi = mid; }
                int n = history.Count - lo;
                const int MaxDrawPoints = 200_000;
                if (n <= 0) points = Array.Empty<Sample>();
                else if (n <= MaxDrawPoints)
                {
                    points = new Sample[n];
                    for (int i = 0; i < n; i++) points[i] = history[lo + i];
                }
                else
                {
                    int step = (n + MaxDrawPoints - 1) / MaxDrawPoints;
                    int m = (n + step - 1) / step;
                    points = new Sample[m];
                    for (int i = 0, j = lo; i < m; i++, j += step) points[i] = history[j];
                }
            }
        }

        long startIndex = maxIndex - windowPoints + 1;
        int width = plot.Width;
        bool tooSparse = points.Length > 0 && points.Length < 10;

        foreach (int ch in Enumerable.Range(0, 8).Where(i => checkedCh[i]))
        {
            if (points.Length == 0) continue;

            // 稀疏：直接连线；密集：每像素列 min-max
            if (points.Length <= width)
            {
                using var pen = new Pen(ChannelColors[ch], 1.6f);
                var pts = new List<PointF>(points.Length);
                foreach (var s in points)
                {
                    if (s.Index < startIndex) continue;
                    float x = plot.Left + (float)((s.Index - startIndex) / (double)windowPoints * width);
                    float y = plot.Top + (float)((centerVoltage + halfRange - s.Volts[ch]) / (2 * halfRange) * plot.Height);
                    if (y < plot.Top) y = plot.Top;
                    if (y > plot.Bottom) y = plot.Bottom;
                    pts.Add(new PointF(x, y));
                }
                if (pts.Count >= 2) g.DrawLines(pen, pts.ToArray());
                else if (pts.Count == 1)
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

                foreach (var s in points)
                {
                    if (s.Index < startIndex) continue;
                    int x = plot.Left + (int)((s.Index - startIndex) / (double)windowPoints * width);
                    if (x < plot.Left) x = plot.Left;
                    if (x > plot.Right) x = plot.Right;

                    float y = plot.Top + (float)((centerVoltage + halfRange - s.Volts[ch]) / (2 * halfRange) * plot.Height);
                    if (y < plot.Top) y = plot.Top;
                    if (y > plot.Bottom) y = plot.Bottom;

                    if (!hasPrev) { prevX = x; colMin = colMax = y; hasPrev = true; continue; }
                    if (x == prevX)
                    {
                        if (y < colMin) colMin = y;
                        if (y > colMax) colMax = y;
                    }
                    else
                    {
                        g.DrawLine(pen, prevX, colMin, prevX, colMax);
                        prevX = x; colMin = colMax = y;
                    }
                }
                if (hasPrev) g.DrawLine(pen, prevX, colMin, prevX, colMax);
            }
        }

        string hint = $"时间窗 {FormatSpan(spanSeconds)} ≈ {windowPoints:N0} 点 · 窗口内实际 {points.Length:N0} 点";
        g.DrawString(hint, font, labelBrush, plot.Left, plot.Top + 2);

        if (points.Length == 0)
            g.DrawString("开始波形采集后将在此显示所选通道", new Font(Font, FontStyle.Bold), Brushes.Gray, plot.Left + 25, plot.Top + plot.Height / 2);
        else if (tooSparse)
            g.DrawString("窗口内点数不足 10 个，请把时间窗调大；若采样率确实不够，请先解决 FPGA 帧率", new Font(Font, FontStyle.Bold), Brushes.Firebrick, plot.Left + 25, plot.Top + plot.Height / 2);
    }

    private void SetConnected(bool connected)
    {
        connectionBox.Enabled = portBox.Enabled = baudBox.Enabled = refreshButton.Enabled = !connected;
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