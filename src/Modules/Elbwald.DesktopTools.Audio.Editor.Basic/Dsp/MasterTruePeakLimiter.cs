using Elbwald.DesktopTools.Audio.Editor.Basic.Models;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Dsp;

/// <summary>
/// Conservative linked-stereo safety limiter. Each render block is measured at
/// 4x inter-sample resolution and attenuated as one linked block when needed.
/// Release is smoothed between blocks. This is intentionally transparent and
/// conservative rather than a loudness-maximizing brickwall design.
/// </summary>
internal sealed class MasterTruePeakLimiter
{
    private const int TailFrames = 18;
    private const double SafetyMarginDb = 0.15;
    private const double ReleaseSeconds = 0.120;

    private readonly float[] _preLimiterTail = new float[TailFrames * 2];
    private readonly object _statisticsGate = new();
    private int _tailFrameCount;
    private double _currentGain = 1.0;

    private long _statisticsFrames;
    private double _statisticsReductionSumDb;
    private double _statisticsMaxReductionDb;
    private long _statisticsFramesAbove3Db;
    private long _statisticsFramesAbove5Db;
    private double _statisticsCurrentReductionDb;

    public void Process(float[] interleavedStereo, int sampleRate, bool enabled, double ceilingDbtp)
    {
        ArgumentNullException.ThrowIfNull(interleavedStereo);
        if (interleavedStereo.Length == 0)
        {
            return;
        }

        if (!enabled)
        {
            Reset();
            return;
        }

        var currentFrames = interleavedStereo.Length / 2;
        var combined = new float[(_tailFrameCount + currentFrames) * 2];
        if (_tailFrameCount > 0)
        {
            Array.Copy(_preLimiterTail, 0, combined, 0, _tailFrameCount * 2);
        }
        Array.Copy(interleavedStereo, 0, combined, _tailFrameCount * 2, interleavedStereo.Length);

        var detector = new TruePeakDetector();
        detector.Process(combined);
        detector.Complete();
        var measuredPeak = Math.Max(detector.MaxLeftLinear, detector.MaxRightLinear);

        CaptureTail(interleavedStereo);

        var safeCeiling = Math.Clamp(ceilingDbtp - SafetyMarginDb, -18.0, -0.05);
        var ceilingLinear = Math.Pow(10.0, safeCeiling / 20.0);
        var targetGain = measuredPeak > ceilingLinear && measuredPeak > 1e-12
            ? Math.Clamp(ceilingLinear / measuredPeak, 0.0, 1.0)
            : 1.0;

        if (targetGain < _currentGain)
        {
            _currentGain = targetGain;
        }

        var releasePerFrame = 1.0 - Math.Exp(-1.0 / Math.Max(1.0, sampleRate * ReleaseSeconds));
        var blockReductionSumDb = 0.0;
        var blockMaxReductionDb = 0.0;
        long blockFramesAbove3Db = 0;
        long blockFramesAbove5Db = 0;
        var blockCurrentReductionDb = GainToReductionDb(_currentGain);

        for (var frame = 0; frame < currentFrames; frame++)
        {
            if (_currentGain < targetGain)
            {
                _currentGain += (targetGain - _currentGain) * releasePerFrame;
                if (_currentGain > targetGain)
                {
                    _currentGain = targetGain;
                }
            }

            var reductionDb = GainToReductionDb(_currentGain);
            blockReductionSumDb += reductionDb;
            blockMaxReductionDb = Math.Max(blockMaxReductionDb, reductionDb);
            if (reductionDb > 3.0)
            {
                blockFramesAbove3Db++;
            }
            if (reductionDb > 5.0)
            {
                blockFramesAbove5Db++;
            }
            blockCurrentReductionDb = reductionDb;

            var sampleIndex = frame * 2;
            interleavedStereo[sampleIndex] = (float)(interleavedStereo[sampleIndex] * _currentGain);
            interleavedStereo[sampleIndex + 1] = (float)(interleavedStereo[sampleIndex + 1] * _currentGain);
        }

        lock (_statisticsGate)
        {
            _statisticsFrames += currentFrames;
            _statisticsReductionSumDb += blockReductionSumDb;
            _statisticsMaxReductionDb = Math.Max(_statisticsMaxReductionDb, blockMaxReductionDb);
            _statisticsFramesAbove3Db += blockFramesAbove3Db;
            _statisticsFramesAbove5Db += blockFramesAbove5Db;
            _statisticsCurrentReductionDb = blockCurrentReductionDb;
        }
    }

    public MasterLimiterReductionSnapshot GetReductionSnapshot()
    {
        lock (_statisticsGate)
        {
            if (_statisticsFrames <= 0)
            {
                return MasterLimiterReductionSnapshot.Empty;
            }

            return new MasterLimiterReductionSnapshot(
                true,
                _statisticsCurrentReductionDb,
                _statisticsMaxReductionDb,
                _statisticsReductionSumDb / _statisticsFrames,
                _statisticsFramesAbove3Db / (double)_statisticsFrames * 100.0,
                _statisticsFramesAbove5Db / (double)_statisticsFrames * 100.0);
        }
    }

    public void Reset()
    {
        Array.Clear(_preLimiterTail, 0, _preLimiterTail.Length);
        _tailFrameCount = 0;
        _currentGain = 1.0;

        lock (_statisticsGate)
        {
            _statisticsFrames = 0;
            _statisticsReductionSumDb = 0;
            _statisticsMaxReductionDb = 0;
            _statisticsFramesAbove3Db = 0;
            _statisticsFramesAbove5Db = 0;
            _statisticsCurrentReductionDb = 0;
        }
    }

    private void CaptureTail(float[] interleavedStereo)
    {
        var frames = interleavedStereo.Length / 2;
        var copyFrames = Math.Min(TailFrames, frames);
        if (copyFrames <= 0)
        {
            _tailFrameCount = 0;
            return;
        }

        var sourceIndex = (frames - copyFrames) * 2;
        Array.Copy(interleavedStereo, sourceIndex, _preLimiterTail, 0, copyFrames * 2);
        _tailFrameCount = copyFrames;
    }

    private static double GainToReductionDb(double gain)
    {
        if (gain >= 0.999999)
        {
            return 0;
        }

        return Math.Clamp(-20.0 * Math.Log10(Math.Max(gain, 1e-9)), 0, 120);
    }
}
