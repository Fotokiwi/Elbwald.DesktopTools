using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Decoding;
using ImageMagick;

namespace Elbwald.DesktopTools.Image.Formats.Extended;

/// <summary>
/// Full-image decoder for the vertically supported extended formats WebP and TIFF.
/// The source is opened read-only. The decoded in-memory raster is normalized to
/// straight-alpha BGRA32 in sRGB so editor code stays independent of ImageMagick.
/// </summary>
public sealed class MagickExtendedImageDecodeProvider : IImageDecodeProvider
{
    private const int BytesPerPixel = 4;

    private static readonly HashSet<string> SupportedExtensions = new(
        [".webp", ".tif", ".tiff"],
        StringComparer.OrdinalIgnoreCase);

    public string ProviderId => "extended.magicknet.image-decode.webp-tiff";

    public int Priority => 100;

    public bool CanDecode(MediaFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return file.MediaType == MediaFileType.Image
               && SupportedExtensions.Contains(file.Extension);
    }

    public Task<DecodedImageFrame> DecodeAsync(
        ImageDecodeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!CanDecode(request.File))
        {
            throw new NotSupportedException(
                $"Der Extended-Decoder unterstützt '{request.File.Extension}' nicht.");
        }

        if (request.MaxEdge is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.MaxEdge,
                "MaxEdge muss größer als 0 sein oder null bleiben.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => DecodeCore(request, cancellationToken),
            cancellationToken);
    }

    private static DecodedImageFrame DecodeCore(
        ImageDecodeRequest request,
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
        using var image = new MagickImage(source);

        cancellationToken.ThrowIfCancellationRequested();

        if (request.ApplyEmbeddedOrientation)
        {
            image.AutoOrient();
        }

        cancellationToken.ThrowIfCancellationRequested();

        ResizeToMaxEdge(image, request.MaxEdge);

        cancellationToken.ThrowIfCancellationRequested();

        // WebP/TIFF may arrive as grayscale, CMYK or another ImageMagick color
        // space. The editor boundary is deliberately one predictable sRGB raster.
        image.ColorSpace = ColorSpace.sRGB;

        cancellationToken.ThrowIfCancellationRequested();

        var width = checked((int)image.Width);
        var height = checked((int)image.Height);
        var stride = checked(width * BytesPerPixel);
        var expectedLength = checked(stride * height);

        using var pixelCollection = image.GetPixels();
        var pixels = pixelCollection.ToByteArray(
            0,
            0,
            image.Width,
            image.Height,
            "BGRA");

        if (pixels is null || pixels.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"Magick.NET lieferte für '{request.File.FullPath}' einen unerwarteten Pixelpuffer.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new DecodedImageFrame(
            width,
            height,
            stride,
            ImagePixelFormat.Bgra32,
            image.HasAlpha ? ImageAlphaMode.Straight : ImageAlphaMode.Opaque,
            ImageColorSpace.Srgb,
            pixels);
    }

    private static void ResizeToMaxEdge(MagickImage image, int? maxEdge)
    {
        if (maxEdge is not > 0)
        {
            return;
        }

        var currentMaxEdge = Math.Max(image.Width, image.Height);
        if (currentMaxEdge <= (uint)maxEdge.Value)
        {
            return;
        }

        var scale = maxEdge.Value / (double)currentMaxEdge;
        var targetWidth = Math.Max(1u, (uint)Math.Round(image.Width * scale));
        var targetHeight = Math.Max(1u, (uint)Math.Round(image.Height * scale));

        image.Resize(targetWidth, targetHeight);
    }
}
