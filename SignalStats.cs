using System.Numerics;

namespace Ad7606Host;

internal struct ChannelStats
{
    public double Dc;          // 中心电压
    public double Vpp;         // 峰峰值
    public double Rms;         // 总 RMS
    public double AcRms;       // 去 DC 后 RMS
    public double Frequency;   // 主频
    public double PeakAmplitude;
    public bool Valid;
}

/// <summary>所有通道滑动窗口统计（采集线程 Push，UI 线程 GetStats）</summary>
internal sealed class SignalStats
{
    public const int Channels = 8;
    public const int WindowSize = 16384;
    private const int FftSize = 8192;    // 内部频率分析用，够 160kHz 下覆盖 51ms

    private readonly object _lock = new();
    private readonly double[] ring = new double[WindowSize * Channels];
    private int writeIdx;
    private long totalWrites;
    private double sampleRate = 200_000;
    private readonly ChannelStats[] results = new ChannelStats[Channels];
    private long lastAnalyzeMs;

    private readonly Complex[] fftWork = new Complex[FftSize];
    private readonly double[] work = new double[FftSize];

    public void SetSampleRate(double hz)
    {
        if (hz <= 0) return;
        lock (_lock) sampleRate = hz;
    }
    public int WindowPoints => WindowSize;

    public void Push(ReadOnlySpan<double> frame)
    {
        lock (_lock)
        {
            int baseIdx = writeIdx * Channels;
            for (int i = 0; i < Channels && i < frame.Length; i++)
                ring[baseIdx + i] = frame[i];
            writeIdx = (writeIdx + 1) % WindowSize;
            totalWrites++;

            long now = Environment.TickCount64;
            if (now - lastAnalyzeMs < 300) return;
            lastAnalyzeMs = now;
            if (totalWrites >= FftSize) AnalyzeAll();
        }
    }

    public ChannelStats GetStats(int ch) { lock (_lock) return results[ch]; }

    public int CopyRecent(int ch, Span<double> dst)
    {
        lock (_lock)
        {
            int count = (int)Math.Min(totalWrites, WindowSize);
            int n = Math.Min(dst.Length, count);
            int start = (writeIdx + WindowSize - n) % WindowSize;
            for (int k = 0; k < n; k++)
                dst[k] = ring[((start + k) % WindowSize) * Channels + ch];
            for (int k = n; k < dst.Length; k++) dst[k] = 0;
            return n;
        }
    }

    private void AnalyzeAll()
    {
        double sr = sampleRate;
        double binHz = sr / FftSize;

        for (int ch = 0; ch < Channels; ch++)
        {
            int start = (writeIdx + WindowSize - FftSize) % WindowSize;
            double sum = 0, sum2 = 0, mn = double.MaxValue, mx = double.MinValue;
            for (int k = 0; k < FftSize; k++)
            {
                double v = ring[((start + k) % WindowSize) * Channels + ch];
                work[k] = v;
                sum += v; sum2 += v * v;
                if (v < mn) mn = v;
                if (v > mx) mx = v;
            }
            double dc = sum / FftSize;
            double rms = Math.Sqrt(sum2 / FftSize);

            if (rms < 1e-6 || (mx - mn) < 1e-4)
            {
                results[ch] = new ChannelStats
                {
                    Dc = dc,
                    Vpp = mx - mn,
                    Rms = rms,
                    AcRms = 0,
                    Frequency = 0,
                    PeakAmplitude = 0,
                    Valid = true
                };
                continue;
            }

            // 去 DC + Hann
            for (int k = 0; k < FftSize; k++)
            {
                double w = 0.5 * (1 - Math.Cos(2 * Math.PI * k / (FftSize - 1)));
                fftWork[k] = new Complex((work[k] - dc) * w, 0);
            }
            FftEngine.Forward(fftWork);

            double magScale = 2.0 / FftSize;
            int peakBin = 1;
            double peakMag = 0;
            for (int k = 1; k < FftSize / 2; k++)
            {
                double mag = fftWork[k].Magnitude * magScale;
                if (mag > peakMag) { peakMag = mag; peakBin = k; }
            }

            double freq = peakBin * binHz;
            if (peakBin > 1 && peakBin < FftSize / 2 - 1)
            {
                double m0 = fftWork[peakBin - 1].Magnitude * magScale;
                double m1 = peakMag;
                double m2 = fftWork[peakBin + 1].Magnitude * magScale;
                double denom = m0 - 2 * m1 + m2;
                if (Math.Abs(denom) > 1e-12)
                {
                    double delta = 0.5 * (m0 - m2) / denom;
                    freq = (peakBin + delta) * binHz;
                }
            }

            results[ch] = new ChannelStats
            {
                Dc = dc,
                Vpp = mx - mn,
                Rms = rms,
                AcRms = Math.Sqrt(Math.Max(0, rms * rms - dc * dc)),
                Frequency = freq,
                PeakAmplitude = peakMag,
                Valid = true
            };
        }
    }
}
