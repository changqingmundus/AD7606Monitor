using System.Linq;
using System.Numerics;
using System.Drawing.Drawing2D;

namespace Ad7606Host;

internal enum SpectrumWindow { Hann, Hamming, Blackman, Rectangular }

internal sealed class SpectrumMeasurement
{
    public int Channel;
    public double FundamentalHz, FundamentalVrms, Dc, AcRms;
    public readonly double[] HarmonicsVrms = new double[4];   // H2~H5
    public double ThdPercent, ThdnPercent, NoiseFloorDbvPerRootHz, PhaseDeg;
}

internal sealed class SpectrumPanel : Panel
{
    private sealed class Trace
    {
        public int Channel;
        public Complex[] Fft = Array.Empty<Complex>();
        public double[] Vrms = Array.Empty<double>();
        public double[] Phase = Array.Empty<double>();
        public SpectrumMeasurement Measurement = new();
    }

    private readonly Trace[] traces = Enumerable.Range(0, 8).Select(i => new Trace { Channel = i }).ToArray();
    private readonly Color[] colors = {
        Color.DodgerBlue, Color.OrangeRed, Color.SeaGreen, Color.MediumPurple,
        Color.DarkOrange, Color.DeepPink, Color.Teal, Color.SaddleBrown
    };
    private readonly bool[] enabled = new bool[8];

    private int n, referenceChannel;
    private double sr = 200000, coherentGain = 1, rmsGain = 1, cursorHz = -1;
    private Rectangle amplitudePlot;

