namespace Elbwald.DesktopTools.Audio.Editor.Basic.Dsp;

internal readonly record struct TrackDynamicsSettings(
    bool CompressorEnabled,
    double CompressorStrength,
    bool LimiterEnabled,
    double LimiterCeilingDb);

/// <summary>
/// Lightweight stereo-linked dynamics stage for the basic editor. The single
/// compressor strength control intentionally maps onto musically sensible
/// threshold/ratio/timing values so the default UI stays understandable.
/// The limiter is currently a sample-peak safety limiter, not a true-peak
/// oversampling limiter.
/// </summary>
internal sealed class TrackDynamicsProcessor
{
    private double _detectorEnvelope;
    private double _smoothedGainDb;

    public void Process(float[] interleavedStereo, int sampleRate, TrackDynamicsSettings settings)
    {
        ProcessCompressor(interleavedStereo, sampleRate, settings);
        ProcessLimiter(interleavedStereo, settings);
    }

    public void ProcessCompressor(float[] interleavedStereo, int sampleRate, TrackDynamicsSettings settings)
    {
        if (interleavedStereo.Length < 2 || sampleRate <= 0)
        {
            return;
        }

        var compressorEnabled = settings.CompressorEnabled;
        if (!compressorEnabled)
        {
            _detectorEnvelope = 0;
            _smoothedGainDb = 0;
            return;
        }

        var strength = Math.Clamp(settings.CompressorStrength, 0, 100) / 100d;
        var thresholdDb = -8d - (28d * strength);
        var ratio = 1.5d + (6.5d * strength);
        var kneeDb = 6d;
        var attackSeconds = (12d - (7d * strength)) / 1000d;
        var releaseSeconds = (180d - (60d * strength)) / 1000d;
        var detectorAttack = Math.Exp(-1d / Math.Max(1d, sampleRate * attackSeconds));
        var detectorRelease = Math.Exp(-1d / Math.Max(1d, sampleRate * releaseSeconds));
        var gainAttack = Math.Exp(-1d / Math.Max(1d, sampleRate * 0.006d));
        var gainRelease = Math.Exp(-1d / Math.Max(1d, sampleRate * 0.090d));
        var makeupDb = 4d * strength;

        for (var index = 0; index + 1 < interleavedStereo.Length; index += 2)
        {
            var left = interleavedStereo[index];
            var right = interleavedStereo[index + 1];
            var detector = Math.Max(Math.Abs(left), Math.Abs(right));
            var detectorCoefficient = detector > _detectorEnvelope
                ? detectorAttack
                : detectorRelease;
            _detectorEnvelope = detectorCoefficient * _detectorEnvelope +
                                (1d - detectorCoefficient) * detector;

            var levelDb = LinearToDb(_detectorEnvelope);
            var gainReductionDb = CalculateCompressionGainDb(levelDb, thresholdDb, ratio, kneeDb);
            var targetGainDb = gainReductionDb + makeupDb;
            var gainCoefficient = targetGainDb < _smoothedGainDb
                ? gainAttack
                : gainRelease;
            _smoothedGainDb = gainCoefficient * _smoothedGainDb +
                              (1d - gainCoefficient) * targetGainDb;

            var gain = DbToLinear(_smoothedGainDb);
            interleavedStereo[index] = (float)(left * gain);
            interleavedStereo[index + 1] = (float)(right * gain);
        }
    }

    public void ProcessLimiter(float[] interleavedStereo, TrackDynamicsSettings settings)
    {
        if (!settings.LimiterEnabled || interleavedStereo.Length < 2)
        {
            return;
        }

        var limiterCeiling = (float)DbToLinear(Math.Clamp(settings.LimiterCeilingDb, -12, 0));
        for (var index = 0; index + 1 < interleavedStereo.Length; index += 2)
        {
            interleavedStereo[index] = Math.Clamp(interleavedStereo[index], -limiterCeiling, limiterCeiling);
            interleavedStereo[index + 1] = Math.Clamp(interleavedStereo[index + 1], -limiterCeiling, limiterCeiling);
        }
    }

    public void Reset()
    {
        _detectorEnvelope = 0;
        _smoothedGainDb = 0;
    }

    private static double CalculateCompressionGainDb(
        double levelDb,
        double thresholdDb,
        double ratio,
        double kneeDb)
    {
        var overDb = levelDb - thresholdDb;
        var halfKnee = kneeDb / 2d;

        if (overDb <= -halfKnee)
        {
            return 0;
        }

        if (overDb >= halfKnee)
        {
            return -(overDb - overDb / ratio);
        }

        // Soft-knee interpolation following the standard quadratic shape.
        var kneePosition = overDb + halfKnee;
        var compressed = (1d / ratio - 1d) * kneePosition * kneePosition / (2d * kneeDb);
        return compressed;
    }

    private static double DbToLinear(double db) => Math.Pow(10d, db / 20d);

    private static double LinearToDb(double linear) =>
        linear <= 0.000_000_1d ? -140d : 20d * Math.Log10(linear);
}
