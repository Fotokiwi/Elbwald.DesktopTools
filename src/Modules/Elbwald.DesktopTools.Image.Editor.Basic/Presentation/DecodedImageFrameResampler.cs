using Elbwald.DesktopTools.Contracts.Media.Decoding;

namespace Elbwald.DesktopTools.Image.Editor.Basic.Presentation;

/// <summary>
/// Creates a smaller in-memory preview frame for interactive editing. This is not
/// an export resampler; it exists solely to keep slider feedback responsive.
/// </summary>
internal static class DecodedImageFrameResampler
{
    public static DecodedImageFrame ResizeToMaxEdge(DecodedImageFrame source, int maxEdge)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (maxEdge <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEdge));
        }

        if (source.PixelFormat is not ImagePixelFormat.Rgba32 and not ImagePixelFormat.Bgra32)
        {
            throw new NotSupportedException(
                $"Pixelformat '{source.PixelFormat}' wird von der schnellen Editor-Vorschau nicht unterstützt.");
        }

        var sourceMaxEdge = Math.Max(source.PixelWidth, source.PixelHeight);
        if (sourceMaxEdge <= maxEdge)
        {
            return source;
        }

        var scale = maxEdge / (double)sourceMaxEdge;
        var targetWidth = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
        var targetStride = checked(targetWidth * 4);
        var targetPixels = new byte[checked(targetStride * targetHeight)];

        var xScale = source.PixelWidth / (double)targetWidth;
        var yScale = source.PixelHeight / (double)targetHeight;

        for (var y = 0; y < targetHeight; y++)
        {
            var sourceY = (y + 0.5d) * yScale - 0.5d;
            var y0 = Math.Clamp((int)Math.Floor(sourceY), 0, source.PixelHeight - 1);
            var y1 = Math.Min(y0 + 1, source.PixelHeight - 1);
            var fy = Math.Clamp(sourceY - y0, 0d, 1d);

            for (var x = 0; x < targetWidth; x++)
            {
                var sourceX = (x + 0.5d) * xScale - 0.5d;
                var x0 = Math.Clamp((int)Math.Floor(sourceX), 0, source.PixelWidth - 1);
                var x1 = Math.Min(x0 + 1, source.PixelWidth - 1);
                var fx = Math.Clamp(sourceX - x0, 0d, 1d);

                var targetOffset = checked(y * targetStride + x * 4);
                var p00 = checked(y0 * source.Stride + x0 * 4);
                var p10 = checked(y0 * source.Stride + x1 * 4);
                var p01 = checked(y1 * source.Stride + x0 * 4);
                var p11 = checked(y1 * source.Stride + x1 * 4);

                for (var channel = 0; channel < 4; channel++)
                {
                    var top = source.Pixels[p00 + channel]
                        + (source.Pixels[p10 + channel] - source.Pixels[p00 + channel]) * fx;
                    var bottom = source.Pixels[p01 + channel]
                        + (source.Pixels[p11 + channel] - source.Pixels[p01 + channel]) * fx;
                    var value = top + (bottom - top) * fy;
                    targetPixels[targetOffset + channel] = (byte)Math.Clamp(
                        (int)Math.Round(value),
                        0,
                        255);
                }
            }
        }

        return new DecodedImageFrame(
            targetWidth,
            targetHeight,
            targetStride,
            source.PixelFormat,
            source.AlphaMode,
            source.ColorSpace,
            targetPixels,
            source.IccProfile?.ToArray());
    }
}
