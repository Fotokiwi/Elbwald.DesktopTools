using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Formats;

namespace Elbwald.DesktopTools.Image.Formats.Extended;

public sealed class WebpMediaFormatProvider : IMediaFormatProvider
{
    public string FormatId => "image.webp";
    public MediaFileType MediaType => MediaFileType.Image;
    public IReadOnlyCollection<string> FileExtensions { get; } = [".webp"];
}

public sealed class TiffMediaFormatProvider : IMediaFormatProvider
{
    public string FormatId => "image.tiff";
    public MediaFileType MediaType => MediaFileType.Image;
    public IReadOnlyCollection<string> FileExtensions { get; } = [".tif", ".tiff"];
}

public sealed class HeifMediaFormatProvider : IMediaFormatProvider
{
    public string FormatId => "image.heif";
    public MediaFileType MediaType => MediaFileType.Image;
    public IReadOnlyCollection<string> FileExtensions { get; } = [".heic", ".heif"];
}

public sealed class AvifMediaFormatProvider : IMediaFormatProvider
{
    public string FormatId => "image.avif";
    public MediaFileType MediaType => MediaFileType.Image;
    public IReadOnlyCollection<string> FileExtensions { get; } = [".avif"];
}

public sealed class Jpeg2000MediaFormatProvider : IMediaFormatProvider
{
    public string FormatId => "image.jpeg2000";
    public MediaFileType MediaType => MediaFileType.Image;

    // Keep the first public surface conservative. Additional JPEG 2000 family
    // suffixes can be enabled once they have dedicated fixtures/tests.
    public IReadOnlyCollection<string> FileExtensions { get; } = [".jp2", ".j2k"];
}
