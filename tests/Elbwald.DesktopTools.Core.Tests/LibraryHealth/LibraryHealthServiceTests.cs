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
        var service = new LibraryHealthService(resolver, analyzer);

        var report = await service.ScanAsync(settings);

        Assert.Equal(2, report.Endpoints.Count);
        Assert.Equal(2, analyzer.Paths.Count);
        Assert.DoesNotContain("/three", analyzer.Paths);
    }

    [Fact]
    public async Task ScanDoesNotAnalyzeUnavailableEndpoint()
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
        var service = new LibraryHealthService(resolver, analyzer);

        var report = await service.ScanAsync(settings);

        Assert.Empty(analyzer.Paths);
        var issue = Assert.Single(report.Issues);
        Assert.Equal(LibraryHealthIssueKind.VolumeUnavailable, issue.Kind);
    }

    private static StorageEndpoint Endpoint(string id, string volumeId, bool enabled = true) =>
        new(id, id, StorageEndpointKind.Custom, new StorageLocation(volumeId, string.Empty), enabled);

    private static StorageLocationResolution Available(string path, string volumeId) =>
        new(
            StorageLocationStatus.Available,
            path,
            new StorageVolumeInfo(volumeId, path),
            "ok");

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
        public List<string> Paths { get; } = new();

        public Task<MediaAnalysisResult> AnalyzeAsync(
            string path,
            MediaAnalysisOptions? options = null,
            IProgress<MediaAnalysisProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Paths.Add(path);
            return Task.FromResult(new MediaAnalysisResult(
                path,
                Array.Empty<MediaAnalyzedFile>(),
                Array.Empty<MediaAnalysisIssue>(),
                0,
                0,
                0));
        }
    }
}
