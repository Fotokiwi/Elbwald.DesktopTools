using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.Contracts.Media.Thumbnailing;

/// <summary>
/// Central registry over built-in and active extension thumbnail providers.
/// </summary>
public interface IImageThumbnailRegistry
{
    event EventHandler? ProvidersChanged;

    IReadOnlyList<IImageThumbnailProvider> Providers { get; }

    IReadOnlyList<IImageThumbnailProvider> FindProviders(MediaFile file);
}
