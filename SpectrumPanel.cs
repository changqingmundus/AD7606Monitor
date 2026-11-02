using System.Numerics;

namespace Ad7606Host;

internal sealed class SpectrumPanel : Panel
{
    private double[] mags = Array.Empty<double>();
    private int n;
    private double sr = 200000;
    private double peakFreq, peakMag, dcValue, rmsValue, acRmsValue;
    private Complex[] fftWork = Array.Empty<Complex>();
    private double[] window = Array.Empty<double>();

    public SpectrumPanel() { DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.White; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Channel { get; set; }

    public void ComputeAndSet(double[] samples, int count, double sampleRate)
    {
        int len = count;
        if (len < 16) return;
        // 取小于等于 len 的 2 的幂
        int pow = 1;
        while (pow * 2 <= len) pow *= 2;
        len = pow;
        if (len < 16) return;

        if (n != len || fftWork.Length != len)
        {
            n = len;
            fftWork = new Complex[n];
            window = new double[n];
            mags = new double[n / 2];
        }
        sr = sampleRate;

        int off = count - n;
        if (off < 0) off = 0;

        double dc = 0, sum2 = 0;
        for (int i = 0; i < n; i++) { dc += samples[off + i]; }
        dc /= n;
        for (int i = 0; i < n; i++) { double v = samples[off + i]; sum2 += v * v; }
        rmsValue = Math.Sqrt(sum2 / n);
        dcValue = dc;
        acRmsValue = Math.Sqrt(Math.Max(0, rmsValue * rmsValue - dc * dc));

        for (int i = 0; i < n; i++)
        {
            double w = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1)));
            fftWork[i] = new Complex((samples[off + i] - dc) * w, 0);
        }
        FftEngine.Forward(fftWork);

        double magScale = 2.0 / n;
        int peakBin = 1;
        double maxMag = 0;
        for (int i = 0; i < n / 2; i++)
        {
            mags[i] = fftWork[i].Magnitude * magScale;
            if (i >= 1 && mags[i] > maxMag) { maxMag = mags[i]; peakBin = i; }
        }

        double binHz = sr / n;
        peakFreq = peakBin * binHz;
        peakMag = maxMag;
        if (peakBin > 1 && peakBin < n / 2 - 1)
        {
            double m0 = mags[peakBin - 1], m1 = mags[peakBin], m2 = mags[peakBin + 1];
            double denom = m0 - 2 * m1 + m2;
            if (Math.Abs(denom) > 1e-12)
                peakFreq = (peakBin + 0.5 * (m0 - m2) / denom) * binHz;
        }
        Invalidate();
    }

    public string Info => n == 0 ? "等待采集" :
        $"CH{Channel + 1} · N={n} · 采样率 {sr:F0} Hz · 主频 {peakFreq:F2} Hz · 峰值 {peakMag:F4} V · DC {dcValue:F4} V · AC RMS {acRmsValue:F4} V";

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = ClientRectangle;
        if (r.Width < 100 || r.Height < 60) return;
        const int left = 70, right = 18, top = 12, bottom = 34;
        var plot = new Rectangle(left, top, r.Width - left - right, r.Height - top - bottom);

        using var gridPen = new Pen(Color.FromArgb(230, 235, 240));
        using var axisPen = new Pen(Color.Gray);
        using var linePen = new Pen(Color.DodgerBlue, 1.2f);
        using var font = new Font("Consolas", 8.5f);
        using var dimBrush = new SolidBrush(Color.DimGray);

        g.DrawRectangle(axisPen, plot);
        for (int i = 0; i <= 4; i++)
        {
            float y = plot.Top + plot.Height * i / 4f;
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
        }

        if (mags.Length == 0)
        {
            g.DrawString("点击“刷新”获取频谱", font, dimBrush, plot.Left + 20, plot.Top + plot.Height / 2);
            return;
        }

        int maxBin = n / 2 - 1;
        double maxFreq = maxBin * sr / n;
        double maxMagAll = 0;
        for (int i = 1; i < maxBin; i++) if (mags[i] > maxMagAll) maxMagAll = mags[i];
        if (maxMagAll < 1e-9) maxMagAll = 1e-9;

        for (int i = 0; i <= 4; i++)
        {
            double freq = maxFreq * i / 4.0;
            float x = plot.Left + plot.Width * i / 4f;
            g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            string label = freq >= 1000 ? $"{freq / 1000:0.#}k" : $"{freq:0}";
            g.DrawString(label, font, dimBrush, x - 10, plot.Bottom + 5);
        }

        var pts = new List<PointF>(maxBin);
        for (int i = 1; i <= maxBin; i++)
        {
            float x = plot.Left + plot.Width * (float)i / maxBin;
            float y = plot.Bottom - (float)(mags[i] / maxMagAll * plot.Height);
            if (y < plot.Top) y = plot.Top;
            if (y > plot.Bottom) y = plot.Bottom;
            pts.Add(new PointF(x, y));
        }
        if (pts.Count >= 2) g.DrawLines(linePen, pts.ToArray());

        // 峰值标记
        if (peakFreq > 0 && peakFreq <= maxFreq)
        {
            float x = plot.Left + plot.Width * (float)(peakFreq / maxFreq);
            using var markPen = new Pen(Color.Firebrick, 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            g.DrawLine(markPen, x, plot.Top, x, plot.Bottom);
            g.DrawString($"峰值 {peakFreq:F2} Hz", font, Brushes.Firebrick, x + 4, plot.Top + 4);
        }
    }
}