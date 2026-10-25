namespace Elbwald.DesktopTools.Contracts.Media.Decoding;

/// <summary>
/// Fully decoded raster image in a backend-neutral memory representation.
/// Pixel data is owned by this value and is never a reference to the source file.
/// </summary>
public sealed record DecodedImageFrame
{
    public DecodedImageFrame(
        int pixelWidth,
        int pixelHeight,
        int stride,
        ImagePixelFormat pixelFormat,
        ImageAlphaMode alphaMode,
        ImageColorSpace colorSpace,
        byte[] pixels,
        byte[]? iccProfile = null)
    {
        if (pixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        }

        if (pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelHeight));
        }

        if (stride <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stride));
        }

        var bytesPerPixel = pixelFormat switch
        {
            ImagePixelFormat.Rgba32 => 4,
            ImagePixelFormat.Bgra32 => 4,
            ImagePixelFormat.Rgba64 => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat))
        };
        var minimumStride = checked(pixelWidth * bytesPerPixel);
        if (stride < minimumStride)
        {
            throw new ArgumentException(
                "Der Stride ist für Breite und Pixelformat zu klein.",
                nameof(stride));
        }

        ArgumentNullException.ThrowIfNull(pixels);

        var minimumLength = checked(stride * pixelHeight);
        if (pixels.Length < minimumLength)
        {
            throw new ArgumentException(
                "Der Pixelpuffer ist kleiner als Stride × Bildhöhe.",
                nameof(pixels));
        }

        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        Stride = stride;
        PixelFormat = pixelFormat;
        AlphaMode = alphaMode;
        ColorSpace = colorSpace;
        Pixels = pixels;
        IccProfile = iccProfile;
    }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public int Stride { get; }

    public ImagePixelFormat PixelFormat { get; }

    public ImageAlphaMode AlphaMode { get; }

    public ImageColorSpace ColorSpace { get; }

    public byte[] Pixels { get; }

    public byte[]? IccProfile { get; }
}
