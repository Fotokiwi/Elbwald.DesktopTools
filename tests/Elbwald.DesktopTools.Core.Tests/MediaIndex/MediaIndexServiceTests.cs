using System.Security.Cryptography;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.Core.Media;
using Elbwald.DesktopTools.Core.MediaIndex;

namespace Elbwald.DesktopTools.Core.Tests.MediaIndex;

public sealed class MediaIndexServiceTests
{
    [Fact]
    public async Task IdenticalContentOnTwoEndpointsUsesOneMediaItemAndTwoLocations()
    {
        using var fixture = new TempFixture();
        var firstRoot = fixture.CreateDirectory("library");
        var secondRoot = fixture.CreateDirectory("backup");
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await File.WriteAllBytesAsync(Path.Combine(firstRoot, "photo.jpg"), payload);
        await File.WriteAllBytesAsync(Path.Combine(secondRoot, "copy.jpg"), payload);

        var service = CreateService(fixture.DatabasePath);
        var settings = new StorageSettings(new[]
        {
            Endpoint("library", StorageEndpointKind.MediaLibrary, "VOL-A", firstRoot),
            Endpoint("backup", StorageEndpointKind.Backup, "VOL-B", secondRoot)
        });

        var result = await service.IndexAsync(settings);
        var summary = await service.GetSummaryAsync();
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        var item = await service.FindByHashAsync(hash, payload.Length);
        var locations = await service.GetLocationsAsync(item!.Id);

        Assert.False(result.CompletedWithIssues);
        Assert.Equal(1, summary.ItemCount);
        Assert.Equal(2, summary.PresentLocationCount);
        Assert.Equal(2, locations.Count(location => location.IsPresent));
        Assert.Contains(locations, location => location.EndpointId == "library");
        Assert.Contains(locations, location => location.EndpointId == "backup");
    }

