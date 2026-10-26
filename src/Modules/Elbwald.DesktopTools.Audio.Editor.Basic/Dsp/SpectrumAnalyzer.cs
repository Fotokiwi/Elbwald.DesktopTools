using System.Numerics;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Dsp;

/// <summary>
/// Lightweight FFT analyzer for the detachable tool window. It intentionally
/// runs on the UI/analysis side, never inside the realtime device callback or
/// the playback render producer.
/// </summary>
internal static class SpectrumAnalyzer
{
    public const double FloorDb = -90;

    public static SpectrumFrame Analyze(float[] samples, int sampleRate, int maxFftSize = 4096, double floorDb = FloorDb)
    {
        if (samples.Length < 64 || sampleRate <= 0)
        {
            return SpectrumFrame.Empty;
        }

        var fftSize = LargestPowerOfTwo(Math.Min(samples.Length, maxFftSize));
        if (fftSize < 64)
        {
            return SpectrumFrame.Empty;
        }

        var inputStart = samples.Length - fftSize;
        var buffer = new Complex[fftSize];
        double windowSum = 0;

        for (var index = 0; index < fftSize; index++)
        {
            var window = 0.5d - 0.5d * Math.Cos(2d * Math.PI * index / (fftSize - 1));
            windowSum += window;
            buffer[index] = new Complex(samples[inputStart + index] * window, 0);
        }

        TransformInPlace(buffer);

        var binCount = fftSize / 2 + 1;
        var db = new double[binCount];
        var normalization = windowSum > 0 ? 2d / windowSum : 1d / fftSize;

        for (var bin = 0; bin < binCount; bin++)
        {
            var magnitude = buffer[bin].Magnitude * normalization;
            var value = 20d * Math.Log10(Math.Max(1e-9, magnitude));
            db[bin] = Math.Clamp(value, Math.Min(-1, floorDb), 0);
        }

        return new SpectrumFrame(sampleRate, fftSize, db);
    }

    private static int LargestPowerOfTwo(int value)
    {
        var result = 1;
        while (result <= value / 2)
        {
            result <<= 1;
        }

        return result;
    }

    private static void TransformInPlace(Complex[] buffer)
    {
        var length = buffer.Length;

        for (var index = 1; index < length; index++)
        {
            var reversed = ReverseBits(index, length);
            if (reversed > index)
            {
                (buffer[index], buffer[reversed]) = (buffer[reversed], buffer[index]);
            }
        }

        for (var size = 2; size <= length; size <<= 1)
        {
            var half = size >> 1;
            var step = -2d * Math.PI / size;
            for (var start = 0; start < length; start += size)
            {
                for (var offset = 0; offset < half; offset++)
                {
                    var twiddle = Complex.FromPolarCoordinates(1, step * offset);
                    var even = buffer[start + offset];
                    var odd = buffer[start + offset + half] * twiddle;
                    buffer[start + offset] = even + odd;
                    buffer[start + offset + half] = even - odd;
                }
            }
        }
    }

    private static int ReverseBits(int value, int length)
    {
        var result = 0;
        for (var bit = 1; bit < length; bit <<= 1)
        {
            result = (result << 1) | (value & 1);
            value >>= 1;
        }

        return result;
    }
}

internal sealed record SpectrumFrame(int SampleRate, int FftSize, double[] MagnitudesDb)
{
    public static SpectrumFrame Empty { get; } = new(0, 0, []);

    public bool HasData => SampleRate > 0 && FftSize > 0 && MagnitudesDb.Length > 0;
}
