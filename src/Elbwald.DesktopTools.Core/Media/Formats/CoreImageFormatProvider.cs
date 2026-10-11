using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Formats;

namespace Elbwald.DesktopTools.Core.Media.Formats;

/// <summary>
/// Compatibility provider for the image extensions recognized by the host
/// before Module API v2. Individual formats can be moved out into extensions
/// without changing scanner/analyzer code once dedicated providers exist.
/// </summary>
public sealed class CoreImageFormatProvider : IMediaFormatProvider
{
    private static readonly string[] Extensions =
    [
        ".jpg",
        ".jpeg",
        ".png",
        ".tif",
        ".tiff",
        ".heic",
        ".heif",
        ".dng",
        ".cr2",
        ".cr3",
        ".nef",
        ".arw",
        ".raf",
        ".orf",
        ".rw2",
        ".pef"
    ];

    public string FormatId => "image.core-compat";

    public MediaFileType MediaType => MediaFileType.Image;

    public IReadOnlyCollection<string> FileExtensions => Extensions;
}
