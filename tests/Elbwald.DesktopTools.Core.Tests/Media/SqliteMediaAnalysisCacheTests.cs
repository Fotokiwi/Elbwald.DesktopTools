using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Core.Media;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class SqliteMediaAnalysisCacheTests
{
    [Fact]
    public async Task StoreAndReadAsync_RoundTripsSuccessfulMetadata()
    {
        using var directory = new TestDirectory();

        var cache = CreateCache(directory);

        var file =
            CreateFile(
                Path.Combine(
                    directory.RootPath,
                    "photo.jpg"),
                length: 1234,
                lastWriteTicks: 638900000000000000);

        var original =
            new ImageMetadataReadResult(
                ImageMetadataReadState.Success,
                new ImageMetadata(
                    6000,
                    4000,
                    new DateTime(
                        2026,
                        8,
                        17,
                        14,
                        30,
                        0,
                        DateTimeKind.Unspecified),
                    "Sony",
                    "ILCE-6400",
                    "E 18-135mm",
                    1,
                    HasExif: true,
                    HasGps: true,
                    Latitude: 51.05,
                    Longitude: 13.73)
                {
                    DateTimeOriginal =
                        new DateTime(
                            2026,
                            8,
                            17,
                            14,
                            30,
                            0,
                            DateTimeKind.Unspecified),
                    DateTimeDigitized =
                        new DateTime(
                            2026,
                            8,
                            17,
                            14,
                            30,
                            1,
                            DateTimeKind.Unspecified),
                    DateTimeModified =
                        new DateTime(
                            2026,
                            9,
                            1,
                            10,
                            0,
                            0,
                            DateTimeKind.Unspecified),
                    GpsDateTimeUtc =
                        new DateTime(
                            2026,
                            8,
                            17,
                            12,
                            30,
                            0,
                            DateTimeKind.Utc)
                },
                new[]
                {
                    "Parser-Hinweis"
                });

        await cache.StoreImageMetadataAsync(
            file,
            original);

        var cached =
            await cache.TryGetImageMetadataAsync(
                file);

        Assert.NotNull(cached);
        Assert.True(cached!.IsSuccessful);
        Assert.True(cached.IsFromCache);
        Assert.Equal(6000, cached.Metadata!.PixelWidth);
        Assert.Equal("ILCE-6400", cached.Metadata.CameraModel);
        Assert.Equal(51.05, cached.Metadata.Latitude);
        Assert.Equal(
            new DateTime(
                2026,
                8,
                17,
                14,
                30,
                0,
                DateTimeKind.Unspecified),
            cached.Metadata.DateTimeOriginal);
        Assert.Equal(
            new DateTime(
                2026,
                8,
                17,
                12,
                30,
                0,
                DateTimeKind.Utc),
            cached.Metadata.GpsDateTimeUtc);
        Assert.Equal(
            "Parser-Hinweis",
            Assert.Single(cached.Warnings));
    }

    [Fact]
    public async Task TryGetAsync_ChangedLength_IsCacheMiss()
    {
        using var directory = new TestDirectory();

        var cache = CreateCache(directory);

        var original =
            CreateFile(
                Path.Combine(
                    directory.RootPath,
                    "photo.jpg"),
                length: 100,
                lastWriteTicks: 638900000000000000);

        await cache.StoreImageMetadataAsync(
            original,
            Success());

        var changed =
            original with
            {
                Length = 101
            };

        Assert.Null(
            await cache.TryGetImageMetadataAsync(
                changed));
    }

    [Fact]
    public async Task TryGetAsync_ChangedLastWriteTime_IsCacheMiss()
    {
        using var directory = new TestDirectory();

        var cache = CreateCache(directory);

        var original =
            CreateFile(
                Path.Combine(
                    directory.RootPath,
                    "photo.jpg"),
                length: 100,
                lastWriteTicks: 638900000000000000);

        await cache.StoreImageMetadataAsync(
            original,
            Success());

        var changed =
            original with
            {
                LastWriteTimeUtc =
                    new DateTimeOffset(
                        new DateTime(
                            638900000000000100,
                            DateTimeKind.Utc))
            };

        Assert.Null(
            await cache.TryGetImageMetadataAsync(
                changed));
    }

    [Fact]
    public async Task GetStatisticsAsync_ReportsEntriesAndDatabaseSize()
    {
        using var directory = new TestDirectory();

        var cache = CreateCache(directory);

        var file =
            CreateFile(
                Path.Combine(
                    directory.RootPath,
                    "photo.jpg"),
                100,
                638900000000000000);

        await cache.StoreImageMetadataAsync(
            file,
            Success());

        var statistics =
            await cache.GetStatisticsAsync();

        Assert.True(
            statistics.IsAvailable);

        Assert.Equal(
            1,
            statistics.EntryCount);

        Assert.True(
            statistics.SizeBytes > 0);

        Assert.EndsWith(
            "media-analysis.db",
            statistics.Location);
    }

    [Fact]
    public async Task ClearAsync_RemovesCachedEntries()
    {
        using var directory = new TestDirectory();

        var cache = CreateCache(directory);

        var file =
            CreateFile(
                Path.Combine(
                    directory.RootPath,
                    "photo.jpg"),
                100,
                638900000000000000);

        await cache.StoreImageMetadataAsync(
            file,
            Success());

        await cache.ClearAsync();

        Assert.Null(
            await cache.TryGetImageMetadataAsync(
                file));
    }

    private static SqliteMediaAnalysisCache CreateCache(
        TestDirectory directory)
    {
        return new SqliteMediaAnalysisCache(
            new MediaAnalysisCacheOptions
            {
                DatabasePath =
                    Path.Combine(
                        directory.RootPath,
                        "cache",
                        "media-analysis.db"),
                AnalysisVersion = 1
            });
    }

    private static MediaFile CreateFile(
        string path,
        long length,
        long lastWriteTicks)
    {
        return new MediaFile(
            path,
            Path.GetFileName(path),
            ".jpg",
            length,
            DateTimeOffset.UtcNow,
            new DateTimeOffset(
                new DateTime(
                    lastWriteTicks,
                    DateTimeKind.Utc)),
            MediaFileType.Image,
            IsSymbolicLink: false);
    }

    private static ImageMetadataReadResult Success()
    {
        return new ImageMetadataReadResult(
            ImageMetadataReadState.Success,
            new ImageMetadata(
                100,
                100,
                null,
                null,
                null,
                null,
                null,
                HasExif: false,
                HasGps: false,
                null,
                null));
    }

    private sealed class TestDirectory
        : IDisposable
    {
        public TestDirectory()
        {
            RootPath =
                Path.Combine(
                    Path.GetTempPath(),
                    "Elbwald.DesktopTools.Tests",
                    Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(
                    RootPath,
                    recursive: true);
            }
        }
    }
}
