namespace Elbwald.DesktopTools.Audio.Editor.Basic.Dsp;

internal readonly record struct MasterLoudnessSnapshot(
    bool HasData,
    double? IntegratedLufs,
    double? MomentaryLufs,
    double? ShortTermLufs,
    double? MaxMomentaryLufs,
    double? MaxShortTermLufs,
    double TruePeakLeftDbtp,
    double TruePeakRightDbtp)
{
    public static MasterLoudnessSnapshot Empty => new(
        false,
        null,
        null,
        null,
        null,
        null,
        -120,
        -120);
}

/// <summary>
/// Stereo K-weighted loudness meter following the BS.1770/R128 measurement model:
/// 400 ms momentary blocks, 3 s short-term window and gated integrated loudness.
/// True peak is estimated with 4x windowed-sinc interpolation.
/// </summary>
internal sealed class MasterLoudnessMeter
{
    private const double LoudnessOffset = -0.691;
    private const double AbsoluteGateLufs = -70.0;
    private const double MeterFloorDb = -120.0;

    private readonly int _sampleRate;
    private readonly int _momentaryFrames;
    private readonly int _shortTermFrames;
    private readonly int _integratedStepFrames;
    private readonly double[] _energyRing;
    private readonly List<double> _integratedBlockEnergies = [];
    private readonly KWeightingStereoFilter _kWeighting;
    private readonly TruePeakDetector _truePeak = new();

    private int _ringWriteIndex;
    private int _ringCount;
    private double _momentaryEnergySum;
    private double _shortTermEnergySum;
    private long _processedFrames;
    private long _nextIntegratedBlockFrame;
    private double? _maxMomentaryLufs;
    private double? _maxShortTermLufs;

