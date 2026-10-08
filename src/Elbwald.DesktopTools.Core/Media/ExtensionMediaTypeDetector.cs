using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.Core.Media;

public sealed class ExtensionMediaTypeDetector
    : IMediaTypeDetector
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp",
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
        };

    public MediaFileType Detect(
        string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            filePath);

        var extension =
            Path.GetExtension(filePath);

        if (string.IsNullOrWhiteSpace(extension))
        {
            return MediaFileType.Unknown;
        }

        return ImageExtensions.Contains(extension)
            ? MediaFileType.Image
            : MediaFileType.Unknown;
    }
}
