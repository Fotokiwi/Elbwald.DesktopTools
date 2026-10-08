using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Core.Media;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class MediaAnalyzerServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_CombinesMetadataOrientationAndLargestFiles()
    {
        var first =
            CreateFile(
                "/media/one.jpg",
                ".jpg",
                1_000,
                MediaFileType.Image);

        var second =
            CreateFile(
                "/media/two.arw",
                ".arw",
                5_000,
                MediaFileType.Image);

        var notes =
            CreateFile(
                "/media/notes.txt",
                ".txt",
                500,
                MediaFileType.Unknown);

        var scanner =
            new FakeScanner(
                new MediaScanResult(
                    "/media",
                    new[]
                    {
                        first,
                        second,
                        notes
                    },
                    Array.Empty<MediaScanError>(),
                    directoriesVisited: 2,
                    filesDiscovered: 3,
                    skippedSymbolicLinkDirectories: 0));

        var reader =
            new FakeImageMetadataReader(
                new Dictionary<string, ImageMetadataReadResult>
                {
                    [first.FullPath] =
                        Success(
                            new ImageMetadata(
                                4000,
                                3000,
                                new DateTime(2026, 10, 7, 12, 0, 0),
                                "Canon",
                                "EOS R6",
                                "RF 24-70",
                                6,
                                HasExif: true,
                                HasGps: true,
                                Latitude: 51,
                                Longitude: 13)),

                    [second.FullPath] =
                        Success(
                            new ImageMetadata(
                                6000,
                                4000,
                                CapturedAt: null,
                                CameraMake: "Sony",
                                CameraModel: "ILCE-6400",
                                LensModel: null,
                                Orientation: 1,
                                HasExif: true,
                                HasGps: false,
                                Latitude: null,
                                Longitude: null))
                });

        var result =
            await new MediaAnalyzerService(
                    scanner,
                    reader)
                .AnalyzeAsync("/media");

        Assert.Equal(3, result.TotalFiles);
        Assert.Equal(6_500, result.TotalBytes);
        Assert.Equal(2, result.ImageCount);
        Assert.Equal(2, result.ImagesWithExif);
        Assert.Equal(1, result.ImagesWithGps);
        Assert.Equal(1, result.ImagesWithCaptureDate);
        Assert.Equal(1, result.MissingCaptureDateCount);
        Assert.Equal(2, result.ImagesWithDimensions);

        Assert.Equal(
            1,
            result.PortraitImageCount);

        Assert.Equal(
            1,
            result.LandscapeImageCount);

        Assert.Equal(
            0,
            result.SquareImageCount);

        Assert.Equal(
            second.FullPath,
            result.LargestFiles[0].File.FullPath);

        Assert.Equal(
            1,
            result.CameraCounts["Canon EOS R6"]);

        Assert.Equal(
            1,
            result.CameraCounts["Sony ILCE-6400"]);

        Assert.Equal(
            2,
            reader.ReadPaths.Count);

        Assert.Equal(
            0,
            result.MetadataCacheHits);

        Assert.Equal(
            2,
            result.MetadataCacheMisses);
    }

    [Fact]
    public async Task AnalyzeAsync_SeparatesRealProblemsFromParserInfo()
    {
        var image =
            CreateFile(
                "/media/photo.jpg",
                ".jpg",
                1_000,
                MediaFileType.Image);

        var scanner =
            new FakeScanner(
                new MediaScanResult(
                    "/media",
                    new[] { image },
                    new[]
                    {
                        new MediaScanError(
                            "/media/private",
                            "Verzeichnis lesen",
                            MediaScanErrorKind.AccessDenied,
                            "Zugriff verweigert.")
                    },
                    directoriesVisited: 1,
                    filesDiscovered: 1,
                    skippedSymbolicLinkDirectories: 0));

        var reader =
            new FakeImageMetadataReader(
                new Dictionary<string, ImageMetadataReadResult>
                {
                    [image.FullPath] =
                        new ImageMetadataReadResult(
                            ImageMetadataReadState.Success,
                            new ImageMetadata(
                                100,
                                100,
                                null,
                                null,
                                null,
                                null,
                                null,
                                HasExif: true,
                                HasGps: false,
                                null,
                                null),
                            new[]
                            {
                                "Sony Makernote: Illegal TIFF tag pointer offset"
                            })
                });

        var result =
            await new MediaAnalyzerService(
                    scanner,
                    reader)
                .AnalyzeAsync("/media");

        Assert.Equal(
            1,
            result.ProblemCount);

        Assert.Equal(
            1,
            result.InfoCount);

        Assert.Equal(
            0,
            result.WarningCount);

        Assert.Contains(
            result.Issues,
            issue =>
                issue.Severity
                == MediaAnalysisSeverity.Problem);

        Assert.Contains(
            result.Issues,
            issue =>
                issue.Severity
                == MediaAnalysisSeverity.Info);
    }

    [Fact]
    public async Task AnalyzeAsync_MetadataFailure_IsProblemAndKeepsFile()
    {
        var image =
            CreateFile(
                "/media/broken.jpg",
                ".jpg",
                1_000,
                MediaFileType.Image);

        var scanner =
            new FakeScanner(
                new MediaScanResult(
                    "/media",
                    new[] { image },
                    Array.Empty<MediaScanError>(),
                    directoriesVisited: 1,
                    filesDiscovered: 1,
                    skippedSymbolicLinkDirectories: 0));

        var reader =
            new FakeImageMetadataReader(
                new Dictionary<string, ImageMetadataReadResult>
                {
                    [image.FullPath] =
                        new ImageMetadataReadResult(
                            ImageMetadataReadState.UnsupportedFormat,
                            errorMessage:
                                "Ungültige Bilddaten.")
                });

        var result =
            await new MediaAnalyzerService(
                    scanner,
                    reader)
                .AnalyzeAsync("/media");

        Assert.Equal(
            1,
            result.TotalFiles);

        Assert.Equal(
            1,
            result.MetadataErrorCount);

        Assert.Equal(
            1,
            result.ProblemCount);

        Assert.Null(
            result.Files[0].ImageMetadata);
    }

    [Fact]
    public async Task AnalyzeAsync_ForwardsOptionsAndProgress()
    {
        var image =
            CreateFile(
                "/media/photo.jpg",
                ".jpg",
                100,
                MediaFileType.Image);

        var scanner =
            new FakeScanner(
                new MediaScanResult(
                    "/media",
                    new[] { image },
                    Array.Empty<MediaScanError>(),
                    directoriesVisited: 1,
                    filesDiscovered: 1,
                    skippedSymbolicLinkDirectories: 0));

        var reader =
            new FakeImageMetadataReader(
                new Dictionary<string, ImageMetadataReadResult>
                {
                    [image.FullPath] =
                        Success(
                            new ImageMetadata(
                                10,
                                10,
                                null,
                                null,
                                null,
                                null,
                                null,
                                false,
                                false,
                                null,
                                null))
                });

        var progress =
            new CapturingProgress();

        await new MediaAnalyzerService(
                scanner,
                reader)
            .AnalyzeAsync(
                "/media",
                new MediaAnalysisOptions
                {
                    Recursive = false,
                    IncludeUnknownFiles = false
                },
                progress);

        Assert.False(
            scanner.LastOptions!.Recursive);

        Assert.False(
            scanner.LastOptions.IncludeUnknownFiles);

        Assert.Contains(
            progress.Values,
            value =>
                value.Stage
                == MediaAnalysisStage.ReadingMetadata);

        Assert.Equal(
            MediaAnalysisStage.Completed,
            progress.Values[^1].Stage);
    }

    private static MediaFile CreateFile(
        string path,
        string extension,
        long length,
        MediaFileType type)
    {
        return new MediaFile(
            path,
            Path.GetFileName(path),
            extension,
            length,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            type,
            IsSymbolicLink: false);
    }

    private static ImageMetadataReadResult Success(
        ImageMetadata metadata)
    {
        return new ImageMetadataReadResult(
            ImageMetadataReadState.Success,
            metadata);
    }

    private sealed class FakeScanner
        : IMediaScanner
    {
        private readonly MediaScanResult _result;

        public FakeScanner(
            MediaScanResult result)
        {
            _result = result;
        }

        public MediaScanOptions? LastOptions { get; private set; }

        public Task<MediaScanResult> ScanAsync(
            string path,
            MediaScanOptions? options = null,
            IProgress<MediaScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LastOptions = options;

            progress?.Report(
                new MediaScanProgress(
                    _result.DirectoriesVisited,
                    _result.FilesDiscovered,
                    _result.Files.Count,
                    _result.Errors.Count,
                    _result.SkippedSymbolicLinkDirectories));

            return Task.FromResult(
                _result);
        }
    }

    private sealed class FakeImageMetadataReader
        : IImageMetadataReader
    {
        private readonly IReadOnlyDictionary<string, ImageMetadataReadResult>
            _results;

        public FakeImageMetadataReader(
            IReadOnlyDictionary<string, ImageMetadataReadResult> results)
        {
            _results = results;
        }

        public List<string> ReadPaths { get; } = new();

        public Task<ImageMetadataReadResult> ReadAsync(
            MediaFile file,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ReadPaths.Add(
                file.FullPath);

            return Task.FromResult(
                _results[file.FullPath]);
        }
    }

    private sealed class CapturingProgress
        : IProgress<MediaAnalysisProgress>
    {
        public List<MediaAnalysisProgress> Values { get; } = new();

        public void Report(
            MediaAnalysisProgress value)
        {
            Values.Add(value);
        }
    }
}
