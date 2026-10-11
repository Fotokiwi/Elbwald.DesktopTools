using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Formats;

namespace Elbwald.DesktopTools.Image.WebP;

/// <summary>
/// Declares WebP as an image format. Decoding, metadata, thumbnails and export
/// remain separate capabilities and are intentionally not claimed here.
/// </summary>
public sealed class WebpMediaFormatProvider : IMediaFormatProvider
{
    public string FormatId => "image.webp";

    public MediaFileType MediaType => MediaFileType.Image;

    public IReadOnlyCollection<string> FileExtensions { get; } = [".webp"];
}
