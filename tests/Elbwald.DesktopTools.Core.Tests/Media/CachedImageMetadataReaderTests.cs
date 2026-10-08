using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Core.Media;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class CachedImageMetadataReaderTests
{
    [Fact]
    public async Task ReadAsync_CachedEntry_DoesNotRequireSourceFile()
    {
        using var directory = new TestDirectory();

        var missingPath =
            Path.Combine(
                directory.RootPath,
                "missing-photo.jpg");

        var file =
            new MediaFile(
                missingPath,
                "missing-photo.jpg",
                ".jpg",
                1234,
                DateTimeOffset.UtcNow,
                new DateTimeOffset(
                    new DateTime(
                        638900000000000000,
                        DateTimeKind.Utc)),
                MediaFileType.Image,
                IsSymbolicLink: false);

        var cache =
            new SqliteMediaAnalysisCache(
                new MediaAnalysisCacheOptions
                {
                    DatabasePath =
                        Path.Combine(
                            directory.RootPath,
                            "cache.db")
                });

        await cache.StoreImageMetadataAsync(
            file,
            new ImageMetadataReadResult(
                ImageMetadataReadState.Success,
                new ImageMetadata(
                    6000,
                    4000,
                    null,
                    "Sony",
                    "ILCE-6400",
                    null,
                    1,
                    HasExif: true,
                    HasGps: false,
                    null,
                    null)));

        var reader =
            new CachedImageMetadataReader(
                new MetadataExtractorImageMetadataReader(),
                cache);

        var result =
            await reader.ReadAsync(file);

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsFromCache);
        Assert.Equal(
            6000,
            result.Metadata!.PixelWidth);

        Assert.False(
            File.Exists(missingPath));
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
