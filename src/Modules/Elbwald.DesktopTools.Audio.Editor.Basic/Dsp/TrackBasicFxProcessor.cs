namespace Elbwald.DesktopTools.Audio.Editor.Basic.Dsp;

internal readonly record struct TrackBasicFxSettings(
    bool ExpanderEnabled,
    double ExpanderThresholdDb,
    double ExpanderReductionDb,
    bool NotchEnabled,
    double NotchFrequencyHz,
    double NotchWidthPercent,
    bool HumRemovalEnabled,
    double HumFrequencyHz,
    int HumHarmonics,
    double HumReductionDb,
    bool DeEsserEnabled,
    double DeEsserFrequencyHz,
    double DeEsserStrength);

/// <summary>
/// Small first-party cleanup/effect stage used by the basic audio editor.
/// The pre-EQ section contains expander/gate, hum removal and a single notch.
/// The post-EQ section contains a lightweight split-band de-esser.
/// All state is kept across render blocks and reset on seek/mute.
/// </summary>
internal sealed class TrackBasicFxProcessor
{
    private readonly BiquadStereoSection _notch = new();
    private readonly BiquadStereoSection[] _humSections =
        Enumerable.Range(0, 8).Select(_ => new BiquadStereoSection()).ToArray();

    private double _expanderDetector;
    private double _expanderGain = 1d;

    private double _deEsserLowLeft;
    private double _deEsserLowRight;
    private double _deEsserDetector;
    private double _deEsserGain = 1d;

    public void ProcessPreEq(float[] interleavedStereo, int sampleRate, TrackBasicFxSettings settings)
    {
        if (interleavedStereo.Length < 2 || sampleRate <= 0)
        {
            return;
        }

        ProcessExpander(interleavedStereo, sampleRate, settings);
        ProcessHumRemoval(interleavedStereo, sampleRate, settings);
        ProcessNotch(interleavedStereo, sampleRate, settings);
    }

    public void ProcessPostEq(float[] interleavedStereo, int sampleRate, TrackBasicFxSettings settings)
    {
        if (interleavedStereo.Length < 2 || sampleRate <= 0)
        {
            return;
        }

        ProcessDeEsser(interleavedStereo, sampleRate, settings);
    }

    public void Reset()
    {
        _expanderDetector = 0d;
        _expanderGain = 1d;
        _deEsserLowLeft = 0d;
        _deEsserLowRight = 0d;
        _deEsserDetector = 0d;
        _deEsserGain = 1d;
        _notch.Reset();

        foreach (var section in _humSections)
        {
            section.Reset();
        }
    }

    private void ProcessExpander(float[] buffer, int sampleRate, TrackBasicFxSettings settings)
    {
        if (!settings.ExpanderEnabled)
        {
            _expanderDetector = 0d;
            _expanderGain = 1d;
            return;
        }

        var threshold = DbToLinear(Math.Clamp(settings.ExpanderThresholdDb, -80d, -10d));
        var floorGain = DbToLinear(-Math.Clamp(settings.ExpanderReductionDb, 0d, 60d));

        var detectorAttack = Coefficient(sampleRate, 4d);
        var detectorRelease = Coefficient(sampleRate, 90d);
        var openCoefficient = Coefficient(sampleRate, 5d);
        var closeCoefficient = Coefficient(sampleRate, 150d);

        for (var index = 0; index < buffer.Length; index += 2)
        {
            var left = buffer[index];
            var right = buffer[index + 1];
            var detector = Math.Max(Math.Abs(left), Math.Abs(right));
            var detectorCoefficient = detector > _expanderDetector ? detectorAttack : detectorRelease;
            _expanderDetector =
                detectorCoefficient * _expanderDetector +
                (1d - detectorCoefficient) * detector;

            var targetGain = _expanderDetector >= threshold ? 1d : floorGain;
            var gainCoefficient = targetGain > _expanderGain ? openCoefficient : closeCoefficient;
            _expanderGain =
                gainCoefficient * _expanderGain +
                (1d - gainCoefficient) * targetGain;

            buffer[index] = (float)(left * _expanderGain);
            buffer[index + 1] = (float)(right * _expanderGain);
        }
    }

