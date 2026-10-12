using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Formats;

namespace Elbwald.DesktopTools.Core.Media.Formats;

/// <summary>
/// Host-owned compatibility provider. JPEG and PNG are intentional base formats.
/// RAW extensions remain here temporarily until the dedicated RAW package is
/// introduced; non-default raster/container formats belong to extensions.
/// </summary>
public sealed class CoreImageFormatProvider : IMediaFormatProvider
{
    private static readonly string[] Extensions =
    [
        ".jpg",
        ".jpeg",
        ".png",

        // Temporary compatibility bridge. These move into the dedicated RAW
        // package in a later patch so existing RAW detection is not regressed.
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
