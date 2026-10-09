using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Contracts.MediaIndex;

public interface IMediaIndexService
{
    Task<MediaIndexRunResult> IndexAsync(
        StorageSettings settings,
        CancellationToken cancellationToken = default);

    Task<MediaIndexSummary> GetSummaryAsync(
        CancellationToken cancellationToken = default);

    Task<MediaIndexItem?> GetItemAsync(
        string mediaItemId,
        CancellationToken cancellationToken = default);

    Task<MediaIndexItem?> FindByHashAsync(
        string sha256,
        long length,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaIndexLocation>> GetLocationsAsync(
        string mediaItemId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaIndexSearchResult>> SearchAsync(
        MediaIndexSearchQuery query,
        CancellationToken cancellationToken = default);
}