    private void ProcessNotch(float[] buffer, int sampleRate, TrackBasicFxSettings settings)
    {
        var enabled =
            settings.NotchEnabled &&
            settings.NotchFrequencyHz > 10d &&
            settings.NotchFrequencyHz < sampleRate * 0.49d;

        var width = Math.Clamp(settings.NotchWidthPercent, 0d, 100d) / 100d;
        var q = 30d - 28d * width;
        var coefficients = enabled
            ? BiquadDesign.Notch(sampleRate, settings.NotchFrequencyHz, q)
            : BiquadCoefficients.Identity;

        _notch.Configure(enabled, coefficients);
        _notch.Process(buffer);
    }

    private void ProcessHumRemoval(float[] buffer, int sampleRate, TrackBasicFxSettings settings)
    {
        var enabled = settings.HumRemovalEnabled;
        var fundamental = Math.Clamp(settings.HumFrequencyHz, 50d, 60d);
        var harmonics = Math.Clamp(settings.HumHarmonics, 1, _humSections.Length);
        var reduction = Math.Clamp(settings.HumReductionDb, 0d, 30d);

        for (var index = 0; index < _humSections.Length; index++)
        {
            var harmonic = index + 1;
            var frequency = fundamental * harmonic;
            var sectionEnabled = enabled && harmonic <= harmonics && frequency < sampleRate * 0.47d;
            var taperedReduction = reduction / (1d + 0.10d * index);
            var coefficients = sectionEnabled
                ? BiquadDesign.Peaking(sampleRate, frequency, -taperedReduction, q: 18d)
                : BiquadCoefficients.Identity;

            _humSections[index].Configure(sectionEnabled, coefficients);
            _humSections[index].Process(buffer);
        }
    }

    private void ProcessDeEsser(float[] buffer, int sampleRate, TrackBasicFxSettings settings)
    {
        if (!settings.DeEsserEnabled)
        {
            _deEsserLowLeft = 0d;
            _deEsserLowRight = 0d;
            _deEsserDetector = 0d;
            _deEsserGain = 1d;
            return;
        }

        var strength = Math.Clamp(settings.DeEsserStrength, 0d, 100d) / 100d;
        var frequency = Math.Clamp(settings.DeEsserFrequencyHz, 3_000d, Math.Min(12_000d, sampleRate * 0.42d));
        var lowPassPole = Math.Exp(-2d * Math.PI * frequency / sampleRate);
        var detectorAttack = Coefficient(sampleRate, 1.5d);
        var detectorRelease = Coefficient(sampleRate, 65d);
        var gainAttack = Coefficient(sampleRate, 2d);
        var gainRelease = Coefficient(sampleRate, 80d);
        var thresholdDb = -12d - 22d * strength;
        var maxReductionDb = 2d + 10d * strength;
        var ratioFactor = 0.25d + 0.55d * strength;

        for (var index = 0; index < buffer.Length; index += 2)
        {
            var left = buffer[index];
            var right = buffer[index + 1];

            _deEsserLowLeft =
                lowPassPole * _deEsserLowLeft +
                (1d - lowPassPole) * left;
            _deEsserLowRight =
                lowPassPole * _deEsserLowRight +
                (1d - lowPassPole) * right;

            var highLeft = left - _deEsserLowLeft;
            var highRight = right - _deEsserLowRight;
            var detector = Math.Max(Math.Abs(highLeft), Math.Abs(highRight));
            var detectorCoefficient = detector > _deEsserDetector ? detectorAttack : detectorRelease;
            _deEsserDetector =
                detectorCoefficient * _deEsserDetector +
                (1d - detectorCoefficient) * detector;

            var detectorDb = LinearToDb(_deEsserDetector);
            var reductionDb = detectorDb > thresholdDb
                ? Math.Min(maxReductionDb, (detectorDb - thresholdDb) * ratioFactor)
                : 0d;
            var targetGain = DbToLinear(-reductionDb);
            var gainCoefficient = targetGain < _deEsserGain ? gainAttack : gainRelease;
            _deEsserGain =
                gainCoefficient * _deEsserGain +
                (1d - gainCoefficient) * targetGain;

            buffer[index] = (float)(_deEsserLowLeft + highLeft * _deEsserGain);
            buffer[index + 1] = (float)(_deEsserLowRight + highRight * _deEsserGain);
        }
    }

    private static double Coefficient(int sampleRate, double timeMs)
    {
        var seconds = Math.Max(0.0001d, timeMs / 1_000d);
        return Math.Exp(-1d / (sampleRate * seconds));
    }

    private static double DbToLinear(double db) => Math.Pow(10d, db / 20d);

    private static double LinearToDb(double value) =>
        value <= 1e-12 ? -120d : 20d * Math.Log10(value);
}
