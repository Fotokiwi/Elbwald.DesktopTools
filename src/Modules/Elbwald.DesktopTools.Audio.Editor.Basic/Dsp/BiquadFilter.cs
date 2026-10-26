namespace Elbwald.DesktopTools.Audio.Editor.Basic.Dsp;

internal readonly record struct BiquadCoefficients(
    double B0,
    double B1,
    double B2,
    double A1,
    double A2)
{
    public static BiquadCoefficients Identity => new(1, 0, 0, 0, 0);

    public double MagnitudeAt(double frequencyHz, int sampleRate)
    {
        if (sampleRate <= 0 || frequencyHz <= 0)
        {
            return 1;
        }

        var omega = 2d * Math.PI * Math.Clamp(frequencyHz, 1d, sampleRate * 0.499d) / sampleRate;
        var cos1 = Math.Cos(omega);
        var sin1 = Math.Sin(omega);
        var cos2 = Math.Cos(2d * omega);
        var sin2 = Math.Sin(2d * omega);

        var numeratorReal = B0 + B1 * cos1 + B2 * cos2;
        var numeratorImag = -(B1 * sin1 + B2 * sin2);
        var denominatorReal = 1d + A1 * cos1 + A2 * cos2;
        var denominatorImag = -(A1 * sin1 + A2 * sin2);

        var numerator = numeratorReal * numeratorReal + numeratorImag * numeratorImag;
        var denominator = denominatorReal * denominatorReal + denominatorImag * denominatorImag;
        if (denominator <= double.Epsilon)
        {
            return 1;
        }

        return Math.Sqrt(Math.Max(0, numerator / denominator));
    }
}

internal static class BiquadDesign
{
    public static BiquadCoefficients LowPass(int sampleRate, double frequencyHz, double q = 0.70710678)
    {
        var (cos, sin, alpha) = Common(sampleRate, frequencyHz, q);
        var b0 = (1d - cos) * 0.5d;
        var b1 = 1d - cos;
        var b2 = b0;
        var a0 = 1d + alpha;
        var a1 = -2d * cos;
        var a2 = 1d - alpha;
        return Normalize(b0, b1, b2, a0, a1, a2);
    }

    public static BiquadCoefficients HighPass(int sampleRate, double frequencyHz, double q = 0.70710678)
    {
        var (cos, sin, alpha) = Common(sampleRate, frequencyHz, q);
        var b0 = (1d + cos) * 0.5d;
        var b1 = -(1d + cos);
        var b2 = b0;
        var a0 = 1d + alpha;
        var a1 = -2d * cos;
        var a2 = 1d - alpha;
        return Normalize(b0, b1, b2, a0, a1, a2);
    }

    public static BiquadCoefficients Notch(
        int sampleRate,
        double frequencyHz,
        double q = 12d)
    {
        var frequency = ClampFrequency(sampleRate, frequencyHz);
        var omega = 2d * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(omega);
        var sin = Math.Sin(omega);
        var alpha = sin / (2d * Math.Max(0.1d, q));

        var b0 = 1d;
        var b1 = -2d * cos;
        var b2 = 1d;
        var a0 = 1d + alpha;
        var a1 = -2d * cos;
        var a2 = 1d - alpha;
        return Normalize(b0, b1, b2, a0, a1, a2);
    }

    public static BiquadCoefficients Peaking(
        int sampleRate,
        double frequencyHz,
        double gainDb,
        double q = 0.9)
    {
        var frequency = ClampFrequency(sampleRate, frequencyHz);
        var omega = 2d * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(omega);
        var sin = Math.Sin(omega);
        var alpha = sin / (2d * Math.Max(0.1, q));
        var a = Math.Pow(10d, gainDb / 40d);

        var b0 = 1d + alpha * a;
        var b1 = -2d * cos;
        var b2 = 1d - alpha * a;
        var a0 = 1d + alpha / a;
        var a1 = -2d * cos;
        var a2 = 1d - alpha / a;
        return Normalize(b0, b1, b2, a0, a1, a2);
    }

