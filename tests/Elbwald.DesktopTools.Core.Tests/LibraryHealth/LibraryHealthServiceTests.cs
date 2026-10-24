using Elbwald.DesktopTools.Contracts.LibraryHealth;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.Core.LibraryHealth;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.LibraryHealth;

public sealed class LibraryHealthServiceTests
{
    [Fact]
    public async Task ScanProcessesEveryEnabledEndpoint()
    {
        var settings = new StorageSettings(new[]
        {
            Endpoint("one", "volume-one"),
            Endpoint("two", "volume-two"),
            Endpoint("disabled", "volume-three", enabled: false)
        });
        var resolver = new FakeResolver(new Dictionary<string, StorageLocationResolution>
        {
            ["volume-one"] = Available("/one", "volume-one"),
            ["volume-two"] = Available("/two", "volume-two"),
            ["volume-three"] = Available("/three", "volume-three")
        });
        var analyzer = new FakeAnalyzer();
        var indexInspector = new FakeIndexInspector();
        var service = new LibraryHealthService(resolver, analyzer, indexInspector);

        var report = await service.ScanAsync(settings);

        Assert.Equal(2, report.Endpoints.Count);
        Assert.Equal(2, analyzer.Paths.Count);
        Assert.DoesNotContain("/three", analyzer.Paths);
        Assert.Contains(indexInspector.LastEndpointStates, state => state.EndpointId == "disabled" && !state.Enabled);
    }

    [Fact]
    public async Task ScanDoesNotAnalyzeUnavailableEndpointAndTreatsItAsInfo()
    {
        var settings = new StorageSettings(new[] { Endpoint("archive", "offline") });
        var resolver = new FakeResolver(new Dictionary<string, StorageLocationResolution>
        {
            ["offline"] = new(
                StorageLocationStatus.VolumeUnavailable,
                null,
                null,
                "Laufwerk fehlt")
        });
        var analyzer = new FakeAnalyzer();
        var service = new LibraryHealthService(resolver, analyzer, new FakeIndexInspector());

        var report = await service.ScanAsync(settings);

        Assert.Empty(analyzer.Paths);
        var issue = Assert.Single(report.Issues);
        Assert.Equal(LibraryHealthIssueKind.VolumeUnavailable, issue.Kind);
        Assert.Equal(LibraryHealthSeverity.Info, issue.Severity);
    }

    [Fact]
    public async Task ScanTreatsMissingConfiguredFolderAsProblem()
    {
        var settings = new StorageSettings(new[] { Endpoint("archive", "online") });
        var resolver = new FakeResolver(new Dictionary<string, StorageLocationResolution>
        {
            ["online"] = new(
                StorageLocationStatus.PathMissing,
                "/archive/photos",
                new StorageVolumeInfo("online", "/archive"),
                "Unterordner fehlt")
        });
        var analyzer = new FakeAnalyzer();
        var service = new LibraryHealthService(resolver, analyzer, new FakeIndexInspector());

        var report = await service.ScanAsync(settings);

        var issue = Assert.Single(report.Issues);
        Assert.Equal(LibraryHealthIssueKind.PathMissing, issue.Kind);
        Assert.Equal(LibraryHealthSeverity.Problem, issue.Severity);
    }

