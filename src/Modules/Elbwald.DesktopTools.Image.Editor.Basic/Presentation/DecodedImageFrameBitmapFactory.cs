using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Elbwald.DesktopTools.Contracts.Media.Decoding;

namespace Elbwald.DesktopTools.Image.Editor.Basic.Presentation;

internal static class DecodedImageFrameBitmapFactory
{
    public static WriteableBitmap Create(DecodedImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.PixelFormat != ImagePixelFormat.Bgra32)
        {
            throw new NotSupportedException(
                $"Die Editor-Oberfläche erwartet BGRA32, erhalten wurde '{frame.PixelFormat}'.");
        }

        var alphaFormat = frame.AlphaMode switch
        {
            ImageAlphaMode.Premultiplied => AlphaFormat.Premul,
            ImageAlphaMode.Straight => AlphaFormat.Unpremul,
            ImageAlphaMode.Opaque => AlphaFormat.Opaque,
            _ => throw new NotSupportedException(
                $"Nicht unterstützter Alpha-Modus '{frame.AlphaMode}'.")
        };

        var bitmap = new WriteableBitmap(
            new PixelSize(frame.PixelWidth, frame.PixelHeight),
            new Vector(96, 96),
            PixelFormats.Bgra8888,
            alphaFormat);

        try
        {
            using var framebuffer = bitmap.Lock();
            var rowBytes = checked(frame.PixelWidth * 4);

            for (var y = 0; y < frame.PixelHeight; y++)
            {
                var sourceOffset = checked(y * frame.Stride);
                var targetAddress = IntPtr.Add(
                    framebuffer.Address,
                    checked(y * framebuffer.RowBytes));

                Marshal.Copy(
                    frame.Pixels,
                    sourceOffset,
                    targetAddress,
                    rowBytes);
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}
