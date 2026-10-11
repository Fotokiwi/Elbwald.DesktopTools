using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Paths;
using Elbwald.DesktopTools.Core.Media;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class JsonMediaAnalysisHistoryStoreTests
{
    [Fact]
    public async Task SaveAndLoadPreservesLastAnalysis()
    {
        using var temp = new TemporaryDirectory();
        var store = new JsonMediaAnalysisHistoryStore(new FakeAppPaths(temp.RootPath));
        var expected = new MediaAnalysisHistoryEntry(
            SourcePath: Path.Combine(temp.RootPath, "Fotos"),
            IncludeSubdirectories: true,
            IsFolder: true,
            CompletedUtc: new DateTimeOffset(2026, 10, 11, 7, 15, 0, TimeSpan.Zero),
            MediaCount: 1234,
            ProblemCount: 7);

        await store.SaveLastAsync(expected);
        var actual = await store.LoadLastAsync();

        Assert.Equal(expected, actual);
    }


    [Fact]
    public async Task SaveAndLoadPreservesResultSnapshot()
    {
        using var temp = new TemporaryDirectory();
        var store = new JsonMediaAnalysisHistoryStore(new FakeAppPaths(temp.RootPath));
        var mediaPath = Path.Combine(temp.RootPath, "Fotos", "bild.jpg");
        var analyzedFile = new MediaAnalyzedFile(
            new MediaFile(
                mediaPath,
                "bild.jpg",
                ".jpg",
                42_000,
                new DateTimeOffset(2026, 10, 10, 18, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 10, 18, 30, 0, TimeSpan.Zero),
                MediaFileType.Image,
                false),
            new ImageMetadata(
                4000,
                3000,
                new DateTime(2026, 10, 10, 18, 15, 0),
                "Elbwald",
                "Testkamera",
                null,
                1,
                true,
                false,
                null,
                null));

        var result = new MediaAnalysisResult(
            Path.Combine(temp.RootPath, "Fotos"),
            new[] { analyzedFile },
            Array.Empty<MediaAnalysisIssue>(),
            directoriesVisited: 1,
            filesDiscovered: 1,
            skippedSymbolicLinkDirectories: 0,
            metadataCacheHits: 1,
            metadataCacheMisses: 0);

        var completedUtc =
            new DateTimeOffset(2026, 10, 11, 7, 15, 0, TimeSpan.Zero);

        await store.SaveLastResultAsync(
            MediaAnalysisResultSnapshot.FromResult(
                result,
                completedUtc));

        var snapshot = await store.LoadLastResultAsync();

        Assert.NotNull(snapshot);
        var restored = snapshot.ToResult();
        Assert.Equal(completedUtc, snapshot.CompletedUtc);
        Assert.Equal(result.RootPath, restored.RootPath);
        Assert.Equal(1, restored.TotalFiles);
        Assert.Equal(42_000, restored.TotalBytes);
        Assert.Equal(1, restored.ImageCount);
        Assert.Equal(1, restored.ImagesWithExif);
        Assert.Equal("Testkamera", restored.Files[0].ImageMetadata?.CameraModel);
        Assert.Equal(mediaPath, restored.Files[0].File.FullPath);
    }

    [Fact]
    public async Task LoadLastResultReturnsNullWhenSnapshotDoesNotExist()
    {
        using var temp = new TemporaryDirectory();
        var store = new JsonMediaAnalysisHistoryStore(new FakeAppPaths(temp.RootPath));

        var actual = await store.LoadLastResultAsync();

        Assert.Null(actual);
    }

    [Fact]
    public async Task LoadReturnsNullWhenHistoryDoesNotExist()
    {
        using var temp = new TemporaryDirectory();
        var store = new JsonMediaAnalysisHistoryStore(new FakeAppPaths(temp.RootPath));

        var actual = await store.LoadLastAsync();

        Assert.Null(actual);
    }

    [Fact]
    public async Task LoadReturnsNullForCorruptHistory()
    {
        using var temp = new TemporaryDirectory();
        var appPaths = new FakeAppPaths(temp.RootPath);
        Directory.CreateDirectory(appPaths.ConfigDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(appPaths.ConfigDirectory, "media-analysis-history.json"),
            "{not-valid-json");

        var store = new JsonMediaAnalysisHistoryStore(appPaths);
        var actual = await store.LoadLastAsync();

        Assert.Null(actual);
    }

    private sealed class FakeAppPaths : IAppPaths
    {
        public FakeAppPaths(string root)
        {
            ApplicationBaseDirectory = root;
            ApplicationDataDirectory = Path.Combine(root, "data");
            ConfigDirectory = Path.Combine(ApplicationDataDirectory, "Config");
            DatabaseDirectory = Path.Combine(ApplicationDataDirectory, "Databases");
            CacheDirectory = Path.Combine(ApplicationDataDirectory, "Cache");
            DiagnosticsDirectory = Path.Combine(ApplicationDataDirectory, "Diagnostics");
            RecoveryDirectory = Path.Combine(ApplicationDataDirectory, "Recovery");
            PortableModeMarkerPath = Path.Combine(root, "portable.flag");
        }

        public bool IsPortable => true;
        public string ApplicationBaseDirectory { get; }
        public string ApplicationDataDirectory { get; }
        public string ConfigDirectory { get; }
        public string DatabaseDirectory { get; }
        public string CacheDirectory { get; }
        public string DiagnosticsDirectory { get; }
        public string RecoveryDirectory { get; }
        public string PortableModeMarkerPath { get; }
    }
}