    [Fact]
    public async Task MoveWithinEndpointKeepsMediaIdAndPreservesPreviousLocation()
    {
        using var fixture = new TempFixture();
        var root = fixture.CreateDirectory("library");
        var oldPath = Path.Combine(root, "old.jpg");
        var newPath = Path.Combine(root, "nested", "new.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        var payload = new byte[] { 9, 8, 7, 6, 5, 4 };
        await File.WriteAllBytesAsync(oldPath, payload);

        var service = CreateService(fixture.DatabasePath);
        var settings = new StorageSettings(new[]
        {
            Endpoint("library", StorageEndpointKind.MediaLibrary, "VOL-A", root)
        });

        await service.IndexAsync(settings);
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        var before = await service.FindByHashAsync(hash, payload.Length);

        File.Move(oldPath, newPath);
        var secondRun = await service.IndexAsync(settings);
        var after = await service.FindByHashAsync(hash, payload.Length);
        var locations = await service.GetLocationsAsync(after!.Id);

        Assert.Equal(before!.Id, after.Id);
        Assert.Equal(1, locations.Count(location => location.IsPresent));
        Assert.Equal(1, locations.Count(location => !location.IsPresent));
        Assert.Contains(locations, location => location.RelativePath == "old.jpg" && !location.IsPresent);
        Assert.Contains(locations, location => location.RelativePath == "nested/new.jpg" && location.IsPresent);
        Assert.Equal(1, secondRun.LocationsMarkedMissing);
        Assert.Equal(1, secondRun.RelocationCount);
        var relocation = Assert.Single(secondRun.Relocations);
        Assert.Equal("library", relocation.PreviousEndpointId);
        Assert.Equal("old.jpg", relocation.PreviousRelativePath);
        Assert.Equal("library", relocation.CurrentEndpointId);
        Assert.Equal("nested/new.jpg", relocation.CurrentRelativePath);
    }

    [Fact]
    public async Task AmbiguousDuplicateMovesAreNotClassifiedAsRelocations()
    {
        using var fixture = new TempFixture();
        var root = fixture.CreateDirectory("library");
        var firstOld = Path.Combine(root, "old-a.jpg");
        var secondOld = Path.Combine(root, "old-b.jpg");
        var firstNew = Path.Combine(root, "new-a.jpg");
        var secondNew = Path.Combine(root, "new-b.jpg");
        var payload = new byte[] { 5, 5, 5, 5, 5 };
        await File.WriteAllBytesAsync(firstOld, payload);
        await File.WriteAllBytesAsync(secondOld, payload);

        var service = CreateService(fixture.DatabasePath);
        var settings = new StorageSettings(new[]
        {
            Endpoint("library", StorageEndpointKind.MediaLibrary, "VOL-A", root)
        });

        await service.IndexAsync(settings);
        File.Move(firstOld, firstNew);
        File.Move(secondOld, secondNew);

        var secondRun = await service.IndexAsync(settings);

        Assert.Equal(2, secondRun.NewLocations);
        Assert.Equal(2, secondRun.LocationsMarkedMissing);
        Assert.Equal(0, secondRun.RelocationCount);
        Assert.Empty(secondRun.Relocations);
    }

    [Fact]
    public async Task SearchFindsMediaByCurrentAndHistoricalPath()
    {
        using var fixture = new TempFixture();
        var root = fixture.CreateDirectory("library");
        var oldPath = Path.Combine(root, "urlaub", "see.jpg");
        var newPath = Path.Combine(root, "2026", "urlaub", "see.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        await File.WriteAllBytesAsync(oldPath, new byte[] { 7, 7, 1, 2, 3 });

        var service = CreateService(fixture.DatabasePath);
        var settings = new StorageSettings(new[]
        {
            Endpoint("library", StorageEndpointKind.MediaLibrary, "VOL-A", root)
        });

        await service.IndexAsync(settings);
        File.Move(oldPath, newPath);
        await service.IndexAsync(settings);

        var current = await service.SearchAsync(new MediaIndexSearchQuery("2026/urlaub", PresentOnly: true));
        var historical = await service.SearchAsync(new MediaIndexSearchQuery("urlaub/see.jpg", HistoricalOnly: true));

        var currentResult = Assert.Single(current);
        var historicalResult = Assert.Single(historical);
        Assert.Equal(currentResult.Item.Id, historicalResult.Item.Id);
        Assert.Equal(1, currentResult.PresentLocationCount);
        Assert.Equal(1, currentResult.HistoricalLocationCount);
    }

    [Fact]
    public async Task IncompleteScanDoesNotMarkPreviouslyKnownLocationsMissing()
    {
        using var fixture = new TempFixture();
        var root = fixture.CreateDirectory("library");
        var filePath = Path.Combine(root, "photo.jpg");
        await File.WriteAllBytesAsync(filePath, new byte[] { 4, 2, 4, 2 });

        var normalScanner = new FileSystemMediaScanner(new ExtensionMediaTypeDetector());
        var resolver = new TestStorageResolver();
        var service = new MediaIndexService(
            normalScanner,
            resolver,
            new MediaIndexOptions { DatabasePath = fixture.DatabasePath });
        var settings = new StorageSettings(new[]
        {
            Endpoint("library", StorageEndpointKind.MediaLibrary, "VOL-A", root)
        });

        await service.IndexAsync(settings);
        File.Delete(filePath);

        var failingScanner = new FixedScanner(new MediaScanResult(
            root,
            Array.Empty<MediaFile>(),
            new[]
            {
                new MediaScanError(root, "Verzeichnis lesen", MediaScanErrorKind.IoError, "simulierter Lesefehler")
            },
            directoriesVisited: 1,
            filesDiscovered: 0,
            skippedSymbolicLinkDirectories: 0));

        var failingService = new MediaIndexService(
            failingScanner,
            resolver,
            new MediaIndexOptions { DatabasePath = fixture.DatabasePath });

        var result = await failingService.IndexAsync(settings);
        var summary = await failingService.GetSummaryAsync();

        Assert.True(result.CompletedWithIssues);
        Assert.Equal(0, result.LocationsMarkedMissing);
        Assert.Equal(1, summary.PresentLocationCount);
        Assert.Equal(0, summary.HistoricalLocationCount);
    }

    private static MediaIndexService CreateService(string databasePath) =>
        new(
            new FileSystemMediaScanner(new ExtensionMediaTypeDetector()),
            new TestStorageResolver(),
            new MediaIndexOptions { DatabasePath = databasePath });

    private static StorageEndpoint Endpoint(
        string id,
        StorageEndpointKind kind,
        string volumeId,
        string absolutePath) =>
        new(
            id,
            id,
            kind,
            new StorageLocation(
                volumeId,
                string.Empty,
                absolutePath,
                absolutePath));

    private sealed class TestStorageResolver : IStorageLocationResolver
    {
        public StorageLocation Capture(string absolutePath) =>
            throw new NotSupportedException();

        public StorageLocationResolution Resolve(StorageLocation location)
        {
            var path = location.LastKnownAbsolutePath;
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)
                ? new StorageLocationResolution(
                    StorageLocationStatus.Available,
                    path,
                    new StorageVolumeInfo(location.VolumeId, path, "test", null, null),
                    "verfügbar")
                : new StorageLocationResolution(
                    StorageLocationStatus.VolumeUnavailable,
                    null,
                    null,
                    "nicht verfügbar");
        }
    }

    private sealed class FixedScanner : IMediaScanner
    {
        private readonly MediaScanResult _result;

        public FixedScanner(MediaScanResult result)
        {
            _result = result;
        }

        public Task<MediaScanResult> ScanAsync(
            string path,
            MediaScanOptions? options = null,
            IProgress<MediaScanProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class TempFixture : IDisposable
    {
        public TempFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "elbwald-index-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            DatabasePath = Path.Combine(Root, "db", "media-index.db");
        }

        public string Root { get; }

        public string DatabasePath { get; }

        public string CreateDirectory(string name)
        {
            var path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // Test cleanup is best effort only.
            }
        }
    }
}