    public SpectrumPanel() { DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.White; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Channel { get; set; }
    public double MaxDisplayHz { get; set; } = 10_000;
    public double MinDisplayHz { get; set; } = 10;
    public bool UseDb { get; set; } = true;
    public SpectrumWindow Window { get; set; } = SpectrumWindow.Hann;
    public bool LogFrequency { get; set; } = true;

    public IReadOnlyList<SpectrumMeasurement> Measurements =>
        traces.Where(t => enabled[t.Channel]).Select(t => t.Measurement).ToArray();

    public string Info => n == 0
        ? "等待采集"
        : $"N={n} · 采样 {sr:F0} Hz · 分辨率 {sr / n:F3} Hz · {Window} 窗 · {(UseDb ? "dBVrms" : "Vrms")}";

    // 单通道兼容（其它地方如果还在用）
    public void ComputeAndSet(double[] samples, int count, double sampleRate)
    {
        var all = new double[8][];
        var selected = new bool[8];
        all[Channel] = samples;
        selected[Channel] = true;
        ComputeAndSetMulti(all, count, sampleRate, selected, Channel);
    }

    public void ComputeAndSetMulti(double[][] samplesByChannel, int count, double sampleRate,
                                    bool[] selected, int phaseReference)
    {
        int available = samplesByChannel
            .Where(s => s is not null)
            .Select(s => s!.Length)
            .DefaultIfEmpty(0)
            .Min();
        int length = HighestPowerOfTwo(Math.Min(count, available));
        if (length < 16) return;

        n = length;
        sr = sampleRate;
        referenceChannel = Math.Clamp(phaseReference, 0, 7);
        Array.Copy(selected, enabled, Math.Min(8, selected.Length));
        BuildWindow(length);

        for (int ch = 0; ch < 8; ch++)
        {
            if (!enabled[ch] || ch >= samplesByChannel.Length || samplesByChannel[ch] is null) continue;
            var source = samplesByChannel[ch]!;
            var trace = traces[ch];

            if (trace.Fft.Length != length)
            {
                trace.Fft = new Complex[length];
                trace.Vrms = new double[length / 2];
                trace.Phase = new double[length / 2];
            }

            int offset = Math.Max(0, source.Length - length);
            double dc = 0, sum2 = 0;
            for (int i = 0; i < length; i++) { double v = source[offset + i]; dc += v; sum2 += v * v; }
            dc /= length;

            for (int i = 0; i < length; i++)
                trace.Fft[i] = new Complex((source[offset + i] - dc) * WindowValue(i, length), 0);

            FftEngine.Forward(trace.Fft);

            for (int bin = 0; bin < length / 2; bin++)
            {
                trace.Vrms[bin] = bin == 0 ? 0 : 2 * trace.Fft[bin].Magnitude / (length * coherentGain * Math.Sqrt(2));
                trace.Phase[bin] = trace.Fft[bin].Phase * 180 / Math.PI;
            }
            trace.Measurement = new SpectrumMeasurement
            {
                Channel = ch,
                Dc = dc,
                AcRms = Math.Sqrt(Math.Max(0, sum2 / length - dc * dc))
            };
        }

        Trace? reference = enabled[referenceChannel]
            ? traces[referenceChannel]
            : traces.FirstOrDefault(t => enabled[t.Channel]);

        foreach (var trace in traces.Where(t => enabled[t.Channel]))
            Analyze(trace, reference);

        Invalidate();
    }

    private void Analyze(Trace trace, Trace? reference)
    {
        int maxBin = Math.Min(n / 2 - 1, (int)(MaxHz / BinHz));
        int minBin = Math.Max(1, (int)Math.Ceiling(MinHz / BinHz));
        int fundamental = minBin;
        for (int i = minBin + 1; i <= maxBin; i++)
            if (trace.Vrms[i] > trace.Vrms[fundamental]) fundamental = i;

        var m = trace.Measurement;
        m.FundamentalHz = (fundamental + ParabolicPeak(trace.Vrms, fundamental)) * BinHz;
        m.FundamentalVrms = BinAmplitude(trace.Vrms, fundamental);

        for (int h = 2; h <= 5; h++)
        {
            int bin = (int)Math.Round(h * m.FundamentalHz / BinHz);
            m.HarmonicsVrms[h - 2] = bin <= maxBin ? BinAmplitude(trace.Vrms, bin) : 0;
        }

        double harmonicPower = m.HarmonicsVrms.Sum(v => v * v);
        double noisePower = 0;
        for (int bin = 1; bin <= maxBin; bin++)
        {
            bool excluded = Math.Abs(bin - fundamental) <= 1;
            for (int h = 2; h <= 5; h++)
                excluded |= Math.Abs(bin - (int)Math.Round(h * m.FundamentalHz / BinHz)) <= 1;
            if (!excluded)
            {
                double v = 2 * trace.Fft[bin].Magnitude / (n * rmsGain * Math.Sqrt(2));
                noisePower += v * v;
            }
        }

        m.ThdPercent = m.FundamentalVrms > 0 ? 100 * Math.Sqrt(harmonicPower) / m.FundamentalVrms : 0;
        m.ThdnPercent = m.FundamentalVrms > 0 ? 100 * Math.Sqrt(harmonicPower + noisePower) / m.FundamentalVrms : 0;
        m.NoiseFloorDbvPerRootHz = 20 * Math.Log10(Math.Max(1e-15, Math.Sqrt(noisePower / Math.Max(BinHz, (maxBin - minBin) * BinHz))));
        m.PhaseDeg = reference is null ? 0 : Wrap(trace.Phase[fundamental] - reference.Phase[fundamental]);
    }

    private static int HighestPowerOfTwo(int value) { int p = 1; while (p <= value / 2) p <<= 1; return p; }
    private double BinHz => sr / n;
    private double MinHz => Math.Max(BinHz, Math.Min(MinDisplayHz, MaxDisplayHz * .99));
    private double MaxHz => Math.Min(sr / 2, Math.Max(MinHz * 1.01, MaxDisplayHz));

    private static double Wrap(double value)
    {
        while (value > 180) value -= 360;
        while (value < -180) value += 360;
        return value;
    }

    private static double ParabolicPeak(double[] a, int i)
    {
        if (i <= 0 || i >= a.Length - 1) return 0;
        double d = a[i - 1] - 2 * a[i] + a[i + 1];
        return Math.Abs(d) < 1e-20 ? 0 : Math.Clamp(.5 * (a[i - 1] - a[i + 1]) / d, -.5, .5);
    }

    private static double BinAmplitude(double[] a, int i) =>
        i < 1 || i >= a.Length - 1 ? 0 : Math.Sqrt(a[i - 1] * a[i - 1] + a[i] * a[i] + a[i + 1] * a[i + 1]);

    private void BuildWindow(int length)
    {
        double sum = 0, sum2 = 0;
        for (int i = 0; i < length; i++) { double w = WindowValue(i, length); sum += w; sum2 += w * w; }
        coherentGain = sum / length;
        rmsGain = Math.Sqrt(sum2 / length);
    }

    private double WindowValue(int i, int length) => Window switch
    {
        SpectrumWindow.Hamming => .54 - .46 * Math.Cos(2 * Math.PI * i / (length - 1)),
        SpectrumWindow.Blackman => .42 - .5 * Math.Cos(2 * Math.PI * i / (length - 1)) + .08 * Math.Cos(4 * Math.PI * i / (length - 1)),
        SpectrumWindow.Rectangular => 1,
        _ => .5 * (1 - Math.Cos(2 * Math.PI * i / (length - 1)))
    };

    protected override void OnMouseMove(MouseEventArgs e)
    {
        cursorHz = amplitudePlot.Contains(e.Location) ? XToHz(e.X, amplitudePlot) : -1;
        Invalidate();
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(EventArgs e) { cursorHz = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width < 160 || ClientSize.Height < 140) return;
        int left = 70, right = 18, top = 18, bottom = 25, gap = 30;
        int usable = ClientSize.Height - top - bottom - gap;
        var amp = new Rectangle(left, top, ClientSize.Width - left - right, (int)(usable * .62));
        var phase = new Rectangle(left, amp.Bottom + gap, amp.Width, usable - amp.Height);
        amplitudePlot = amp;
        DrawAmplitude(e.Graphics, amp);
        DrawPhase(e.Graphics, phase);
    }

    private void DrawAmplitude(Graphics g, Rectangle plot)
    {
        using var grid = new Pen(Color.FromArgb(225, 230, 236));
        using var axis = new Pen(Color.Gray);
        using var font = new Font("Consolas", 8);
        using var text = new SolidBrush(Color.DimGray);
        DrawGrid(g, plot, grid, axis, font, text,
            UseDb ? -140 : 0, UseDb ? 20 : MaxAmplitude(),
            UseDb ? "dBV" : "Vrms", "幅值频谱");

        if (n == 0)
        {
            g.DrawString("点击“刷新频谱”开始分析", font, text, plot.Left + 15, plot.Top + 15);
            return;
        }

        foreach (var trace in traces.Where(t => enabled[t.Channel]))
            DrawTrace(g, plot, trace);

        var selected = traces[Channel];
        if (enabled[Channel] && selected.Measurement.FundamentalHz > 0)
        {
            Marker(g, plot, selected.Measurement.FundamentalHz, "F", Color.Firebrick, font);
            for (int h = 2; h <= 5; h++)
                if (h * selected.Measurement.FundamentalHz <= MaxHz)
                    Marker(g, plot, h * selected.Measurement.FundamentalHz, $"H{h}", Color.SaddleBrown, font);
        }
        Cursor(g, plot, font, text);
    }

    private void DrawPhase(Graphics g, Rectangle plot)
    {
        using var grid = new Pen(Color.FromArgb(225, 230, 236));
        using var axis = new Pen(Color.Gray);
        using var font = new Font("Consolas", 8);
        using var text = new SolidBrush(Color.DimGray);
        DrawGrid(g, plot, grid, axis, font, text, -180, 180, "°",
            $"相位差（参考 CH{referenceChannel + 1}）");
        if (n == 0) return;

        Trace? reference = enabled[referenceChannel] ? traces[referenceChannel] : null;
        foreach (var trace in traces.Where(t => enabled[t.Channel]))
        {
            using var pen = new Pen(colors[trace.Channel], 1);
            PointF? previous = null;
            for (int bin = Math.Max(1, (int)Math.Ceiling(MinHz / BinHz));
                 bin <= Math.Min(n / 2 - 1, (int)(MaxHz / BinHz)); bin++)
            {
                double v = reference is null ? 0 : Wrap(trace.Phase[bin] - reference.Phase[bin]);
                var point = new PointF(HzToX(bin * BinHz, plot),
                    plot.Bottom - (float)((v + 180) / 360 * plot.Height));
                if (previous.HasValue && Math.Abs(point.Y - previous.Value.Y) < plot.Height * .7f)
                    g.DrawLine(pen, previous.Value, point);
                previous = point;
            }
        }
        Cursor(g, plot, font, text);
    }

    private void DrawTrace(Graphics g, Rectangle plot, Trace trace)
    {
        using var pen = new Pen(colors[trace.Channel], trace.Channel == Channel ? 1.6f : 1f);
        PointF? previous = null;
        int min = Math.Max(1, (int)Math.Ceiling(MinHz / BinHz));
        int max = Math.Min(n / 2 - 1, (int)(MaxHz / BinHz));
        double top = UseDb ? 20 : MaxAmplitude();
        double bottom = UseDb ? -140 : 0;

        for (int bin = min; bin <= max; bin++)
        {
            double v = UseDb ? 20 * Math.Log10(Math.Max(1e-12, trace.Vrms[bin])) : trace.Vrms[bin];
            var point = new PointF(HzToX(bin * BinHz, plot),
                Math.Clamp(plot.Bottom - (float)((v - bottom) / (top - bottom) * plot.Height),
                           plot.Top, plot.Bottom));
            if (previous.HasValue) g.DrawLine(pen, previous.Value, point);
            previous = point;
        }
    }

    private void DrawGrid(Graphics g, Rectangle p, Pen grid, Pen axis, Font font, Brush text,
                          double minY, double maxY, string unit, string title)
    {
        g.DrawRectangle(axis, p);
        g.DrawString(title, font, text, p.Left, p.Top - 15);
        for (int i = 0; i <= 4; i++)
        {
            float y = p.Bottom - p.Height * i / 4f;
            g.DrawLine(grid, p.Left, y, p.Right, y);
            g.DrawString($"{minY + (maxY - minY) * i / 4:0.#}{unit}", font, text, 2, y - 7);
        }
        foreach (double f in Ticks())
        {
            float x = HzToX(f, p);
            g.DrawLine(grid, x, p.Top, x, p.Bottom);
            g.DrawString(Freq(f), font, text, x - 12, p.Bottom + 4);
        }
    }

    private IEnumerable<double> Ticks()
    {
        if (!LogFrequency)
        {
            for (int i = 0; i <= 5; i++)
                yield return MinHz + (MaxHz - MinHz) * i / 5;
            yield break;
        }
        for (int p = (int)Math.Ceiling(Math.Log10(MinHz));
             p <= (int)Math.Floor(Math.Log10(MaxHz)); p++)
            yield return Math.Pow(10, p);
    }

    private float HzToX(double hz, Rectangle p)
    {
        double t = LogFrequency
            ? Math.Log(hz / MinHz) / Math.Log(MaxHz / MinHz)
            : (hz - MinHz) / (MaxHz - MinHz);
        return p.Left + (float)(Math.Clamp(t, 0, 1) * p.Width);
    }

    private double XToHz(int x, Rectangle p)
    {
        double t = (x - p.Left) / (double)p.Width;
        return LogFrequency ? MinHz * Math.Pow(MaxHz / MinHz, t) : MinHz + (MaxHz - MinHz) * t;
    }

    private void Marker(Graphics g, Rectangle p, double hz, string name, Color c, Font f)
    {
        float x = HzToX(hz, p);
        using var pen = new Pen(c, 1) { DashStyle = DashStyle.Dash };
        using var brush = new SolidBrush(c);
        g.DrawLine(pen, x, p.Top, x, p.Bottom);
        g.DrawString(name, f, brush, x + 2, p.Top + 2);
    }

    private void Cursor(Graphics g, Rectangle p, Font f, Brush text)
    {
        if (cursorHz < MinHz || cursorHz > MaxHz) return;
        float x = HzToX(cursorHz, p);
        using var pen = new Pen(Color.FromArgb(120, Color.Black));
        g.DrawLine(pen, x, p.Top, x, p.Bottom);
        g.DrawString(Freq(cursorHz), f, text, x + 3, p.Top + 3);
    }

    private double MaxAmplitude() =>
        Math.Max(.001, traces.Where(t => enabled[t.Channel]).SelectMany(t => t.Vrms).DefaultIfEmpty(.001).Max() * 1.15);

    private static string Freq(double f) => f >= 1000 ? $"{f / 1000:0.##}k" : $"{f:0.#}";
}