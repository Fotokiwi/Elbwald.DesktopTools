namespace Elbwald.DesktopTools.Contracts.Media;

public interface IMediaThumbnailService
{
    Task<MediaThumbnailResult> GetThumbnailAsync(
        MediaFile file,
        int? width = null,
        CancellationToken cancellationToken = default);

    Task<MediaCacheStatistics> GetCacheStatisticsAsync(
        CancellationToken cancellationToken = default);

    Task ClearCacheAsync(
        CancellationToken cancellationToken = default);
}
