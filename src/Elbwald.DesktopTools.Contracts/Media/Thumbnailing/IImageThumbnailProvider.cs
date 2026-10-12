using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.Contracts.Media.Thumbnailing;

/// <summary>
/// Creates a PNG thumbnail for a concrete image file. Providers only decode and
/// scale image data; cache ownership remains with the host thumbnail service.
/// </summary>
public interface IImageThumbnailProvider
{
    string ProviderId { get; }

    int Priority { get; }

    bool CanCreate(MediaFile file);

    Task<MediaThumbnailResult> CreateAsync(
        MediaFile file,
        int targetWidth,
        CancellationToken cancellationToken = default);
}