    public MasterLoudnessMeter(int sampleRate)
    {
        if (sampleRate is < 8_000 or > 384_000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        _sampleRate = sampleRate;
        _momentaryFrames = Math.Max(1, (int)Math.Round(sampleRate * 0.400));
        _shortTermFrames = Math.Max(_momentaryFrames, (int)Math.Round(sampleRate * 3.000));
        _integratedStepFrames = Math.Max(1, (int)Math.Round(sampleRate * 0.100));
        _energyRing = new double[_shortTermFrames];
        _nextIntegratedBlockFrame = _momentaryFrames;
        _kWeighting = new KWeightingStereoFilter(sampleRate);
    }

    public void Process(float[] interleavedStereo)
    {
        ArgumentNullException.ThrowIfNull(interleavedStereo);
        if (interleavedStereo.Length == 0)
        {
            return;
        }

        if ((interleavedStereo.Length & 1) != 0)
        {
            throw new ArgumentException("Stereo-Puffer muss eine gerade Sample-Anzahl besitzen.", nameof(interleavedStereo));
        }

        _truePeak.Process(interleavedStereo);

        for (var index = 0; index < interleavedStereo.Length; index += 2)
        {
            var (left, right) = _kWeighting.Process(interleavedStereo[index], interleavedStereo[index + 1]);
            AddEnergy(left * left + right * right);
        }
    }

    public void Complete() => _truePeak.Complete();

    public void Reset()
    {
        Array.Clear(_energyRing, 0, _energyRing.Length);
        _integratedBlockEnergies.Clear();
        _ringWriteIndex = 0;
        _ringCount = 0;
        _momentaryEnergySum = 0;
        _shortTermEnergySum = 0;
        _processedFrames = 0;
        _nextIntegratedBlockFrame = _momentaryFrames;
        _maxMomentaryLufs = null;
        _maxShortTermLufs = null;
        _kWeighting.Reset();
        _truePeak.Reset();
    }

    public MasterLoudnessSnapshot GetSnapshot()
    {
        if (_processedFrames <= 0)
        {
            return MasterLoudnessSnapshot.Empty;
        }

        var momentary = _ringCount >= _momentaryFrames
            ? LoudnessFromEnergy(_momentaryEnergySum / _momentaryFrames)
            : null;
        var shortTerm = _ringCount >= _shortTermFrames
            ? LoudnessFromEnergy(_shortTermEnergySum / _shortTermFrames)
            : null;
        var integrated = CalculateIntegratedLoudness();

        return new MasterLoudnessSnapshot(
            true,
            integrated,
            momentary,
            shortTerm,
            _maxMomentaryLufs,
            _maxShortTermLufs,
            LinearToDb(_truePeak.MaxLeftLinear),
            LinearToDb(_truePeak.MaxRightLinear));
    }

    private void AddEnergy(double energy)
    {
        energy = Math.Max(0, energy);

        if (_ringCount >= _momentaryFrames)
        {
            var momentaryOldIndex = _ringWriteIndex - _momentaryFrames;
            if (momentaryOldIndex < 0)
            {
                momentaryOldIndex += _energyRing.Length;
            }

            _momentaryEnergySum -= _energyRing[momentaryOldIndex];
        }

        if (_ringCount >= _energyRing.Length)
        {
            _shortTermEnergySum -= _energyRing[_ringWriteIndex];
        }
        else
        {
            _ringCount++;
        }

        _energyRing[_ringWriteIndex] = energy;
        _ringWriteIndex++;
        if (_ringWriteIndex >= _energyRing.Length)
        {
            _ringWriteIndex = 0;
        }

        _momentaryEnergySum += energy;
        _shortTermEnergySum += energy;
        _processedFrames++;

        if (_processedFrames < _nextIntegratedBlockFrame)
        {
            return;
        }

        while (_processedFrames >= _nextIntegratedBlockFrame)
        {
            if (_ringCount >= _momentaryFrames)
            {
                var blockEnergy = Math.Max(0, _momentaryEnergySum / _momentaryFrames);
                _integratedBlockEnergies.Add(blockEnergy);

                var momentary = LoudnessFromEnergy(blockEnergy);
                if (momentary is not null)
                {
                    _maxMomentaryLufs = _maxMomentaryLufs is null
                        ? momentary
                        : Math.Max(_maxMomentaryLufs.Value, momentary.Value);
                }

                if (_ringCount >= _shortTermFrames)
                {
                    var shortTerm = LoudnessFromEnergy(_shortTermEnergySum / _shortTermFrames);
                    if (shortTerm is not null)
                    {
                        _maxShortTermLufs = _maxShortTermLufs is null
                            ? shortTerm
                            : Math.Max(_maxShortTermLufs.Value, shortTerm.Value);
                    }
                }
            }

            _nextIntegratedBlockFrame += _integratedStepFrames;
        }
    }

    private double? CalculateIntegratedLoudness()
    {
        if (_integratedBlockEnergies.Count == 0)
        {
            return null;
        }

        var absoluteGated = _integratedBlockEnergies
            .Where(energy => (LoudnessFromEnergy(energy) ?? double.NegativeInfinity) >= AbsoluteGateLufs)
            .ToArray();
        if (absoluteGated.Length == 0)
        {
            return null;
        }

        var absoluteMean = absoluteGated.Average();
        var ungatedLoudness = LoudnessFromEnergy(absoluteMean);
        if (ungatedLoudness is null)
        {
            return null;
        }

        var relativeGate = ungatedLoudness.Value - 10.0;
        var finalGate = Math.Max(AbsoluteGateLufs, relativeGate);
        var finalBlocks = absoluteGated
            .Where(energy => (LoudnessFromEnergy(energy) ?? double.NegativeInfinity) >= finalGate)
            .ToArray();
        if (finalBlocks.Length == 0)
        {
            return null;
        }

        return LoudnessFromEnergy(finalBlocks.Average());
    }

    private static double? LoudnessFromEnergy(double meanSquare)
    {
        if (meanSquare <= 1e-20)
        {
            return null;
        }

        return LoudnessOffset + 10.0 * Math.Log10(meanSquare);
    }

    private static double LinearToDb(double linear)
    {
        if (linear <= 1e-6)
        {
            return MeterFloorDb;
        }

        return Math.Max(MeterFloorDb, 20.0 * Math.Log10(linear));
    }

    private sealed class KWeightingStereoFilter
    {
        private readonly IirStereoSection _preFilter;
        private readonly IirStereoSection _rlbHighPass;

        public KWeightingStereoFilter(int sampleRate)
        {
            _preFilter = new IirStereoSection(CreatePreFilter(sampleRate));
            _rlbHighPass = new IirStereoSection(CreateRlbHighPass(sampleRate));
        }

        public (double Left, double Right) Process(double left, double right)
        {
            (left, right) = _preFilter.Process(left, right);
            return _rlbHighPass.Process(left, right);
        }

        public void Reset()
        {
            _preFilter.Reset();
            _rlbHighPass.Reset();
        }

        private static FilterCoefficients CreatePreFilter(int sampleRate)
        {
            const double frequency = 1681.974450955533;
            const double gainDb = 3.999843853973347;
            const double q = 0.7071752369554196;
            const double vbExponent = 0.4996667741545416;

            var k = Math.Tan(Math.PI * frequency / sampleRate);
            var vh = Math.Pow(10.0, gainDb / 20.0);
            var vb = Math.Pow(vh, vbExponent);
            var a0 = 1.0 + k / q + k * k;

            return new FilterCoefficients(
                (vh + vb * k / q + k * k) / a0,
                2.0 * (k * k - vh) / a0,
                (vh - vb * k / q + k * k) / a0,
                2.0 * (k * k - 1.0) / a0,
                (1.0 - k / q + k * k) / a0);
        }

        private static FilterCoefficients CreateRlbHighPass(int sampleRate)
        {
            const double frequency = 38.13547087602444;
            const double q = 0.5003270373238773;

            var k = Math.Tan(Math.PI * frequency / sampleRate);
            var a0 = 1.0 + k / q + k * k;

            return new FilterCoefficients(
                1.0 / a0,
                -2.0 / a0,
                1.0 / a0,
                2.0 * (k * k - 1.0) / a0,
                (1.0 - k / q + k * k) / a0);
        }
    }

    private readonly record struct FilterCoefficients(
        double B0,
        double B1,
        double B2,
        double A1,
        double A2);

    private sealed class IirStereoSection
    {
        private readonly FilterCoefficients _coefficients;
        private double _leftZ1;
        private double _leftZ2;
        private double _rightZ1;
        private double _rightZ2;

        public IirStereoSection(FilterCoefficients coefficients)
        {
            _coefficients = coefficients;
        }

        public (double Left, double Right) Process(double left, double right)
        {
            var c = _coefficients;

            var leftOutput = c.B0 * left + _leftZ1;
            _leftZ1 = c.B1 * left - c.A1 * leftOutput + _leftZ2;
            _leftZ2 = c.B2 * left - c.A2 * leftOutput;

            var rightOutput = c.B0 * right + _rightZ1;
            _rightZ1 = c.B1 * right - c.A1 * rightOutput + _rightZ2;
            _rightZ2 = c.B2 * right - c.A2 * rightOutput;

            return (leftOutput, rightOutput);
        }

        public void Reset()
        {
            _leftZ1 = 0;
            _leftZ2 = 0;
            _rightZ1 = 0;
            _rightZ2 = 0;
        }
    }
}

/// <summary>
/// Small streaming 4x inter-sample peak estimator. A short Lanczos-windowed
/// sinc kernel is used to reconstruct three positions between source samples.
/// </summary>
internal sealed class TruePeakDetector
{
    private const int Radius = 8;
    private const int WindowLength = Radius * 2 + 2;
    private const int OversampleFactor = 4;

