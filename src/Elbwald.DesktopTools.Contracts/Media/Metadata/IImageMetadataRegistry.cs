using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.Contracts.Media.Metadata;

/// <summary>
/// Central read-only registry over built-in and extension-provided image
/// metadata readers.
/// </summary>
public interface IImageMetadataRegistry
{
    IReadOnlyList<IImageMetadataProvider> Providers { get; }

    IReadOnlyList<IImageMetadataProvider> FindReaders(MediaFile file);
}
