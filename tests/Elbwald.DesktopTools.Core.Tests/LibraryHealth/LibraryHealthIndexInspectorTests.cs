using Elbwald.DesktopTools.Contracts.LibraryHealth;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Knowledge;
using Elbwald.DesktopTools.Contracts.Media.MetadataEditing;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.MediaIndex.Collections;
using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.Core.LibraryHealth;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.LibraryHealth;

public sealed class LibraryHealthIndexInspectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "elbwald-health-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InspectSeparatesReachableOfflineMissingAndUnconfiguredMedia()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "online"));
        File.WriteAllText(Path.Combine(_root, "online", "exists.jpg"), "test");

        var items = new[]
        {
            Item("reachable"),
            Item("offline"),
            Item("missing"),
            Item("unconfigured")
        };

        var now = DateTimeOffset.UtcNow;
        var locations = new[]
        {
            Location(1, "reachable", "online", "exists.jpg", now),
            Location(2, "offline", "offline", "archive.jpg", now),
            Location(3, "missing", "online", "gone.jpg", now),
            Location(4, "unconfigured", "removed-endpoint", "legacy.jpg", now)
        };

        var service = new FakeMediaIndexService(items, locations);
        var inspector = new LibraryHealthIndexInspector(service);
        var states = new[]
        {
            new LibraryHealthIndexEndpointState(
                "online",
                true,
                StorageLocationStatus.Available,
                Path.Combine(_root, "online")),
            new LibraryHealthIndexEndpointState(
                "offline",
                true,
                StorageLocationStatus.VolumeUnavailable,
                null)
        };

        var result = await inspector.InspectAsync(states);

        Assert.Equal(4, result.IndexedMediaCount);
        Assert.Equal(1, result.PresentMediaCount);
        Assert.Equal(1, result.OfflineMediaCount);
        Assert.Equal(1, result.MissingMediaCount);
        Assert.Equal(1, result.UnconfiguredMediaCount);
        Assert.Equal(1, result.StaleLocationCount);
    }

    [Fact]
    public async Task InspectTreatsDisabledEndpointAsOfflineInsteadOfMissing()
    {
        var item = Item("disabled");
        var location = Location(1, item.Id, "disabled-endpoint", "photo.jpg", DateTimeOffset.UtcNow);
        var inspector = new LibraryHealthIndexInspector(
            new FakeMediaIndexService(new[] { item }, new[] { location }));

        var result = await inspector.InspectAsync(new[]
        {
            new LibraryHealthIndexEndpointState(
                "disabled-endpoint",
                false,
                StorageLocationStatus.Available,
                _root)
        });

        Assert.Equal(1, result.OfflineMediaCount);
        Assert.Equal(0, result.MissingMediaCount);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best effort test cleanup.
        }
    }

    private static MediaIndexItem Item(string id) =>
        new(
            id,
            new string('A', 64),
            4,
            MediaFileType.Image,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private static MediaIndexLocation Location(
        long id,
        string mediaItemId,
        string endpointId,
        string relativePath,
        DateTimeOffset now) =>
        new(
            id,
            mediaItemId,
            endpointId,
            StorageEndpointKind.MediaLibrary,
            endpointId + "-volume",
            relativePath,
            4,
            now,
            true,
            now,
            now);

    private sealed class FakeMediaIndexService : IMediaIndexService
    {
        private readonly IReadOnlyList<MediaIndexItem> _items;
        private readonly IReadOnlyList<MediaIndexLocation> _locations;

        public FakeMediaIndexService(
            IReadOnlyList<MediaIndexItem> items,
            IReadOnlyList<MediaIndexLocation> locations)
        {
            _items = items;
            _locations = locations;
        }

        public Task<MediaIndexRunResult> IndexAsync(StorageSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MediaIndexSummary> GetSummaryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaIndexSummary(
                ItemCount: _items.Count,
                PresentLocationCount: _locations.Count(location => location.IsPresent),
                HistoricalLocationCount: _locations.Count(location => !location.IsPresent),
                LastIndexedUtc: DateTimeOffset.UtcNow));

        public Task<MediaIndexItem?> GetItemAsync(string mediaItemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(item => item.Id == mediaItemId));

        public Task<MediaIndexItem?> FindByHashAsync(string sha256, long length, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<MediaIndexLocation>> GetLocationsAsync(string mediaItemId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaIndexLocation>>(_locations.Where(location => location.MediaItemId == mediaItemId).ToArray());

        public Task<IReadOnlyList<MediaIndexLocation>> GetAllLocationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_locations);

        public Task<MediaIndexItem> EnsureItemAsync(string sha256, long length, MediaFileType mediaType, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MediaMetadataSnapshot> GetMetadataAsync(string mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveMetadataBatchAsync(IReadOnlyList<MediaMetadataUpdate> updates, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MediaIndexFacts?> GetFactsAsync(string mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<MediaKnowledgeItem>> GetKnowledgeItemsAsync(MediaKnowledgeKind kind, string? searchText = null, int limit = 24, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MediaKnowledgeItem> EnsureKnowledgeItemAsync(MediaKnowledgeDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MediaKnowledgeSnapshot> GetKnowledgeAsync(string mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> AssignKnowledgeItemBatchAsync(IReadOnlyList<string> mediaItemIds, string knowledgeItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> RemoveKnowledgeItemBatchAsync(IReadOnlyList<string> mediaItemIds, string knowledgeItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetRatingBatchAsync(IReadOnlyList<string> mediaItemIds, int rating, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UpdateIdentityAfterControlledRewriteAsync(string mediaItemId, string expectedOldSha256, long expectedOldLength, string newSha256, long newLength, DateTime newLastWriteTimeUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<MediaIndexSearchResult>> SearchAsync(MediaIndexSearchQuery query, CancellationToken cancellationToken = default)
        {
            var page = _items
                .Skip(Math.Max(0, query.Offset))
                .Take(Math.Clamp(query.Limit, 1, 500))
                .Select(item => new MediaIndexSearchResult(
                    item,
                    null,
                    null,
                    false,
                    0,
                    0,
                    CaptureDate: DateTime.Today))
                .ToArray();

            return Task.FromResult<IReadOnlyList<MediaIndexSearchResult>>(page);
        }

        public Task<IReadOnlyList<MediaIndexFolderFacet>> GetFolderFacetsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<MediaIndexFolderNode>> GetFolderChildrenAsync(string endpointId, string? parentRelativeFolder = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<MediaTimelineFacet>> GetTimelineYearsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<MediaTimelineFacet>> GetTimelineMonthsAsync(int year, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<MediaTimelineFacet>> GetTimelineDaysAsync(int year, int month, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<MediaSmartCollection>> GetSmartCollectionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MediaSmartCollection> SaveSmartCollectionAsync(MediaSmartCollectionDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteSmartCollectionAsync(string collectionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
