using System.Globalization;
using System.IO.Ports;

namespace Ad7606Host;

public sealed class MainForm : Form
{
    private readonly ComboBox portBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
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
    private readonly Label statusLabel = new() { AutoSize = true, Text = "未连接", ForeColor = Color.DimGray, Padding = new Padding(5, 7, 0, 0) };
    private readonly Label lastReadLabel = new() { AutoSize = true, Text = "最近读取：—", ForeColor = Color.DimGray, Padding = new Padding(0, 3, 0, 8) };
    private readonly Label[] rawLabels = new Label[8];
    private readonly Label[] signedLabels = new Label[8];
    private readonly Label[] voltageLabels = new Label[8];
    private readonly System.Windows.Forms.Timer pollTimer = new() { Interval = 100 };
    private readonly CheckBox[] waveChecks = new CheckBox[8];
    private readonly WaveformPanel waveform = new();
    private readonly NumericUpDown windowSeconds = new() { Minimum = 1, Maximum = 300, Value = 10, Width = 65 };
    private readonly ComboBox waveRangeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 95 };
    private readonly NumericUpDown waveCenterBox = new() { Minimum = -10, Maximum = 10, DecimalPlaces = 2, Increment = 0.1M, Value = 2.5M, Width = 72 };
    private readonly Button waveRunButton = new() { Text = "开始波形采集", AutoSize = true, Enabled = false };
    private readonly Label waveInfo = new() { AutoSize = true, Text = "选择通道后开始采集" };
    private SerialPort? serial;
    private bool busy;
    private ReadMode mode;
    private readonly object historyLock = new();
    private readonly List<Sample> history = new();
    private static readonly Color[] ChannelColors = { Color.DodgerBlue, Color.OrangeRed, Color.SeaGreen, Color.MediumPurple, Color.DarkOrange, Color.DeepPink, Color.Teal, Color.SaddleBrown };

    private enum ReadMode { Stopped, All, Single, Waveform }
    private sealed record Sample(DateTime Time, double[] Volts);

    public MainForm()
    {
        Text = "AD7606 电压采集上位机";
        MinimumSize = new Size(960, 650); Size = new Size(1120, 760);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(245, 247, 250);
        baudBox.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200", "2000000" }); baudBox.SelectedItem = "2000000";
        rangeBox.Items.AddRange(new object[] { "±5 V", "±10 V" }); rangeBox.SelectedIndex = 0;
        waveRangeBox.Items.AddRange(new object[] { "0.25 Vpp", "0.5 Vpp", "1 Vpp", "2 Vpp", "5 Vpp", "10 Vpp", "20 Vpp" });
        waveRangeBox.SelectedIndex = 1;
        waveRangeBox.SelectedIndexChanged += (_, _) => waveform.Invalidate();
        waveCenterBox.ValueChanged += (_, _) => waveform.Invalidate();
        for (int i = 0; i < 8; i++) { channelBox.Items.Add($"通道 {i + 1}"); waveChecks[i] = new CheckBox { Text = $"CH{i + 1}", AutoSize = true, Checked = i == 0, ForeColor = ChannelColors[i], Font = new Font(Font, FontStyle.Bold) }; waveChecks[i].CheckedChanged += (_, _) => waveform.Invalidate(); }
        channelBox.SelectedIndex = 0;
        BuildLayout(); RefreshPorts();
        refreshButton.Click += (_, _) => RefreshPorts(); connectButton.Click += (_, _) => ToggleConnection();
        allRunButton.Click += (_, _) => ToggleMode(ReadMode.All);
        oneRunButton.Click += (_, _) => ToggleMode(ReadMode.Single);
        allOnceButton.Click += async (_, _) => await ReadChannelsAsync(0, 8, false);
        oneOnceButton.Click += async (_, _) => await ReadChannelsAsync(channelBox.SelectedIndex, 1, false);
        waveRunButton.Click += (_, _) => { if (mode == ReadMode.Waveform) StopPolling(); else { SetMode(ReadMode.Waveform); } };
        pollTimer.Tick += async (_, _) => await PollTickAsync();
        intervalBox.ValueChanged += (_, _) => pollTimer.Interval = (int)intervalBox.Value;
        waveform.Paint += DrawWaveform;
        FormClosing += (_, _) => { pollTimer.Stop(); ClosePort(); };
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); Controls.Add(root);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 10), Margin = Padding.Empty };
        top.Controls.Add(new Label { Text = "AD7606 电压采集", Font = new Font(Font.FontFamily, 17, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 6, 18, 0) });
        AddLabeled(top, "串口", portBox); top.Controls.Add(refreshButton); AddLabeled(top, "波特率", baudBox); top.Controls.Add(connectButton);
        top.Controls.Add(new Label { Width = 8 }); top.Controls.Add(allRunButton); top.Controls.Add(allOnceButton);
        AddLabeled(top, "单通道", channelBox); top.Controls.Add(oneRunButton); top.Controls.Add(oneOnceButton);
        AddLabeled(top, "量程", rangeBox); AddLabeled(top, "采样间隔 ms", intervalBox); top.Controls.Add(statusLabel);
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
        waveTools.Controls.Add(new Label { Text = "时间窗(s)：", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        waveTools.Controls.Add(windowSeconds);
        waveTools.Controls.Add(new Label { Text = "电压范围(Vpp)：", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        waveTools.Controls.Add(waveRangeBox);
        waveTools.Controls.Add(new Label { Text = "垂直中心(V)：", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        waveTools.Controls.Add(waveCenterBox); waveTools.Controls.Add(waveRunButton);
        var clearButton = new Button { Text = "清空波形", AutoSize = true }; clearButton.Click += (_, _) => { lock (historyLock) history.Clear(); waveform.Invalidate(); };
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
        if (serial?.IsOpen == true) { StopPolling(); ClosePort(); SetConnected(false); SetStatus("已断开", false); return; }
        if (portBox.SelectedItem is not string port) { SetStatus("请先选择串口", true); return; }
        try
        {
            serial = new SerialPort(port, int.Parse((string)baudBox.SelectedItem!, CultureInfo.InvariantCulture), Parity.None, 8, StopBits.One)
            { ReadTimeout = 1000, WriteTimeout = 1000, DtrEnable = false, RtsEnable = false, Handshake = Handshake.None };
            serial.Open(); serial.DiscardInBuffer(); serial.DiscardOutBuffer(); SetConnected(true); SetStatus($"已连接 {port}", false);
        }
        catch (Exception ex) { ClosePort(); SetConnected(false); SetStatus($"连接失败：{ex.Message}", true); }
    }

    private void ToggleMode(ReadMode requested)
    { if (mode == requested) StopPolling(); else SetMode(requested); }

    private void SetMode(ReadMode requested)
    {
        if (serial?.IsOpen != true) { SetStatus("请先连接串口", true); return; }
        mode = requested; pollTimer.Interval = (int)intervalBox.Value; pollTimer.Start(); UpdateModeButtons();
        SetStatus(requested switch { ReadMode.All => "连续读取全部通道中", ReadMode.Single => $"连续读取通道 {channelBox.SelectedIndex + 1} 中", _ => "波形采集中" }, false);
    }

    private void StopPolling()
    { pollTimer.Stop(); mode = ReadMode.Stopped; UpdateModeButtons(); }

    private void UpdateModeButtons()
    {
        allRunButton.Text = mode == ReadMode.All ? "停止读取全部" : "开始读取全部";
        oneRunButton.Text = mode == ReadMode.Single ? "停止单通道" : "开始单通道";
        waveRunButton.Text = mode == ReadMode.Waveform ? "停止波形采集" : "开始波形采集";
        bool connected = serial?.IsOpen == true;
        allRunButton.Enabled = connected && (mode == ReadMode.Stopped || mode == ReadMode.All);
        allOnceButton.Enabled = connected && mode == ReadMode.Stopped && !busy;
        oneRunButton.Enabled = connected && (mode == ReadMode.Stopped || mode == ReadMode.Single);
        oneOnceButton.Enabled = connected && mode == ReadMode.Stopped && !busy;
        waveRunButton.Enabled = connected && (mode == ReadMode.Stopped || mode == ReadMode.Waveform);
        channelBox.Enabled = mode != ReadMode.Single;
    }

    private async Task PollTickAsync()
    {
        if (busy || serial?.IsOpen != true || mode == ReadMode.Stopped) return;
        if (mode == ReadMode.Single) await ReadChannelsAsync(channelBox.SelectedIndex, 1, true);
        else await ReadChannelsAsync(0, 8, true);
    }

    private async Task ReadChannelsAsync(int startChannel, int count, bool background)
    {
        if (busy || serial?.IsOpen != true) return;
        busy = true; UpdateModeButtons();
        try
        {
            var port = serial;
            byte[] cmd = { 0xAA, 0x01, (byte)startChannel, (byte)count };
            port.DiscardInBuffer(); port.Write(cmd, 0, cmd.Length);
            byte[] response = await Task.Run(() => ReadExact(port, count * 2, TimeSpan.FromSeconds(1.5)));
            var voltsByChannel = new double[8];
            for (int i = 0; i < count; i++)
            {
                ushort raw = (ushort)((response[i * 2] << 8) | response[i * 2 + 1]);
                short value = unchecked((short)raw); double volts = value * (rangeBox.SelectedIndex == 1 ? 10.0 : 5.0) / 32768.0;
                int ch = startChannel + i; voltsByChannel[ch] = volts;
                rawLabels[ch].Text = $"原始值  0x{raw:X4}"; signedLabels[ch].Text = $"有符号值  {value}";
                voltageLabels[ch].Text = $"{volts:+0.0000;-0.0000;0.0000} V";
            }
            DateTime now = DateTime.Now;
            if (count == 8)
            {
                lock (historyLock) { history.Add(new Sample(now, voltsByChannel)); TrimHistory(now); }
                waveform.Invalidate();
            }
            lastReadLabel.Text = $"最近读取：{now:yyyy-MM-dd HH:mm:ss.fff} · {(count == 8 ? "全部 8 通道" : $"通道 {startChannel + 1}")}";
            if (!background) SetStatus("单次读取成功", false);
            if (mode == ReadMode.Waveform) waveInfo.Text = $"采集中 · 当前缓存 {history.Count:N0} 个样本点 · 采样间隔约 {intervalBox.Value} ms";
        }
        catch (TimeoutException ex) { SetStatus($"响应超时：{ex.Message}", true); if (mode != ReadMode.Stopped) StopPolling(); }
        catch (Exception ex) { SetStatus($"读取失败：{ex.Message}", true); if (mode != ReadMode.Stopped) StopPolling(); }
        finally { busy = false; UpdateModeButtons(); }
    }

    private void TrimHistory(DateTime now)
    {
        DateTime cutoff = now.AddSeconds(-(double)windowSeconds.Value - 2);
        int remove = history.FindIndex(s => s.Time >= cutoff);
        if (remove > 0) history.RemoveRange(0, remove);
        if (history.Count > 100000) history.RemoveRange(0, history.Count - 100000);
    }

    private static byte[] ReadExact(SerialPort port, int length, TimeSpan timeout)
    {
        byte[] data = new byte[length]; int offset = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
        while (offset < length)
        {
            if (sw.Elapsed >= timeout) throw new TimeoutException($"已收到 {offset}/{length} 字节");
            int available = port.BytesToRead;
            if (available == 0) { Thread.Sleep(2); continue; }
            offset += port.Read(data, offset, Math.Min(available, length - offset));
        }
        return data;
    }

    private void DrawWaveform(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Rectangle r = waveform.ClientRectangle; if (r.Width < 100 || r.Height < 100) return;
        const int left = 65, right = 18, top = 20, bottom = 42;
        var plot = new Rectangle(left, top, r.Width - left - right, r.Height - top - bottom);
        using var gridPen = new Pen(Color.FromArgb(225, 230, 236)); using var axisPen = new Pen(Color.Gray);
        using var font = new Font("Microsoft YaHei UI", 8.5f); using var labelBrush = new SolidBrush(Color.DimGray);
        string rangeText = (string)(waveRangeBox.SelectedItem ?? "0.5 Vpp");
        double halfRange = double.Parse(rangeText.Split(" Vpp")[0], CultureInfo.InvariantCulture) / 2.0;
        double centerVoltage = (double)waveCenterBox.Value;
        for (int i = 0; i <= 4; i++)
        {
            float y = plot.Top + plot.Height * i / 4f; g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            double v = centerVoltage + halfRange - 2 * halfRange * i / 4.0; g.DrawString($"{v:0.###} V", font, labelBrush, 2, y - 8);
            float x = plot.Left + plot.Width * i / 4f; g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            g.DrawString($"-{(double)windowSeconds.Value * i / 4:0.#}s", font, labelBrush, x - 16, plot.Bottom + 7);
        }
        g.DrawRectangle(axisPen, plot);
        Sample[] points; lock (historyLock) points = history.ToArray();
        DateTime now = DateTime.Now; double span = (double)windowSeconds.Value;
        foreach (int ch in Enumerable.Range(0, 8).Where(i => waveChecks[i].Checked))
        {
            var coords = new List<PointF>();
            foreach (var sample in points)
            {
                double age = (now - sample.Time).TotalSeconds; if (age < 0 || age > span) continue;
                float x = plot.Right - (float)(age / span * plot.Width);
                float y = plot.Top + (float)((centerVoltage + halfRange - sample.Volts[ch]) / (2 * halfRange) * plot.Height);
                coords.Add(new PointF(x, y));
            }
            if (coords.Count > 1) { using var p = new Pen(ChannelColors[ch], 1.5f); g.DrawLines(p, coords.ToArray()); }
        }
        g.DrawString("时间（最新在右）", font, labelBrush, plot.Left + plot.Width / 2 - 36, r.Height - 21);
        if (points.Length == 0) g.DrawString("开始波形采集后将在此显示所选通道", new Font(Font, FontStyle.Bold), Brushes.Gray, plot.Left + 25, plot.Top + plot.Height / 2);
    }

    private void SetConnected(bool connected)
    {
        portBox.Enabled = baudBox.Enabled = refreshButton.Enabled = !connected;
        connectButton.Text = connected ? "断开" : "连接";
        if (!connected) StopPolling(); UpdateModeButtons();
    }
    private void SetStatus(string text, bool error) { statusLabel.Text = text; statusLabel.ForeColor = error ? Color.Firebrick : Color.SeaGreen; }
    private void ClosePort() { try { if (serial?.IsOpen == true) serial.Close(); } catch { } serial?.Dispose(); serial = null; }
}

internal sealed class WaveformPanel : Panel
{
    public WaveformPanel() { DoubleBuffered = true; ResizeRedraw = true; }
}



