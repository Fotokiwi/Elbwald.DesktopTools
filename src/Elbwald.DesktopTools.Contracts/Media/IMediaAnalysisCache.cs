namespace Elbwald.DesktopTools.Contracts.Media;

public interface IMediaAnalysisCache
{
    Task<ImageMetadataReadResult?> TryGetImageMetadataAsync(
        MediaFile file,
        CancellationToken cancellationToken = default);

    Task StoreImageMetadataAsync(
        MediaFile file,
        ImageMetadataReadResult result,
        CancellationToken cancellationToken = default);

    Task<MediaCacheStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default);

    Task ClearAsync(
        CancellationToken cancellationToken = default);
}
