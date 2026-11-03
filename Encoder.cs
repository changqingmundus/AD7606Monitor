namespace Ad7606Host;

internal sealed class EncoderConfig
{
    public bool Enabled;
    public bool SingleEnded;
    public int ChAp = -1, ChAn = -1, ChBp = -1, ChBn = -1, ChZp = -1, ChZn = -1;
    public int ChCp = -1, ChCn = -1, ChDp = -1, ChDn = -1;
    public double Lines = 1250;
    public bool InvertZ;
    public double OffsetA = 2.5, OffsetB = 2.5, OffsetZ = 2.5;
    public double OffsetC = 2.5, OffsetD = 2.5;
    public bool AutoCenter;
}

internal sealed class EncoderSnapshot
{
    public bool Valid;
    public bool Synced;
    public bool SingleEnded;
    public double AngleDeg;
    public double ContinuousTurns;
    public double SpeedRpm;
    public int Direction;
    public double A, B, Z, C, D;
    public double RawA, RawB, RawZ;
    public double PeakA, PeakB;
    public int ZPulseCount;
    public double DtPerFrameMs;

    public bool CdValid;
    public double CdAngleDeg;
    public double AbzAngleDeg;
    public double AngleErrorDeg;

    // 误差统计（最小/最大/平均绝对/样本数）
    public double MinAngleErrorDeg;
    public double MaxAngleErrorDeg;
    public double MeanAbsAngleErrorDeg;
    public long AngleErrorSamples;

    // 供 UI “取当前均值” 使用
    public double MeanA, MeanB, MeanC, MeanD;
}

internal sealed class EncoderState
{
    private readonly object _lock = new();

    private bool _enabled, _autoCenter, _invertZ, _singleEnded;
    private int _chAp, _chAn, _chBp, _chBn, _chZp, _chZn;
    private int _chCp = -1, _chCn = -1, _chDp = -1, _chDn = -1;
    private double _lines = 1250;
    private double _offsetA = 2.5, _offsetB = 2.5, _offsetZ = 2.5, _offsetC = 2.5, _offsetD = 2.5;
    private double _dtPerFrame = 5e-6;

    private double _offsetEstA, _offsetEstB;
    private double _lastTheta, _continuousAngle;
    private bool _init, _synced;
    private int _lastZEffective;
    private double _speedRps, _lastSpeedAngle, _lastSpeedTime, _timeAccum;
    private int _zPulses;
    private double _peakA, _peakB;

    private bool _cdValid;
    private double _cdAngleDeg;

    // 误差统计
    private double _minErrDeg = double.PositiveInfinity;
    private double _maxErrDeg = double.NegativeInfinity;
    private double _sumAbsErrDeg;
    private long _errCount;

    private double _meanA, _meanB, _meanC, _meanD;
    private bool _meanInit;

    private EncoderSnapshot _snapshot = new();

    public void Configure(EncoderConfig cfg, double sampleRate)
    {
        lock (_lock)
        {
            _enabled = cfg.Enabled && cfg.ChAp >= 0 && cfg.ChBp >= 0;
            _singleEnded = cfg.SingleEnded;
            _chAp = cfg.ChAp; _chAn = cfg.ChAn; _chBp = cfg.ChBp; _chBn = cfg.ChBn;
            _chZp = cfg.ChZp; _chZn = cfg.ChZn;
            _chCp = cfg.ChCp; _chCn = cfg.ChCn; _chDp = cfg.ChDp; _chDn = cfg.ChDn;
            _lines = cfg.Lines > 0 ? cfg.Lines : 1250;
            _invertZ = cfg.InvertZ; _autoCenter = cfg.AutoCenter;
            _offsetA = cfg.OffsetA; _offsetB = cfg.OffsetB; _offsetZ = cfg.OffsetZ;
            _offsetC = cfg.OffsetC; _offsetD = cfg.OffsetD;
            _cdValid = cfg.ChCp >= 0 && cfg.ChDp >= 0;
            _dtPerFrame = 1.0 / Math.Max(1, sampleRate);
            ResetLocked();
        }
    }

