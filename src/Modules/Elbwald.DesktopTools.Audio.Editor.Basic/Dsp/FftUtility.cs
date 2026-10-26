using System.Numerics;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Dsp;

/// <summary>
/// Small radix-2 FFT helper shared by analysis and restoration DSP.
/// </summary>
internal static class FftUtility
{
    public static void Forward(Complex[] buffer) => TransformInPlace(buffer, inverse: false);

    public static void Inverse(Complex[] buffer) => TransformInPlace(buffer, inverse: true);

    private static void TransformInPlace(Complex[] buffer, bool inverse)
    {
        var length = buffer.Length;
        if (length < 2 || (length & (length - 1)) != 0)
        {
            throw new ArgumentException("FFT length must be a power of two.", nameof(buffer));
        }

        for (var index = 1; index < length; index++)
        {
            var reversed = ReverseBits(index, length);
            if (reversed > index)
            {
                (buffer[index], buffer[reversed]) = (buffer[reversed], buffer[index]);
            }
        }

        var direction = inverse ? 1d : -1d;
        for (var size = 2; size <= length; size <<= 1)
        {
            var half = size >> 1;
            var step = direction * 2d * Math.PI / size;
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

        if (!inverse)
        {
            return;
        }

        var scale = 1d / length;
        for (var index = 0; index < length; index++)
        {
            buffer[index] *= scale;
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