    public static BiquadCoefficients LowShelf(
        int sampleRate,
        double frequencyHz,
        double gainDb,
        double slope = 1d)
    {
        var frequency = ClampFrequency(sampleRate, frequencyHz);
        var omega = 2d * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(omega);
        var sin = Math.Sin(omega);
        var a = Math.Pow(10d, gainDb / 40d);
        var safeSlope = Math.Clamp(slope, 0.1, 2d);
        var alpha = sin / 2d * Math.Sqrt((a + 1d / a) * (1d / safeSlope - 1d) + 2d);
        var beta = 2d * Math.Sqrt(a) * alpha;

        var b0 = a * ((a + 1d) - (a - 1d) * cos + beta);
        var b1 = 2d * a * ((a - 1d) - (a + 1d) * cos);
        var b2 = a * ((a + 1d) - (a - 1d) * cos - beta);
        var a0 = (a + 1d) + (a - 1d) * cos + beta;
        var a1 = -2d * ((a - 1d) + (a + 1d) * cos);
        var a2 = (a + 1d) + (a - 1d) * cos - beta;
        return Normalize(b0, b1, b2, a0, a1, a2);
    }

    public static BiquadCoefficients HighShelf(
        int sampleRate,
        double frequencyHz,
        double gainDb,
        double slope = 1d)
    {
        var frequency = ClampFrequency(sampleRate, frequencyHz);
        var omega = 2d * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(omega);
        var sin = Math.Sin(omega);
        var a = Math.Pow(10d, gainDb / 40d);
        var safeSlope = Math.Clamp(slope, 0.1, 2d);
        var alpha = sin / 2d * Math.Sqrt((a + 1d / a) * (1d / safeSlope - 1d) + 2d);
        var beta = 2d * Math.Sqrt(a) * alpha;

        var b0 = a * ((a + 1d) + (a - 1d) * cos + beta);
        var b1 = -2d * a * ((a - 1d) + (a + 1d) * cos);
        var b2 = a * ((a + 1d) + (a - 1d) * cos - beta);
        var a0 = (a + 1d) - (a - 1d) * cos + beta;
        var a1 = 2d * ((a - 1d) - (a + 1d) * cos);
        var a2 = (a + 1d) - (a - 1d) * cos - beta;
        return Normalize(b0, b1, b2, a0, a1, a2);
    }

    private static (double Cos, double Sin, double Alpha) Common(
        int sampleRate,
        double frequencyHz,
        double q)
    {
        var frequency = ClampFrequency(sampleRate, frequencyHz);
        var omega = 2d * Math.PI * frequency / sampleRate;
        var sin = Math.Sin(omega);
        return (Math.Cos(omega), sin, sin / (2d * Math.Max(0.1, q)));
    }

    private static double ClampFrequency(int sampleRate, double frequencyHz) =>
        Math.Clamp(frequencyHz, 10d, Math.Max(20d, sampleRate * 0.49d));

    private static BiquadCoefficients Normalize(
        double b0,
        double b1,
        double b2,
        double a0,
        double a1,
        double a2)
    {
        if (Math.Abs(a0) <= double.Epsilon)
        {
            return BiquadCoefficients.Identity;
        }

        return new BiquadCoefficients(
            b0 / a0,
            b1 / a0,
            b2 / a0,
            a1 / a0,
            a2 / a0);
    }
}

internal sealed class BiquadStereoSection
{
    private BiquadCoefficients _coefficients = BiquadCoefficients.Identity;
    private double _leftZ1;
    private double _leftZ2;
    private double _rightZ1;
    private double _rightZ2;
    private bool _enabled;

    public void Configure(bool enabled, BiquadCoefficients coefficients)
    {
        if (_enabled != enabled)
        {
            Reset();
        }

        _enabled = enabled;
        _coefficients = coefficients;
    }

    public void Process(float[] interleavedStereo)
    {
        if (!_enabled)
        {
            return;
        }

        var c = _coefficients;
        for (var index = 0; index < interleavedStereo.Length; index += 2)
        {
            var leftInput = interleavedStereo[index];
            var leftOutput = c.B0 * leftInput + _leftZ1;
            _leftZ1 = c.B1 * leftInput - c.A1 * leftOutput + _leftZ2;
            _leftZ2 = c.B2 * leftInput - c.A2 * leftOutput;
            interleavedStereo[index] = (float)leftOutput;

            var rightInput = interleavedStereo[index + 1];
            var rightOutput = c.B0 * rightInput + _rightZ1;
            _rightZ1 = c.B1 * rightInput - c.A1 * rightOutput + _rightZ2;
            _rightZ2 = c.B2 * rightInput - c.A2 * rightOutput;
            interleavedStereo[index + 1] = (float)rightOutput;
        }
    }

    public void Reset()
    {
        _leftZ1 = 0;
        _leftZ2 = 0;
        _rightZ1 = 0;
        _rightZ2 = 0;
    }
}