    public void ResetErrorStats()
    {
        lock (_lock)
        {
            _minErrDeg = double.PositiveInfinity;
            _maxErrDeg = double.NegativeInfinity;
            _sumAbsErrDeg = 0;
            _errCount = 0;
        }
    }

    public void SetOffsets(double a, double b, double z, double c, double d)
    {
        lock (_lock) { _offsetA = a; _offsetB = b; _offsetZ = z; _offsetC = c; _offsetD = d; }
    }

    public void UpdateFrameInterval(double dtPerFrame)
    {
        if (dtPerFrame <= 0 || double.IsNaN(dtPerFrame) || double.IsInfinity(dtPerFrame)) return;
        if (dtPerFrame < 5e-7 || dtPerFrame > 0.1) return;
        lock (_lock) { _dtPerFrame = dtPerFrame; }
    }

    public (double A, double B, double C, double D) GetAverageVoltage()
    {
        lock (_lock) { return (_meanA, _meanB, _meanC, _meanD); }
    }

    public void ResetMeans()
    {
        lock (_lock) { _meanA = _meanB = _meanC = _meanD = 0; _meanInit = false; }
    }

    public void ZeroAngle()
    {
        lock (_lock)
        {
            _continuousAngle = 0;
            _lastSpeedAngle = 0;
            _lastSpeedTime = _timeAccum;
            _synced = false;
            _minErrDeg = double.PositiveInfinity;
            _maxErrDeg = double.NegativeInfinity;
            _sumAbsErrDeg = 0;
            _errCount = 0;
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
        _cdAngleDeg = 0;
        _minErrDeg = double.PositiveInfinity;
        _maxErrDeg = double.NegativeInfinity;
        _sumAbsErrDeg = 0;
        _errCount = 0;
        _meanA = _meanB = _meanC = _meanD = 0; _meanInit = false;
        _snapshot = new EncoderSnapshot();
    }

    public void ProcessFrame(ReadOnlySpan<double> volts)
    {
        if (!_enabled) return;

        lock (_lock)
        {
            double rawA = ReadRaw(volts, _chAp, _chAn);
            double rawB = ReadRaw(volts, _chBp, _chBn);
            double rawZ = ReadRaw(volts, _chZp, _chZn);
            double rawC = _cdValid ? ReadRaw(volts, _chCp, _chCn) : 0;
            double rawD = _cdValid ? ReadRaw(volts, _chDp, _chDn) : 0;

            const double meanAlpha = 0.00002;
            if (!_meanInit)
            {
                _meanA = rawA; _meanB = rawB; _meanC = rawC; _meanD = rawD;
                _meanInit = true;
            }
            else
            {
                _meanA += meanAlpha * (rawA - _meanA);
                _meanB += meanAlpha * (rawB - _meanB);
                if (_cdValid)
                {
                    _meanC += meanAlpha * (rawC - _meanC);
                    _meanD += meanAlpha * (rawD - _meanD);
                }
            }

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

            if (_cdValid)
            {
                double c = rawC - _offsetC;
                double d = rawD - _offsetD;
                const double minCdAmp = 0.005;
                if (Math.Abs(c) > minCdAmp || Math.Abs(d) > minCdAmp)
                {
                    double cd = Math.Atan2(d, c) * 180.0 / Math.PI;
                    if (cd < 0) cd += 360;
                    _cdAngleDeg = cd;
                }
            }

            const double minAmp = 0.01;
            if (Math.Abs(a) < minAmp && Math.Abs(b) < minAmp)
            {
                _speedRps *= 0.95;
                EmitSnapshot(a, b, z, rawA, rawB, rawZ, rawC, rawD);
                return;
            }

            double theta = Math.Atan2(b, a);

            if (!_init)
            {
                _lastTheta = theta;
                _lastSpeedTime = _timeAccum;
                _lastSpeedAngle = 0;
                _init = true;
                EmitSnapshot(a, b, z, rawA, rawB, rawZ, rawC, rawD);
                return;
            }

            double delta = theta - _lastTheta;
            if (delta > Math.PI) delta -= 2 * Math.PI;
            if (delta < -Math.PI) delta += 2 * Math.PI;
            if (Math.Abs(delta) < 0.0005) delta = 0;

            _continuousAngle += delta / _lines;
            _lastTheta = theta;

            if (_chZp >= 0 || _chZn >= 0)
            {
                const double hyst = 0.2;
                int rawSign = z > hyst ? 1 : (z < -hyst ? -1 : 0);
                int effective = _invertZ ? -rawSign : rawSign;
                if (effective > 0 && _lastZEffective <= 0)
                {
                    double aligned = Math.Round(_continuousAngle / (2 * Math.PI)) * 2 * Math.PI;
                    _lastSpeedAngle += aligned - _continuousAngle;
                    _continuousAngle = aligned;
                    _synced = true;
                    _zPulses++;
                }
                if (rawSign != 0) _lastZEffective = effective;
            }

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

            EmitSnapshot(a, b, z, rawA, rawB, rawZ, rawC, rawD);
        }
    }

    private void EmitSnapshot(double a, double b, double z,
                              double rawA, double rawB, double rawZ,
                              double rawC, double rawD)
    {
        double angleDeg = (_continuousAngle * 180 / Math.PI) % 360;
        if (angleDeg < 0) angleDeg += 360;
        int dir = _speedRps > 0.02 ? 1 : (_speedRps < -0.02 ? -1 : 0);

        double c = _cdValid ? rawC - _offsetC : 0;
        double d = _cdValid ? rawD - _offsetD : 0;

        double err = 0;
        if (_cdValid && _synced)
        {
            err = angleDeg - _cdAngleDeg;
            while (err > 180) err -= 360;
            while (err < -180) err += 360;

            if (err < _minErrDeg) _minErrDeg = err;
            if (err > _maxErrDeg) _maxErrDeg = err;
            _sumAbsErrDeg += Math.Abs(err);
            _errCount++;
        }

        _snapshot = new EncoderSnapshot
        {
            Valid = true,
            Synced = _synced,
            SingleEnded = _singleEnded,
            AngleDeg = angleDeg,
            ContinuousTurns = _continuousAngle / (2 * Math.PI),
            SpeedRpm = _speedRps * 60,
            Direction = dir,
            A = a,
            B = b,
            Z = z,
            C = c,
            D = d,
            RawA = rawA,
            RawB = rawB,
            RawZ = rawZ,
            PeakA = _peakA,
            PeakB = _peakB,
            ZPulseCount = _zPulses,
            DtPerFrameMs = _dtPerFrame * 1000.0,

            CdValid = _cdValid,
            AbzAngleDeg = angleDeg,
            CdAngleDeg = _cdAngleDeg,
            AngleErrorDeg = err,
            MinAngleErrorDeg = _errCount > 0 ? _minErrDeg : 0,
            MaxAngleErrorDeg = _errCount > 0 ? _maxErrDeg : 0,
            MeanAbsAngleErrorDeg = _errCount > 0 ? _sumAbsErrDeg / _errCount : 0,
            AngleErrorSamples = _errCount,

            MeanA = _meanA,
            MeanB = _meanB,
            MeanC = _meanC,
            MeanD = _meanD,
        };
    }

    public EncoderSnapshot GetSnapshot() { lock (_lock) return _snapshot; }

    private double ReadRaw(ReadOnlySpan<double> v, int p, int n)
    {
        if (_singleEnded)
            return p >= 0 ? v[p] : (n >= 0 ? v[n] : 0);
        if (p >= 0 && n >= 0) return v[p] - v[n];
        if (p >= 0) return v[p];
        if (n >= 0) return -v[n];
        return 0;
    }
}