    [Fact]
    public async Task ScanSeparatesOfflineIndexedMediaFromMissingMedia()
    {
        var settings = new StorageSettings(Array.Empty<StorageEndpoint>());
        var resolver = new FakeResolver(new Dictionary<string, StorageLocationResolution>());
        var analyzer = new FakeAnalyzer();
        var indexInspector = new FakeIndexInspector(
            new LibraryHealthIndexSummary(
                IndexedMediaCount: 120,
                PresentMediaCount: 110,
                OrphanedMediaCount: 5,
                MissingCaptureDateCount: 7,
                LastIndexedUtc: DateTimeOffset.UtcNow)
            {
                OfflineMediaCount = 5,
                MissingMediaCount = 3,
                UnconfiguredMediaCount = 2,
                StaleLocationCount = 4
            });
        var service = new LibraryHealthService(resolver, analyzer, indexInspector);

        var report = await service.ScanAsync(settings);

        Assert.Equal(120, report.IndexSummary.IndexedMediaCount);
        Assert.Equal(5, report.IndexSummary.OfflineMediaCount);
        Assert.Equal(3, report.IndexSummary.MissingMediaCount);
        Assert.Contains(
            report.Issues,
            issue => issue.Kind == LibraryHealthIssueKind.IndexedMediaOffline
                     && issue.Severity == LibraryHealthSeverity.Info);
        Assert.Contains(
            report.Issues,
            issue => issue.Kind == LibraryHealthIssueKind.IndexedMediaMissing
                     && issue.Severity == LibraryHealthSeverity.Problem);
        Assert.Contains(
            report.Issues,
            issue => issue.Kind == LibraryHealthIssueKind.IndexedMediaUnconfigured);
        Assert.Contains(
            report.Issues,
            issue => issue.Kind == LibraryHealthIssueKind.StaleIndexLocation);
    }

