using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.MetadataEditing;
using Elbwald.DesktopTools.Contracts.Media.Knowledge;
using Elbwald.DesktopTools.Contracts.MediaIndex.Collections;

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

    Task<IReadOnlyList<MediaIndexLocation>> GetAllLocationsAsync(
        CancellationToken cancellationToken = default);

    Task<int> MarkLocationsMissingAfterControlledRemovalAsync(
        string mediaItemId,
        IReadOnlyList<long> locationIds,
        CancellationToken cancellationToken = default)
        => Task.FromException<int>(new NotSupportedException(
            "Controlled location removal is not supported by this media-index implementation."));

    Task<MediaIndexItem> EnsureItemAsync(
        string sha256,
        long length,
        MediaFileType mediaType,
        CancellationToken cancellationToken = default);

    Task<MediaMetadataSnapshot> GetMetadataAsync(
        string mediaItemId,
        CancellationToken cancellationToken = default);

    Task SaveMetadataBatchAsync(
        IReadOnlyList<MediaMetadataUpdate> updates,
        CancellationToken cancellationToken = default);

    Task<MediaIndexFacts?> GetFactsAsync(
        string mediaItemId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaKnowledgeItem>> GetKnowledgeItemsAsync(
        MediaKnowledgeKind kind,
        string? searchText = null,
        int limit = 24,
        CancellationToken cancellationToken = default);

    Task<MediaKnowledgeItem> EnsureKnowledgeItemAsync(
        MediaKnowledgeDraft draft,
        CancellationToken cancellationToken = default);

    Task<MediaKnowledgeSnapshot> GetKnowledgeAsync(
        string mediaItemId,
        CancellationToken cancellationToken = default);

    Task<int> AssignKnowledgeItemBatchAsync(
        IReadOnlyList<string> mediaItemIds,
        string knowledgeItemId,
        CancellationToken cancellationToken = default);

    Task<int> RemoveKnowledgeItemBatchAsync(
        IReadOnlyList<string> mediaItemIds,
        string knowledgeItemId,
        CancellationToken cancellationToken = default);

    Task SetRatingBatchAsync(
        IReadOnlyList<string> mediaItemIds,
        int rating,
        CancellationToken cancellationToken = default);

    Task UpdateIdentityAfterControlledRewriteAsync(
        string mediaItemId,
        string expectedOldSha256,
        long expectedOldLength,
        string newSha256,
        long newLength,
        DateTime newLastWriteTimeUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaIndexSearchResult>> SearchAsync(
        MediaIndexSearchQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaIndexFolderFacet>> GetFolderFacetsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaIndexFolderNode>> GetFolderChildrenAsync(
        string endpointId,
        string? parentRelativeFolder = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaTimelineFacet>> GetTimelineYearsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaTimelineFacet>> GetTimelineMonthsAsync(
        int year,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaTimelineFacet>> GetTimelineDaysAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaSmartCollection>> GetSmartCollectionsAsync(
        CancellationToken cancellationToken = default);

    Task<MediaSmartCollection> SaveSmartCollectionAsync(
        MediaSmartCollectionDraft draft,
        CancellationToken cancellationToken = default);

    Task DeleteSmartCollectionAsync(
        string collectionId,
        CancellationToken cancellationToken = default);
}
