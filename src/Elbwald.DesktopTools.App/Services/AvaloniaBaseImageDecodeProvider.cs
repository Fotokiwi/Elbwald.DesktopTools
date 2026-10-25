using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Decoding;

namespace Elbwald.DesktopTools.App.Services;

/// <summary>
/// Built-in full-image decoder for Elbwald's base formats JPEG and PNG.
/// The source file is opened read-only and decoded into a backend-neutral BGRA32 frame.
/// </summary>
public sealed class AvaloniaBaseImageDecodeProvider : IImageDecodeProvider
{
    private const int BytesPerPixel = 4;

    private static readonly HashSet<string> SupportedExtensions = new(
        [".jpg", ".jpeg", ".png"],
        StringComparer.OrdinalIgnoreCase);

    private readonly IImageMetadataReader _metadataReader;

    public AvaloniaBaseImageDecodeProvider(IImageMetadataReader metadataReader)
    {
        ArgumentNullException.ThrowIfNull(metadataReader);
        _metadataReader = metadataReader;
    }

    public string ProviderId => "elbwald.core.image-decode.jpeg-png";

    public int Priority => 0;

    public bool CanDecode(MediaFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return file.MediaType == MediaFileType.Image
               && SupportedExtensions.Contains(file.Extension);
    }

    public async Task<DecodedImageFrame> DecodeAsync(
        ImageDecodeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!CanDecode(request.File))
        {
            throw new NotSupportedException(
                $"Der Basisdecoder unterstützt '{request.File.Extension}' nicht.");
        }

        if (request.MaxEdge is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.MaxEdge,
                "MaxEdge muss größer als 0 sein oder null bleiben.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        ImageMetadata? metadata = null;
        if (request.ApplyEmbeddedOrientation || request.MaxEdge.HasValue)
        {
            var metadataResult = await _metadataReader
                .ReadAsync(request.File, cancellationToken)
                .ConfigureAwait(false);

            if (metadataResult.IsSuccessful)
            {
                metadata = metadataResult.Metadata;
            }
        }

        return await Task.Run(
                () => DecodeCore(request, metadata, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static DecodedImageFrame DecodeCore(
        ImageDecodeRequest request,
        ImageMetadata? metadata,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var source = new FileStream(
            request.File.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.SequentialScan);

        using var bitmap = DecodeBitmap(
            source,
            request.MaxEdge,
            metadata?.PixelWidth,
            metadata?.PixelHeight);

        cancellationToken.ThrowIfCancellationRequested();

        var pixels = CopyToStraightBgra32(bitmap, cancellationToken);
        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        var stride = checked(width * BytesPerPixel);

        var orientation = request.ApplyEmbeddedOrientation
            ? metadata?.Orientation
            : null;

        if (orientation is >= 2 and <= 8)
        {
            (pixels, width, height) = ApplyExifOrientation(
                pixels,
                width,
                height,
                orientation.Value,
                cancellationToken);
            stride = checked(width * BytesPerPixel);
        }

        var alphaMode = bitmap.AlphaFormat == AlphaFormat.Opaque
            ? ImageAlphaMode.Opaque
            : ImageAlphaMode.Straight;

        return new DecodedImageFrame(
            width,
            height,
            stride,
            ImagePixelFormat.Bgra32,
            alphaMode,
            ImageColorSpace.Unknown,
            pixels);
    }

    private static Bitmap DecodeBitmap(
        Stream source,
        int? maxEdge,
        int? metadataWidth,
        int? metadataHeight)
    {
        if (maxEdge is > 0
            && metadataWidth is > 0
            && metadataHeight is > 0
            && Math.Max(metadataWidth.Value, metadataHeight.Value) > maxEdge.Value)
        {
            return metadataWidth.Value >= metadataHeight.Value
                ? Bitmap.DecodeToWidth(
                    source,
                    maxEdge.Value,
                    BitmapInterpolationMode.HighQuality)
                : Bitmap.DecodeToHeight(
                    source,
                    maxEdge.Value,
                    BitmapInterpolationMode.HighQuality);
        }

        var bitmap = new Bitmap(source);

        if (maxEdge is not > 0
            || Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height) <= maxEdge.Value)
        {
            return bitmap;
        }

        var scale = maxEdge.Value
                    / (double)Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        var targetSize = new PixelSize(
            Math.Max(1, (int)Math.Round(bitmap.PixelSize.Width * scale)),
            Math.Max(1, (int)Math.Round(bitmap.PixelSize.Height * scale)));

        var scaled = bitmap.CreateScaledBitmap(
            targetSize,
            BitmapInterpolationMode.HighQuality);
        bitmap.Dispose();
        return scaled;
    }

    private static byte[] CopyToStraightBgra32(
        Bitmap bitmap,
        CancellationToken cancellationToken)
    {
        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        var targetStride = checked(width * BytesPerPixel);
        var pixels = new byte[checked(targetStride * height)];

        using var normalized = new WriteableBitmap(
            bitmap.PixelSize,
            bitmap.Dpi,
            PixelFormats.Bgra8888,
            AlphaFormat.Unpremul);
        using var framebuffer = normalized.Lock();

        bitmap.CopyPixels(framebuffer);

        if (framebuffer.Format != PixelFormats.Bgra8888)
        {
            throw new NotSupportedException(
                $"Avalonia lieferte unerwartetes Pixelformat '{framebuffer.Format}'.");
        }

        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceAddress = IntPtr.Add(
                framebuffer.Address,
                checked(y * framebuffer.RowBytes));
            Marshal.Copy(
                sourceAddress,
                pixels,
                checked(y * targetStride),
                targetStride);
        }

        return pixels;
    }

    private static (byte[] Pixels, int Width, int Height) ApplyExifOrientation(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        int orientation,
        CancellationToken cancellationToken)
    {
        if (orientation == 1)
        {
            return (source, sourceWidth, sourceHeight);
        }

        var swapsAxes = orientation is 5 or 6 or 7 or 8;
        var targetWidth = swapsAxes ? sourceHeight : sourceWidth;
        var targetHeight = swapsAxes ? sourceWidth : sourceHeight;
        var target = new byte[checked(targetWidth * targetHeight * BytesPerPixel)];
        var sourceStride = checked(sourceWidth * BytesPerPixel);
        var targetStride = checked(targetWidth * BytesPerPixel);

        for (var y = 0; y < sourceHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var x = 0; x < sourceWidth; x++)
            {
                var (targetX, targetY) = orientation switch
                {
                    2 => (sourceWidth - 1 - x, y),
                    3 => (sourceWidth - 1 - x, sourceHeight - 1 - y),
                    4 => (x, sourceHeight - 1 - y),
                    5 => (y, x),
                    6 => (sourceHeight - 1 - y, x),
                    7 => (sourceHeight - 1 - y, sourceWidth - 1 - x),
                    8 => (y, sourceWidth - 1 - x),
                    _ => (x, y)
                };

                var sourceOffset = checked(y * sourceStride + x * BytesPerPixel);
                var targetOffset = checked(targetY * targetStride + targetX * BytesPerPixel);

                Buffer.BlockCopy(
                    source,
                    sourceOffset,
                    target,
                    targetOffset,
                    BytesPerPixel);
            }
        }

        return (target, targetWidth, targetHeight);
    }
}
