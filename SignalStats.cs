using System.Numerics;

namespace Ad7606Host;

internal struct ChannelStats
{
    public double Dc;
    public double Vpp;
    public double Rms;
    public double AcRms;
    public double Frequency;
    public double PeakAmplitude;
    public bool Valid;
}

internal struct ChannelStatsCumulative
{
    public double MinDc, MaxDc, SumDc;
    public double MinVpp, MaxVpp, SumVpp;
    public long Count;

    public bool Valid => Count > 0;
    public double AvgDc => Count > 0 ? SumDc / Count : 0;
    public double AvgVpp => Count > 0 ? SumVpp / Count : 0;

    public static ChannelStatsCumulative Empty => new ChannelStatsCumulative
    {
        MinDc = double.PositiveInfinity,
        MaxDc = double.NegativeInfinity,
        MinVpp = double.PositiveInfinity,
        MaxVpp = double.NegativeInfinity,
        Count = 0,
    };
}

internal sealed class SignalStats
{
    public const int Channels = 8;
    public const int WindowSize = 16384;
    private const int FftSize = 8192;

    private readonly object _lock = new();
    private readonly double[] ring = new double[WindowSize * Channels];
    private int writeIdx;
    private long totalWrites;
    private long sampleRateBits = BitConverter.DoubleToInt64Bits(200_000);
    private readonly ChannelStats[] results = new ChannelStats[Channels];
    private readonly ChannelStatsCumulative[] cumulative = new ChannelStatsCumulative[Channels];
    private long lastAnalyzeMs;
    private int analysisQueued;

    private readonly Complex[] fftWork = new Complex[FftSize];
    private readonly double[] copyBuf = new double[FftSize];

    public SignalStats()
    {
        for (int ch = 0; ch < Channels; ch++)
            cumulative[ch] = ChannelStatsCumulative.Empty;
    }

    public void SetSampleRate(double hz)
    {
        if (hz > 0) Interlocked.Exchange(ref sampleRateBits, BitConverter.DoubleToInt64Bits(hz));
    }

    private double SampleRate => BitConverter.Int64BitsToDouble(Interlocked.Read(ref sampleRateBits));

    public int WindowPoints => WindowSize;

    public void Push(ReadOnlySpan<double> frame)
    {
        bool needAnalyze = false;
        lock (_lock)
        {
            int baseIdx = writeIdx * Channels;
            for (int i = 0; i < Channels && i < frame.Length; i++)
                ring[baseIdx + i] = frame[i];
            writeIdx = (writeIdx + 1) % WindowSize;
            totalWrites++;

            long now = Environment.TickCount64;
            if (now - lastAnalyzeMs >= 300 && totalWrites >= FftSize)
            {
                lastAnalyzeMs = now;
                needAnalyze = true;
            }
        }
        // FFT across all channels is comparatively expensive. Never hold up the USB reader for it;
        // keep one analysis worker at a time and let it use the latest ring-buffer snapshot.
        if (needAnalyze && Interlocked.Exchange(ref analysisQueued, 1) == 0)
        {
            _ = Task.Run(() =>
            {
                try { AnalyzeAll(); }
                finally { Volatile.Write(ref analysisQueued, 0); }
            });
        }
    }

    private void AnalyzeAll()
    {
        double sr = SampleRate;
        double binHz = sr / FftSize;
        double[] local = copyBuf;

        for (int ch = 0; ch < Channels; ch++)
        {
            int start;
            lock (_lock)
            {
                start = (writeIdx + WindowSize - FftSize) % WindowSize;
                for (int k = 0; k < FftSize; k++)
                    local[k] = ring[((start + k) % WindowSize) * Channels + ch];
            }

            double sum = 0, sum2 = 0, mn = double.MaxValue, mx = double.MinValue;
            for (int k = 0; k < FftSize; k++)
            {
                double v = local[k];
                sum += v; sum2 += v * v;
                if (v < mn) mn = v;
                if (v > mx) mx = v;
            }
            double dc = sum / FftSize;
            double rms = Math.Sqrt(sum2 / FftSize);
            double vpp = mx - mn;

            if (rms < 1e-6 || vpp < 1e-4)
            {
                lock (_lock) results[ch] = new ChannelStats
                {
                    Dc = dc,
                    Vpp = vpp,
                    Rms = rms,
                    AcRms = 0,
                    Frequency = 0,
                    PeakAmplitude = 0,
                    Valid = true
                };
                UpdateCumulative(ch, dc, vpp);
                continue;
            }

            for (int k = 0; k < FftSize; k++)
            {
                double w = 0.5 * (1 - Math.Cos(2 * Math.PI * k / (FftSize - 1)));
                fftWork[k] = new Complex((local[k] - dc) * w, 0);
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
                    freq = (peakBin + 0.5 * (m0 - m2) / denom) * binHz;
            }

            lock (_lock) results[ch] = new ChannelStats
            {
                Dc = dc,
                Vpp = vpp,
                Rms = rms,
                AcRms = Math.Sqrt(Math.Max(0, rms * rms - dc * dc)),
                Frequency = freq,
                PeakAmplitude = peakMag,
                Valid = true
            };

            UpdateCumulative(ch, dc, vpp);
        }
    }

    private void UpdateCumulative(int ch, double dc, double vpp)
    {
        lock (_lock)
        {
            cumulative[ch].MinDc = Math.Min(cumulative[ch].MinDc, dc);
            cumulative[ch].MaxDc = Math.Max(cumulative[ch].MaxDc, dc);
            cumulative[ch].SumDc += dc;
            cumulative[ch].MinVpp = Math.Min(cumulative[ch].MinVpp, vpp);
            cumulative[ch].MaxVpp = Math.Max(cumulative[ch].MaxVpp, vpp);
            cumulative[ch].SumVpp += vpp;
            cumulative[ch].Count++;
        }
    }

    public ChannelStats GetStats(int ch) { lock (_lock) return results[ch]; }

    public ChannelStatsCumulative GetCumulativeStats(int ch)
    {
        lock (_lock) return cumulative[ch];
    }

    public void ResetCumulative()
    {
        lock (_lock)
        {
            for (int ch = 0; ch < Channels; ch++)
                cumulative[ch] = ChannelStatsCumulative.Empty;
        }
    }

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
}
