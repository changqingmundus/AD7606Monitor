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

    // 偏置模式
    public bool AutoCenterFromVpp;
    public double VppTrackAlpha = 1e-5;                 // A/B 快追踪
    public double CdCoverageTargetDeg = 360.0;          // C/D 需要覆盖的机械角（默认一整圈）
    public double CdCenterMinSpeedRpm = 3.0;            // C/D 估计阶段需要的最低转速
    public double CdCenterTrackAlphaAfterReady = 0.0;   // 完成后追踪系数（0=冻结）

    // S120 动态漂移监控
    public double ErrorLimitDeg = 15.0;
    public double BaselineLearnSeconds = 0.5;
    public int PolePairs = 0;
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

    // S120 动态漂移监控
    public bool BaselineReady;
    public double BaselineErrorDeg;
    public double InstantErrorDeg;
    public double DriftDeg;
    public double MaxDriftDeg;
    public bool AlarmActive;
    public double LimitDeg;
    public int PolePairs;
    public double ElectricDriftDeg;
    public double ElectricLimitDeg;
    public long BaselineLearnProgress;
    public long BaselineLearnTarget;
    public double BaselineLearnSeconds;
    public bool BaselineBlockedByCdEstimate;   // 新增：C/D 估计未完成，基准学习暂停

    // 诊断
    public double MinAngleErrorDeg;
    public double MaxAngleErrorDeg;
    public double MeanAbsAngleErrorDeg;
    public long AngleErrorSamples;

    public double MeanA, MeanB, MeanC, MeanD;

    // 偏置模式诊断
    public bool AutoCenterFromVpp;
    public double UsedCenterA, UsedCenterB, UsedCenterC, UsedCenterD;
    public double EstVppA, EstVppB, EstVppC, EstVppD;

    // C/D 估计状态
    public bool CdCenterReady;
    public double CdCoverageDeg;
    public double CdCoverageTargetDeg;
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

    // 诊断
    private double _minErrDeg = double.PositiveInfinity;
    private double _maxErrDeg = double.NegativeInfinity;
    private double _sumAbsErrDeg;
    private long _errCount;

    // S120
    private double _errorLimitDeg = 15.0;
    private double _baselineLearnSeconds = 0.5;
    private int _polePairs = 0;
    private bool _baselineReady;
    private double _baselineErrorSum;
    private long _baselineErrorCount;
    private long _baselineLearnTarget;
    private double _baselineErrorDeg;
    private double _instantErrorDeg;
    private double _driftDeg;
    private double _maxDriftDeg;
    private bool _alarmActive;

    private double _meanA, _meanB, _meanC, _meanD;
    private bool _meanInit;

    // Vpp 自动中心
    private bool _autoCenterFromVpp;
    private double _vppTrackAlpha = 1e-5;
    private double _cdCoverageTargetDeg = 360.0;
    private double _cdCenterMinSpeedRps = 0.05;
    private double _cdCenterTrackAlphaAfterReady = 0.0;

    // A/B 快追踪状态
    private bool _abInit;
    private double _minA, _maxA, _minB, _maxB;
    private double _centerA = 2.5, _centerB = 2.5;
    private double _vppA, _vppB;

    // C/D 慢追踪状态
    private bool _cdInit;
    private bool _cdCenterReady;
    private double _minC, _maxC, _minD, _maxD;
    private double _centerC = 2.5, _centerD = 2.5;
    private double _vppC, _vppD;
    private double _cdCoverageDeg;

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

            _errorLimitDeg = cfg.ErrorLimitDeg > 0 ? cfg.ErrorLimitDeg : 15.0;
            _baselineLearnSeconds = cfg.BaselineLearnSeconds > 0 ? cfg.BaselineLearnSeconds : 0.5;
            _polePairs = cfg.PolePairs >= 0 ? cfg.PolePairs : 0;
            _baselineLearnTarget = Math.Max(100, (long)(_baselineLearnSeconds / _dtPerFrame));

            _autoCenterFromVpp = cfg.AutoCenterFromVpp;
            _vppTrackAlpha = cfg.VppTrackAlpha > 0 ? cfg.VppTrackAlpha : 1e-5;
            _cdCoverageTargetDeg = cfg.CdCoverageTargetDeg > 0 ? cfg.CdCoverageTargetDeg : 360.0;
            _cdCenterMinSpeedRps = (cfg.CdCenterMinSpeedRpm > 0 ? cfg.CdCenterMinSpeedRpm : 3.0) / 60.0;
            _cdCenterTrackAlphaAfterReady = cfg.CdCenterTrackAlphaAfterReady;

            ResetLocked();
        }
    }

    public void SetOffsets(double a, double b, double z, double c, double d)
    {
        lock (_lock) { _offsetA = a; _offsetB = b; _offsetZ = z; _offsetC = c; _offsetD = d; }
    }

    public void UpdateLimits(double limitDeg, int polePairs, double learnSeconds)
    {
        lock (_lock)
        {
            _errorLimitDeg = limitDeg > 0 ? limitDeg : 15.0;
            _polePairs = Math.Max(0, polePairs);
            if (learnSeconds > 0 && Math.Abs(learnSeconds - _baselineLearnSeconds) > 0.001)
            {
                _baselineLearnSeconds = learnSeconds;
                _baselineLearnTarget = Math.Max(100, (long)(_baselineLearnSeconds / _dtPerFrame));
            }
        }
    }

    public void UpdateFrameInterval(double dtPerFrame)
    {
        if (dtPerFrame <= 0 || double.IsNaN(dtPerFrame) || double.IsInfinity(dtPerFrame)) return;
        if (dtPerFrame < 5e-7 || dtPerFrame > 0.1) return;
        lock (_lock)
        {
            _dtPerFrame = dtPerFrame;
            _baselineLearnTarget = Math.Max(100, (long)(_baselineLearnSeconds / _dtPerFrame));
        }
    }

    public (double A, double B, double C, double D) GetAverageVoltage()
    {
        lock (_lock) { return (_meanA, _meanB, _meanC, _meanD); }
    }

    public void ResetMeans()
    {
        lock (_lock) { _meanA = _meanB = _meanC = _meanD = 0; _meanInit = false; }
    }

    public void ReloadBaseline()
    {
        lock (_lock)
        {
            _baselineReady = false;
            _baselineErrorSum = 0;
            _baselineErrorCount = 0;
            _baselineErrorDeg = 0;
            _driftDeg = 0;
            _maxDriftDeg = 0;
            _alarmActive = false;
        }
    }

    /// <summary>重置 Vpp 中心估计（A/B 和 C/D 都重置）</summary>
    public void ResetAutoCenter()
    {
        lock (_lock)
        {
            _abInit = false;
            _cdInit = false;
            _cdCenterReady = false;
            _cdCoverageDeg = 0;
            _minC = _maxC = _minD = _maxD = 0;
            _vppC = _vppD = 0;
            _centerC = _offsetC;
            _centerD = _offsetD;
        }
    }

    public void ResetAlarm()
    {
        lock (_lock) { _maxDriftDeg = 0; _alarmActive = false; }
    }

    public void ResetErrorStats()
    {
        lock (_lock)
        {
            _minErrDeg = double.PositiveInfinity;
            _maxErrDeg = double.NegativeInfinity;
            _sumAbsErrDeg = 0;
            _errCount = 0;
            _instantErrorDeg = 0;
        }
    }

    public void ZeroAngle()
    {
        lock (_lock)
        {
            _continuousAngle = 0;
            _lastSpeedAngle = 0;
            _lastSpeedTime = _timeAccum;
            _synced = false;
            _baselineReady = false;
            _baselineErrorSum = 0;
            _baselineErrorCount = 0;
            _baselineErrorDeg = 0;
            _driftDeg = 0;
            _maxDriftDeg = 0;
            _alarmActive = false;
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

        _baselineReady = false;
        _baselineErrorSum = 0;
        _baselineErrorCount = 0;
        _baselineErrorDeg = 0;
        _instantErrorDeg = 0;
        _driftDeg = 0;
        _maxDriftDeg = 0;
        _alarmActive = false;

        _abInit = false;
        _minA = _maxA = _minB = _maxB = 0;
        _centerA = _offsetA; _centerB = _offsetB;
        _vppA = _vppB = 0;

        _cdInit = false;
        _cdCenterReady = false;
        _minC = _maxC = _minD = _maxD = 0;
        _centerC = _offsetC; _centerD = _offsetD;
        _vppC = _vppD = 0;
        _cdCoverageDeg = 0;

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

            // 均值滑动（“取当前均值”按钮用）
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

            // ===== Vpp 中心更新 =====
            UpdateCenters(rawA, rawB, rawC, rawD);

            // 去偏
            double a = rawA - _centerA;
            double b = rawB - _centerB;
            double z = rawZ - _offsetZ;
            double cDemean = _cdValid ? rawC - _centerC : 0;
            double dDemean = _cdValid ? rawD - _centerD : 0;

            // 固定模式下仍支持慢速自动去偏
            if (!_autoCenterFromVpp && _autoCenter && _init)
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

            // CD 更新
            if (_cdValid)
            {
                const double minCdAmp = 0.005;
                if (Math.Abs(cDemean) > minCdAmp || Math.Abs(dDemean) > minCdAmp)
                {
                    double cd = Math.Atan2(dDemean, cDemean) * 180.0 / Math.PI;
                    if (cd < 0) cd += 360;
                    _cdAngleDeg = cd;
                }
            }

            // 静止判据
            const double minAmp = 0.01;
            if (Math.Abs(a) < minAmp && Math.Abs(b) < minAmp)
            {
                _speedRps *= 0.95;
                EmitSnapshot(a, b, z, cDemean, dDemean, rawA, rawB, rawZ);
                return;
            }

            // ABZ 解算
            double theta = Math.Atan2(b, a);

            if (!_init)
            {
                _lastTheta = theta;
                _lastSpeedTime = _timeAccum;
                _lastSpeedAngle = 0;
                _init = true;
                EmitSnapshot(a, b, z, cDemean, dDemean, rawA, rawB, rawZ);
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

            // C/D 覆盖率累计（用转速×dt 积分，绝对值累加）
            if (_cdValid && _autoCenterFromVpp && !_cdCenterReady)
            {
                if (Math.Abs(_speedRps) >= _cdCenterMinSpeedRps)
                    _cdCoverageDeg += Math.Abs(_speedRps) * _dtPerFrame * 360.0;
                if (_cdCoverageDeg >= _cdCoverageTargetDeg)
                {
                    _cdCenterReady = true;
                    _centerC = (_minC + _maxC) * 0.5;
                    _centerD = (_minD + _maxD) * 0.5;
                    _vppC = _maxC - _minC;
                    _vppD = _maxD - _minD;
                }
            }

            EmitSnapshot(a, b, z, cDemean, dDemean, rawA, rawB, rawZ);
        }
    }

    /// <summary>更新 A/B 快追踪 + C/D 慢追踪（估计阶段只增不减）</summary>
    private void UpdateCenters(double rawA, double rawB, double rawC, double rawD)
    {
        if (!_autoCenterFromVpp)
        {
            _centerA = _offsetA; _centerB = _offsetB;
            _centerC = _offsetC; _centerD = _offsetD;
            return;
        }

        // A/B：滑动衰减 min/max（快追踪，0.5s 时间常数）
        if (!_abInit)
        {
            _minA = _maxA = rawA;
            _minB = _maxB = rawB;
            _abInit = true;
        }
        else
        {
            UpdateRangeSliding(ref _minA, ref _maxA, rawA, _vppTrackAlpha);
            UpdateRangeSliding(ref _minB, ref _maxB, rawB, _vppTrackAlpha);
        }
        _centerA = (_minA + _maxA) * 0.5;
        _centerB = (_minB + _maxB) * 0.5;
        _vppA = _maxA - _minA;
        _vppB = _maxB - _minB;

        // C/D：估计阶段纯 min/max（只增不减）；完成后冻结或极慢追踪
        if (!_cdValid)
        {
            _centerC = _offsetC; _centerD = _offsetD;
            return;
        }

        if (!_cdInit)
        {
            _minC = _maxC = rawC;
            _minD = _maxD = rawD;
            _cdInit = true;
        }

        if (!_cdCenterReady)
        {
            // 纯 min/max，不做衰减 —— 保证转一整圈后精确
            if (rawC > _maxC) _maxC = rawC;
            if (rawC < _minC) _minC = rawC;
            if (rawD > _maxD) _maxD = rawD;
            if (rawD < _minD) _minD = rawD;

            _vppC = _maxC - _minC;
            _vppD = _maxD - _minD;

            // 未完成前用 offset 作为临时中心（保证解算能工作）
            _centerC = _offsetC;
            _centerD = _offsetD;
        }
        else
        {
            // 完成后：默认冻结，除非配置了追踪系数
            if (_cdCenterTrackAlphaAfterReady > 0)
            {
                UpdateRangeSliding(ref _minC, ref _maxC, rawC, _cdCenterTrackAlphaAfterReady);
                UpdateRangeSliding(ref _minD, ref _maxD, rawD, _cdCenterTrackAlphaAfterReady);
                _centerC = (_minC + _maxC) * 0.5;
                _centerD = (_minD + _maxD) * 0.5;
                _vppC = _maxC - _minC;
                _vppD = _maxD - _minD;
            }
            // 冻结时保持 centerC/centerD 不变
        }
    }

    /// <summary>滑动衰减 min/max：新高直接更新，否则缓慢向 v 收缩</summary>
    private static void UpdateRangeSliding(ref double min, ref double max, double v, double alpha)
    {
        if (v > max) max = v;
        else max += alpha * (v - max);

        if (v < min) min = v;
        else min += alpha * (v - min);
    }

    private void EmitSnapshot(double a, double b, double z,
                              double cDemean, double dDemean,
                              double rawA, double rawB, double rawZ)
    {
        double angleDeg = (_continuousAngle * 180 / Math.PI) % 360;
        if (angleDeg < 0) angleDeg += 360;
        int dir = _speedRps > 0.02 ? 1 : (_speedRps < -0.02 ? -1 : 0);

        // ===== 漂移监控 =====
        double err = 0;
        bool baselineBlocked = _cdValid && _autoCenterFromVpp && !_cdCenterReady;

        if (_cdValid && _synced)
        {
            err = Wrap180(angleDeg - _cdAngleDeg);
            _instantErrorDeg = err;

            if (err < _minErrDeg) _minErrDeg = err;
            if (err > _maxErrDeg) _maxErrDeg = err;
            _sumAbsErrDeg += Math.Abs(err);
            _errCount++;

            if (!_baselineReady)
            {
                // 关键：C/D 中心估计完成前不学习基准，否则学到错误偏移
                if (!baselineBlocked && Math.Abs(_speedRps) < 3.0)
                {
                    _baselineErrorSum += err;
                    _baselineErrorCount++;
                    if (_baselineErrorCount >= _baselineLearnTarget)
                    {
                        _baselineErrorDeg = _baselineErrorSum / _baselineErrorCount;
                        _baselineReady = true;
                    }
                }
            }
            else
            {
                double drift = Wrap180(err - _baselineErrorDeg);
                _driftDeg = drift;
                double absDrift = Math.Abs(drift);
                if (absDrift > _maxDriftDeg) _maxDriftDeg = absDrift;
                _alarmActive = absDrift > _errorLimitDeg;
            }
        }
        else
        {
            _driftDeg = 0;
            _alarmActive = false;
        }

        double electricDrift = _polePairs > 0 ? _driftDeg * _polePairs : 0;
        double electricLimit = _polePairs > 0 ? _errorLimitDeg * _polePairs : 0;

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
            C = cDemean,
            D = dDemean,
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

            BaselineReady = _baselineReady,
            BaselineErrorDeg = _baselineErrorDeg,
            InstantErrorDeg = _instantErrorDeg,
            DriftDeg = _driftDeg,
            MaxDriftDeg = _maxDriftDeg,
            AlarmActive = _alarmActive,
            LimitDeg = _errorLimitDeg,
            PolePairs = _polePairs,
            ElectricDriftDeg = electricDrift,
            ElectricLimitDeg = electricLimit,
            BaselineLearnProgress = _baselineErrorCount,
            BaselineLearnTarget = _baselineLearnTarget,
            BaselineLearnSeconds = _baselineLearnSeconds,
            BaselineBlockedByCdEstimate = baselineBlocked && !_baselineReady,

            MinAngleErrorDeg = _errCount > 0 ? _minErrDeg : 0,
            MaxAngleErrorDeg = _errCount > 0 ? _maxErrDeg : 0,
            MeanAbsAngleErrorDeg = _errCount > 0 ? _sumAbsErrDeg / _errCount : 0,
            AngleErrorSamples = _errCount,

            MeanA = _meanA,
            MeanB = _meanB,
            MeanC = _meanC,
            MeanD = _meanD,

            AutoCenterFromVpp = _autoCenterFromVpp,
            UsedCenterA = _centerA,
            UsedCenterB = _centerB,
            UsedCenterC = _centerC,
            UsedCenterD = _centerD,
            EstVppA = _vppA,
            EstVppB = _vppB,
            EstVppC = _vppC,
            EstVppD = _vppD,

            CdCenterReady = !_cdValid || !_autoCenterFromVpp || _cdCenterReady,
            CdCoverageDeg = _cdCoverageDeg,
            CdCoverageTargetDeg = _cdCoverageTargetDeg,
        };
    }

    public EncoderSnapshot GetSnapshot() { lock (_lock) return _snapshot; }

    private static double Wrap180(double deg)
    {
        while (deg > 180) deg -= 360;
        while (deg < -180) deg += 360;
        return deg;
    }

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