    [Fact]
    public async Task ScanReportsMetadataQualityWithoutRequiringCameraOrGps()
    {
        var settings = new StorageSettings(new[] { Endpoint("photos", "volume-one") });
        var resolver = new FakeResolver(new Dictionary<string, StorageLocationResolution>
        {
            ["volume-one"] = Available("/one", "volume-one")
        });

        var now = DateTime.Now;
        var files = new[]
        {
            ImageFile(
                "/one/conflict.jpg",
                new ImageMetadata(4000, 3000, new DateTime(2024, 6, 1, 12, 0, 0), null, null, null, 1, true, false, null, null)
                {
                    DateTimeOriginal = new DateTime(2024, 6, 1, 12, 0, 0),
                    DateTimeDigitized = new DateTime(2024, 6, 3, 12, 0, 0)
                }),
            ImageFile(
                "/one/future.jpg",
                new ImageMetadata(null, null, now.AddYears(1), null, null, null, 1, true, false, null, null)
                {
                    DateTimeOriginal = now.AddYears(1)
                }),
            ImageFile(
                "/one/normal.jpg",
                new ImageMetadata(1920, 1080, new DateTime(2024, 6, 2, 10, 0, 0), null, null, null, 1, false, false, null, null)),
            ImageFile(
                "/one/no-date.jpg",
                new ImageMetadata(1600, 1200, null, null, null, null, 1, false, false, null, null))
        };

        var analyzer = new FakeAnalyzer(path => new MediaAnalysisResult(
            path,
            files,
            new[]
            {
                new MediaAnalysisIssue(
                    "/one/broken.jpg",
                    MediaAnalysisIssueKind.MetadataError,
                    "Metadaten konnten nicht gelesen werden.",
                    MediaAnalysisSeverity.Warning)
            },
            1,
            files.Length,
            0));

        var service = new LibraryHealthService(resolver, analyzer, new FakeIndexInspector());
        var report = await service.ScanAsync(settings);

        var endpoint = Assert.Single(report.Endpoints);
        Assert.Equal(4, endpoint.ImageCount);
        Assert.Equal(1, endpoint.MissingCaptureDateCount);
        Assert.Equal(1, endpoint.MissingDimensionsCount);
        Assert.Equal(1, endpoint.ConflictingCaptureDateCount);
        Assert.Equal(1, endpoint.SuspiciousCaptureDateCount);
        Assert.Equal(1, endpoint.MetadataErrorCount);

        Assert.Contains(report.Issues, issue => issue.Kind == LibraryHealthIssueKind.MissingCaptureDate);
        Assert.Contains(report.Issues, issue => issue.Kind == LibraryHealthIssueKind.ConflictingCaptureDate);
        Assert.Contains(report.Issues, issue => issue.Kind == LibraryHealthIssueKind.SuspiciousCaptureDate);
        Assert.Contains(report.Issues, issue => issue.Kind == LibraryHealthIssueKind.MissingDimensions);
        Assert.DoesNotContain(
            report.Issues,
            issue => issue.Message.Contains("Kamera", StringComparison.OrdinalIgnoreCase)
                     || issue.Message.Contains("GPS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScanMergesIndexCountsIntoEndpointResult()
    {
        var settings = new StorageSettings(new[] { Endpoint("photos", "volume-one") });
        var resolver = new FakeResolver(new Dictionary<string, StorageLocationResolution>
        {
            ["volume-one"] = Available("/one", "volume-one")
        });
        var indexInspector = new FakeIndexInspector(
            LibraryHealthIndexSummary.Empty with
            {
                EndpointSummaries = new[]
                {
                    new LibraryHealthIndexEndpointSummary(
                        "photos",
                        IndexedLocationCount: 12,
                        ReachableLocationCount: 10,
                        OfflineLocationCount: 0,
                        MissingLocationCount: 2,
                        HistoricalLocationCount: 3,
                        UnconfiguredLocationCount: 0)
                }
            });
        var service = new LibraryHealthService(resolver, new FakeAnalyzer(), indexInspector);

        var report = await service.ScanAsync(settings);

        var endpoint = Assert.Single(report.Endpoints);
        Assert.Equal(12, endpoint.IndexedLocationCount);
        Assert.Equal(10, endpoint.ReachableIndexedLocationCount);
        Assert.Equal(2, endpoint.MissingIndexedLocationCount);
        Assert.Equal(3, endpoint.HistoricalIndexedLocationCount);
    }

    private static MediaAnalyzedFile ImageFile(
        string path,
        ImageMetadata metadata)
    {
        var now = DateTimeOffset.UtcNow;
        return new MediaAnalyzedFile(
            new MediaFile(
                path,
                Path.GetFileName(path),
                Path.GetExtension(path),
                1024,
                now,
                now,
                MediaFileType.Image,
                false),
            metadata);
    }

    private static StorageEndpoint Endpoint(string id, string volumeId, bool enabled = true) =>
        new(id, id, StorageEndpointKind.Custom, new StorageLocation(volumeId, string.Empty), enabled);

    private static StorageLocationResolution Available(string path, string volumeId) =>
        new(
            StorageLocationStatus.Available,
            path,
            new StorageVolumeInfo(volumeId, path),
            "ok");

    private sealed class FakeIndexInspector : ILibraryHealthIndexInspector
    {
        private readonly LibraryHealthIndexSummary _summary;

        public FakeIndexInspector(LibraryHealthIndexSummary? summary = null)
        {
            _summary = summary ?? LibraryHealthIndexSummary.Empty;
        }

        public IReadOnlyList<LibraryHealthIndexEndpointState> LastEndpointStates { get; private set; } =
            Array.Empty<LibraryHealthIndexEndpointState>();

        public Task<LibraryHealthIndexSummary> InspectAsync(
            IReadOnlyList<LibraryHealthIndexEndpointState> endpointStates,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastEndpointStates = endpointStates;
            return Task.FromResult(_summary);
        }
    }

    private sealed class FakeResolver : IStorageLocationResolver
    {
        private readonly IReadOnlyDictionary<string, StorageLocationResolution> _resolutions;

        public FakeResolver(IReadOnlyDictionary<string, StorageLocationResolution> resolutions)
        {
            _resolutions = resolutions;
        }

        public StorageLocation Capture(string absolutePath) => throw new NotSupportedException();

        public StorageLocationResolution Resolve(StorageLocation location) =>
            _resolutions[location.VolumeId];
    }

    private sealed class FakeAnalyzer : IMediaAnalyzer
    {
        private readonly Func<string, MediaAnalysisResult>? _resultFactory;

        public FakeAnalyzer(Func<string, MediaAnalysisResult>? resultFactory = null)
        {
            _resultFactory = resultFactory;
        }

        public List<string> Paths { get; } = new();

        public Task<MediaAnalysisResult> AnalyzeAsync(
            string path,
            MediaAnalysisOptions? options = null,
            IProgress<MediaAnalysisProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Paths.Add(path);

            return Task.FromResult(
                _resultFactory?.Invoke(path)
                ?? new MediaAnalysisResult(
                    path,
                    Array.Empty<MediaAnalyzedFile>(),
                    Array.Empty<MediaAnalysisIssue>(),
                    0,
                    0,
                    0));
        }
    }
}
