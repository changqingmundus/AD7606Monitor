namespace Ad7606Host;

internal sealed class EncoderConfig
{
    public bool Enabled;
    public int ChAp = -1, ChAn = -1, ChBp = -1, ChBn = -1, ChZp = -1, ChZn = -1;
    public double Lines = 1250;
    public bool InvertZ;
    public double OffsetA, OffsetB, OffsetZ;
    public bool AutoCenter;
}

internal sealed class EncoderSnapshot
{
    public bool Valid;
    public bool Synced;
    public double AngleDeg;
    public double ContinuousTurns;
    public double SpeedRpm;
    public int Direction;
    public double A, B, Z;
    public double RawA, RawB, RawZ;
    public double PeakA, PeakB;
    public int ZPulseCount;
    public double DtPerFrameMs;
}

internal sealed class EncoderState
{
    private readonly object _lock = new();

    private bool _enabled, _autoCenter, _invertZ;
    private int _chAp, _chAn, _chBp, _chBn, _chZp, _chZn;
    private double _lines = 1250, _offsetA, _offsetB, _offsetZ;
    private double _dtPerFrame = 5e-6;

    private double _offsetEstA, _offsetEstB;
    private double _lastTheta, _continuousAngle;
    private bool _init, _synced;
    private int _lastZEffective;
    private double _speedRps, _lastSpeedAngle, _lastSpeedTime, _timeAccum;
    private int _zPulses;
    private double _peakA, _peakB;
    private EncoderSnapshot _snapshot = new();

    public void Configure(EncoderConfig cfg, double sampleRate)
    {
        lock (_lock)
        {
            _enabled = cfg.Enabled && cfg.ChAp >= 0 && cfg.ChBp >= 0;
            _chAp = cfg.ChAp; _chAn = cfg.ChAn; _chBp = cfg.ChBp; _chBn = cfg.ChBn;
            _chZp = cfg.ChZp; _chZn = cfg.ChZn;
            _lines = cfg.Lines > 0 ? cfg.Lines : 1250;
            _invertZ = cfg.InvertZ; _autoCenter = cfg.AutoCenter;
            _offsetA = cfg.OffsetA; _offsetB = cfg.OffsetB; _offsetZ = cfg.OffsetZ;
            _dtPerFrame = 1.0 / Math.Max(1, sampleRate);
            ResetLocked();
        }
    }

    /// <summary>用实测帧率更新时间基。由后台流循环每 0.4 秒调用一次。</summary>
    public void UpdateFrameInterval(double dtPerFrame)
    {
        if (dtPerFrame <= 0 || double.IsNaN(dtPerFrame) || double.IsInfinity(dtPerFrame)) return;
        if (dtPerFrame < 5e-7 || dtPerFrame > 0.1) return;   // 2 MHz ~ 10 Hz 之外视为异常
        lock (_lock) { _dtPerFrame = dtPerFrame; }
    }

    public void ZeroAngle()
    {
        lock (_lock)
        {
            _continuousAngle = 0;
            _lastSpeedAngle = 0;
            _lastSpeedTime = _timeAccum;
            _synced = false;
        }
    }

    public void Reset() { lock (_lock) ResetLocked(); }

    private void ResetLocked()
    {
        _offsetEstA = _offsetEstB = 0;
        _lastTheta = 0; _continuousAngle = 0;
        _init = false; _synced = false;
        _lastZEffective = 0;
        _speedRps = 0; _lastSpeedAngle = 0; _lastSpeedTime = 0; _timeAccum = 0;
        _zPulses = 0; _peakA = _peakB = 0;
        _snapshot = new EncoderSnapshot();
    }