    private static readonly double[][] Coefficients = BuildCoefficients();

    private readonly double[] _left = new double[WindowLength];
    private readonly double[] _right = new double[WindowLength];
    private int _filled;

    public double MaxLeftLinear { get; private set; }

    public double MaxRightLinear { get; private set; }

    public void Process(float[] interleavedStereo)
    {
        ArgumentNullException.ThrowIfNull(interleavedStereo);
        if ((interleavedStereo.Length & 1) != 0)
        {
            throw new ArgumentException("Stereo-Puffer muss eine gerade Sample-Anzahl besitzen.", nameof(interleavedStereo));
        }

        for (var index = 0; index < interleavedStereo.Length; index += 2)
        {
            Push(interleavedStereo[index], interleavedStereo[index + 1]);
        }
    }

    public void Complete()
    {
        for (var i = 0; i < WindowLength; i++)
        {
            Push(0, 0);
        }
    }

    public void Reset()
    {
        Array.Clear(_left, 0, _left.Length);
        Array.Clear(_right, 0, _right.Length);
        _filled = 0;
        MaxLeftLinear = 0;
        MaxRightLinear = 0;
    }

    private void Push(double left, double right)
    {
        MaxLeftLinear = Math.Max(MaxLeftLinear, Math.Abs(left));
        MaxRightLinear = Math.Max(MaxRightLinear, Math.Abs(right));

        Array.Copy(_left, 1, _left, 0, WindowLength - 1);
        Array.Copy(_right, 1, _right, 0, WindowLength - 1);
        _left[^1] = left;
        _right[^1] = right;

        if (_filled < WindowLength)
        {
            _filled++;
            if (_filled < WindowLength)
            {
                return;
            }
        }

        for (var phase = 0; phase < OversampleFactor; phase++)
        {
            var coefficients = Coefficients[phase];
            double reconstructedLeft = 0;
            double reconstructedRight = 0;
            for (var tap = 0; tap < WindowLength; tap++)
            {
                reconstructedLeft += _left[tap] * coefficients[tap];
                reconstructedRight += _right[tap] * coefficients[tap];
            }

            MaxLeftLinear = Math.Max(MaxLeftLinear, Math.Abs(reconstructedLeft));
            MaxRightLinear = Math.Max(MaxRightLinear, Math.Abs(reconstructedRight));
        }
    }

    private static double[][] BuildCoefficients()
    {
        var result = new double[OversampleFactor][];
        for (var phase = 0; phase < OversampleFactor; phase++)
        {
            var target = Radius + phase / (double)OversampleFactor;
            var coefficients = new double[WindowLength];
            var sum = 0.0;

            for (var tap = 0; tap < WindowLength; tap++)
            {
                var distance = target - tap;
                var coefficient = Sinc(distance) * Sinc(distance / (Radius + 1.0));
                coefficients[tap] = coefficient;
                sum += coefficient;
            }

            if (Math.Abs(sum) > 1e-12)
            {
                for (var tap = 0; tap < coefficients.Length; tap++)
                {
                    coefficients[tap] /= sum;
                }
            }

            result[phase] = coefficients;
        }

        return result;
    }

    private static double Sinc(double value)
    {
        if (Math.Abs(value) < 1e-12)
        {
            return 1.0;
        }

        var x = Math.PI * value;
        return Math.Sin(x) / x;
    }
}
