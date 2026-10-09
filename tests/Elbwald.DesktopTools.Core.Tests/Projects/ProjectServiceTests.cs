using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Projects;
using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.Core.Projects;

namespace Elbwald.DesktopTools.Core.Tests.Projects;

public sealed class ProjectServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "elbwald-project-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Create_Update_And_Assign_Media_Persists()
    {
        Directory.CreateDirectory(_directory);
        var media = CreateMedia("media-1");
        var index = new FakeMediaIndexService(media);
        var options = new ProjectOptions { DatabasePath = Path.Combine(_directory, "projects.db") };
        var service = new ProjectService(options, index);

        var project = await service.CreateProjectAsync(new ProjectCreateRequest("Urlaub 2026", "Urlaub"));
        var added = await service.AddMediaAsync(project.Id, media.Id);
        var updated = await service.UpdateProjectAsync(project.Id, new ProjectUpdateRequest("Urlaub Ostsee 2026", "Urlaub", Location: "Ostsee"));

        Assert.True(added);
        Assert.Equal("Urlaub Ostsee 2026", updated.Name);
        Assert.Equal("Ostsee", updated.Location);
        var references = await service.GetMediaReferencesAsync(project.Id);
        Assert.Single(references);
        Assert.Equal(media.Id, references[0].MediaItemId);

        var reopened = new ProjectService(options, index);
        var persisted = await reopened.GetProjectAsync(project.Id);
        Assert.NotNull(persisted);
        Assert.Equal("Urlaub Ostsee 2026", persisted!.Name);
        Assert.Single(await reopened.GetMediaReferencesAsync(project.Id));
    }

    [Fact]
    public async Task AddMedia_Rejects_Unknown_MediaId()
    {
        Directory.CreateDirectory(_directory);
        var service = new ProjectService(
            new ProjectOptions { DatabasePath = Path.Combine(_directory, "projects.db") },
            new FakeMediaIndexService());
        var project = await service.CreateProjectAsync(new ProjectCreateRequest("Sammlung", "Sammlung"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddMediaAsync(project.Id, "missing-media"));
        Assert.Empty(await service.GetMediaReferencesAsync(project.Id));
    }

    [Fact]
    public async Task Removing_Project_Reference_Does_Not_Delete_Media_From_Index()
    {
        Directory.CreateDirectory(_directory);
        var media = CreateMedia("media-1");
        var index = new FakeMediaIndexService(media);
        var service = new ProjectService(
            new ProjectOptions { DatabasePath = Path.Combine(_directory, "projects.db") },
            index);
        var project = await service.CreateProjectAsync(new ProjectCreateRequest("Sammlung", "Sammlung"));
        await service.AddMediaAsync(project.Id, media.Id);

        Assert.True(await service.RemoveMediaAsync(project.Id, media.Id));
        Assert.Empty(await service.GetMediaReferencesAsync(project.Id));
        Assert.NotNull(await index.GetItemAsync(media.Id));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch { }
    }

    private static MediaIndexItem CreateMedia(string id) => new(
        id,
        new string('A', 64),
        1234,
        MediaFileType.Image,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private sealed class FakeMediaIndexService(params MediaIndexItem[] items) : IMediaIndexService
    {
        private readonly Dictionary<string, MediaIndexItem> _items = items.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

        public Task<MediaIndexRunResult> IndexAsync(StorageSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaIndexSummary> GetSummaryAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaIndexItem?> GetItemAsync(string mediaItemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.TryGetValue(mediaItemId, out var item) ? item : null);
        public Task<MediaIndexItem?> FindByHashAsync(string sha256, long length, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.Values.FirstOrDefault(item => item.Sha256 == sha256 && item.Length == length));
        public Task<IReadOnlyList<MediaIndexLocation>> GetLocationsAsync(string mediaItemId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaIndexLocation>>(Array.Empty<MediaIndexLocation>());
        public Task<IReadOnlyList<MediaIndexSearchResult>> SearchAsync(MediaIndexSearchQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaIndexSearchResult>>(Array.Empty<MediaIndexSearchResult>());
    }
}