    public void ProcessFrame(double[] volts)
    {
        if (!_enabled) return;

        lock (_lock)
        {
            double rawA = Diff(volts, _chAp, _chAn);
            double rawB = Diff(volts, _chBp, _chBn);
            double rawZ = Diff(volts, _chZp, _chZn);

            double a = rawA - _offsetA;
            double b = rawB - _offsetB;
            double z = rawZ - _offsetZ;

            if (_autoCenter && _init)
            {
                const double alpha = 0.00002;
                _offsetEstA += alpha * ((rawA - _offsetA) - _offsetEstA);
                _offsetEstB += alpha * ((rawB - _offsetB) - _offsetEstB);
                a = rawA - _offsetA - _offsetEstA;
                b = rawB - _offsetB - _offsetEstB;
            }

            if (Math.Abs(a) > _peakA) _peakA = Math.Abs(a);
            if (Math.Abs(b) > _peakB) _peakB = Math.Abs(b);

            _timeAccum += _dtPerFrame;

            const double minAmp = 0.01;
            if (Math.Abs(a) < minAmp && Math.Abs(b) < minAmp)
            {
                _speedRps *= 0.95;
                EmitSnapshot(a, b, z, rawA, rawB, rawZ);
                return;
            }

            double theta = Math.Atan2(b, a);

            if (!_init)
            {
                _lastTheta = theta;
                _lastSpeedTime = _timeAccum;
                _lastSpeedAngle = 0;
                _init = true;
                EmitSnapshot(a, b, z, rawA, rawB, rawZ);
                return;
            }

            double delta = theta - _lastTheta;
            if (delta > Math.PI) delta -= 2 * Math.PI;
            if (delta < -Math.PI) delta += 2 * Math.PI;
            if (Math.Abs(delta) < 0.0005) delta = 0;

            _continuousAngle += delta / _lines;
            _lastTheta = theta;

            // Z 上升沿：对齐整圈，同时同步速度基准，避免尖峰
            if (_chZp >= 0 || _chZn >= 0)
            {
                const double hyst = 0.2;
                int rawSign = z > hyst ? 1 : (z < -hyst ? -1 : 0);
                int effective = _invertZ ? -rawSign : rawSign;
                if (effective > 0 && _lastZEffective <= 0)
                {
                    double aligned = Math.Round(_continuousAngle / (2 * Math.PI)) * 2 * Math.PI;
                    _lastSpeedAngle += aligned - _continuousAngle;   // 同步修正基准
                    _continuousAngle = aligned;
                    _synced = true;
                    _zPulses++;
                }
                if (rawSign != 0) _lastZEffective = effective;
            }

            // 速度：50ms 窗口 + 0.8/0.2 低通
            double elapsed = _timeAccum - _lastSpeedTime;
            if (elapsed >= 0.05)
            {
                double dAngle = _continuousAngle - _lastSpeedAngle;
                double instRps = dAngle / elapsed / (2 * Math.PI);
                if (Math.Abs(instRps) > 200) instRps = _speedRps;
                _speedRps = _speedRps * 0.8 + instRps * 0.2;
                _lastSpeedTime = _timeAccum;
                _lastSpeedAngle = _continuousAngle;
            }

            EmitSnapshot(a, b, z, rawA, rawB, rawZ);
        }
    }

    private void EmitSnapshot(double a, double b, double z, double rawA, double rawB, double rawZ)
    {
        double angleDeg = (_continuousAngle * 180 / Math.PI) % 360;
        if (angleDeg < 0) angleDeg += 360;
        int dir = _speedRps > 0.02 ? 1 : (_speedRps < -0.02 ? -1 : 0);
        _snapshot = new EncoderSnapshot
        {
            Valid = true,
            Synced = _synced,
            AngleDeg = angleDeg,
            ContinuousTurns = _continuousAngle / (2 * Math.PI),
            SpeedRpm = _speedRps * 60,
            Direction = dir,
            A = a,
            B = b,
            Z = z,
            RawA = rawA,
            RawB = rawB,
            RawZ = rawZ,
            PeakA = _peakA,
            PeakB = _peakB,
            ZPulseCount = _zPulses,
            DtPerFrameMs = _dtPerFrame * 1000.0,
        };
    }

    public EncoderSnapshot GetSnapshot() { lock (_lock) return _snapshot; }

    private static double Diff(double[] v, int p, int n)
    {
        if (p >= 0 && n >= 0) return v[p] - v[n];
        if (p >= 0) return v[p];
        if (n >= 0) return -v[n];
        return 0;
    }
}