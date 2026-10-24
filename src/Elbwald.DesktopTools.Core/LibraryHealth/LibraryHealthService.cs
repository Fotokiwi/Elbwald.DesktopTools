using Elbwald.DesktopTools.Contracts.LibraryHealth;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Core.LibraryHealth;

public sealed class LibraryHealthService : ILibraryHealthService
{
    private const string MediaIndexEndpointId = "media-index";

    private readonly IStorageLocationResolver _storageResolver;
    private readonly IMediaAnalyzer _mediaAnalyzer;
    private readonly ILibraryHealthIndexInspector _indexInspector;

    public LibraryHealthService(
        IStorageLocationResolver storageResolver,
        IMediaAnalyzer mediaAnalyzer,
        ILibraryHealthIndexInspector indexInspector)
    {
        ArgumentNullException.ThrowIfNull(storageResolver);
        ArgumentNullException.ThrowIfNull(mediaAnalyzer);
        ArgumentNullException.ThrowIfNull(indexInspector);
        _storageResolver = storageResolver;
        _mediaAnalyzer = mediaAnalyzer;
        _indexInspector = indexInspector;
    }

    public async Task<LibraryHealthReport> ScanAsync(
        StorageSettings settings,
        CancellationToken cancellationToken = default,
        IProgress<LibraryHealthProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var startedAt = DateTimeOffset.UtcNow;
        var endpointResults = new List<LibraryHealthEndpointResult>();

        progress?.Report(new LibraryHealthProgress(
            LibraryHealthScanStage.ResolvingStorageLocations));
        var issues = new List<LibraryHealthIssue>();

        // Resolve every configured endpoint for index classification. Disabled endpoints
        // remain known to the index and therefore must not look like deleted data.
        var endpointStates = new List<LibraryHealthIndexEndpointState>();
        var resolutions = new Dictionary<string, StorageLocationResolution>(StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in settings.Endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolution = _storageResolver.Resolve(endpoint.Location);
            resolutions[endpoint.Id] = resolution;
            endpointStates.Add(new LibraryHealthIndexEndpointState(
                endpoint.Id,
                endpoint.Enabled,
                resolution.Status,
                resolution.ResolvedPath));
        }

        var enabledEndpoints = settings.Endpoints
            .Where(endpoint => endpoint.Enabled)
            .ToArray();

        for (var endpointOffset = 0; endpointOffset < enabledEndpoints.Length; endpointOffset++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var endpoint = enabledEndpoints[endpointOffset];
            var endpointIndex = endpointOffset + 1;
            var resolution = resolutions[endpoint.Id];

            if (!resolution.IsAvailable)
            {
                issues.Add(CreateStorageIssue(endpoint, resolution));
                endpointResults.Add(new LibraryHealthEndpointResult(
                    endpoint,
                    resolution.Status,
                    resolution.ResolvedPath,
                    0,
                    1,
                    0));
                continue;
            }

            try
            {
                progress?.Report(new LibraryHealthProgress(
                    LibraryHealthScanStage.ScanningFiles,
                    endpointIndex,
                    enabledEndpoints.Length,
                    endpoint.Name,
                    CurrentPath: resolution.ResolvedPath));

                var analysisProgress = progress is null
                    ? null
                    : new EndpointAnalysisProgress(
                        progress,
                        endpointIndex,
                        enabledEndpoints.Length,
                        endpoint.Name,
                        resolution.ResolvedPath!);

                var analysis = await _mediaAnalyzer.AnalyzeAsync(
                    resolution.ResolvedPath!,
                    new MediaAnalysisOptions
                    {
                        Recursive = true,
                        IncludeUnknownFiles = true
                    },
                    analysisProgress,
                    cancellationToken);

                foreach (var analysisIssue in analysis.Issues)
                {
                    issues.Add(new LibraryHealthIssue(
                        endpoint.Id,
                        analysisIssue.Kind == MediaAnalysisIssueKind.ScanError
                            ? LibraryHealthIssueKind.ScanError
                            : LibraryHealthIssueKind.MetadataProblem,
                        analysisIssue.Severity == MediaAnalysisSeverity.Problem
                            ? LibraryHealthSeverity.Problem
                            : LibraryHealthSeverity.Warning,
                        analysisIssue.Message,
                        analysisIssue.Path));
                }

                var metadataQuality = EvaluateMetadataQuality(analysis);

                if (analysis.MissingCaptureDateCount > 0)
                {
                    issues.Add(new LibraryHealthIssue(
                        endpoint.Id,
                        LibraryHealthIssueKind.MissingCaptureDate,
                        LibraryHealthSeverity.Warning,
                        $"{analysis.MissingCaptureDateCount:N0} Bilddateien besitzen kein ermitteltes Aufnahmedatum."));
                }

                if (metadataQuality.ConflictingCaptureDateCount > 0)
                {
                    issues.Add(new LibraryHealthIssue(
                        endpoint.Id,
                        LibraryHealthIssueKind.ConflictingCaptureDate,
                        LibraryHealthSeverity.Warning,
                        $"{metadataQuality.ConflictingCaptureDateCount:N0} Bilddateien enthalten Datumsquellen, die mehr als 24 Stunden voneinander abweichen. Prüfe diese Dateien, bevor Datum-basierte Sortierung oder Projektzuordnung verwendet wird."));
                }

                if (metadataQuality.SuspiciousCaptureDateCount > 0)
                {
                    issues.Add(new LibraryHealthIssue(
                        endpoint.Id,
                        LibraryHealthIssueKind.SuspiciousCaptureDate,
                        LibraryHealthSeverity.Warning,
                        $"{metadataQuality.SuspiciousCaptureDateCount:N0} Bilddateien besitzen ein offensichtlich unplausibles Aufnahmedatum (vor 1900 oder in der Zukunft)."));
                }

                if (metadataQuality.MissingDimensionsCount > 0)
                {
                    issues.Add(new LibraryHealthIssue(
                        endpoint.Id,
                        LibraryHealthIssueKind.MissingDimensions,
                        LibraryHealthSeverity.Warning,
                        $"{metadataQuality.MissingDimensionsCount:N0} Bilddateien besitzen keine zuverlässig ermittelbaren Bildabmessungen. Vorschau oder weitere Bildfunktionen können dadurch eingeschränkt sein."));
                }

                var aggregateMetadataIssueCount =
                    (analysis.MissingCaptureDateCount > 0 ? 1 : 0)
                    + (metadataQuality.ConflictingCaptureDateCount > 0 ? 1 : 0)
                    + (metadataQuality.SuspiciousCaptureDateCount > 0 ? 1 : 0)
                    + (metadataQuality.MissingDimensionsCount > 0 ? 1 : 0);

                endpointResults.Add(new LibraryHealthEndpointResult(
                    endpoint,
                    resolution.Status,
                    resolution.ResolvedPath,
                    analysis.TotalFiles,
                    analysis.Issues.Count + aggregateMetadataIssueCount,
                    analysis.MissingCaptureDateCount)
                {
                    ImageCount = analysis.ImageCount,
                    MissingDimensionsCount = metadataQuality.MissingDimensionsCount,
                    ConflictingCaptureDateCount = metadataQuality.ConflictingCaptureDateCount,
                    SuspiciousCaptureDateCount = metadataQuality.SuspiciousCaptureDateCount,
                    MetadataErrorCount = analysis.MetadataErrorCount
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidOperationException)
            {
                issues.Add(new LibraryHealthIssue(
                    endpoint.Id,
                    LibraryHealthIssueKind.ScanError,
                    LibraryHealthSeverity.Problem,
                    "Der Endpunkt konnte nicht vollständig gelesen werden: " + exception.Message,
                    resolution.ResolvedPath));

                endpointResults.Add(new LibraryHealthEndpointResult(
                    endpoint,
                    resolution.Status,
                    resolution.ResolvedPath,
                    0,
                    1,
                    0));
            }
        }

        progress?.Report(new LibraryHealthProgress(
            LibraryHealthScanStage.InspectingIndexLocations));

        var indexSummary = await InspectMediaIndexAsync(
            endpointStates,
            issues,
            cancellationToken,
            progress);
        var indexByEndpoint = indexSummary.EndpointSummaries.ToDictionary(
            summary => summary.EndpointId,
            StringComparer.OrdinalIgnoreCase);

        endpointResults = endpointResults
            .Select(result =>
            {
                if (!indexByEndpoint.TryGetValue(result.Endpoint.Id, out var indexEndpoint))
                {
                    return result;
                }

                return result with
                {
                    IndexedLocationCount = indexEndpoint.IndexedLocationCount,
                    ReachableIndexedLocationCount = indexEndpoint.ReachableLocationCount,
                    MissingIndexedLocationCount = indexEndpoint.MissingLocationCount,
                    HistoricalIndexedLocationCount = indexEndpoint.HistoricalLocationCount
                };
            })
            .ToList();

        progress?.Report(new LibraryHealthProgress(
            LibraryHealthScanStage.Finalizing));

        var report = new LibraryHealthReport(
            startedAt,
            DateTimeOffset.UtcNow,
            endpointResults,
            issues,
            indexSummary);

        progress?.Report(new LibraryHealthProgress(
            LibraryHealthScanStage.Completed));

        return report;
    }

    private async Task<LibraryHealthIndexSummary> InspectMediaIndexAsync(
        IReadOnlyList<LibraryHealthIndexEndpointState> endpointStates,
        ICollection<LibraryHealthIssue> issues,
        CancellationToken cancellationToken,
        IProgress<LibraryHealthProgress>? progress)
    {
        try
        {
            var summary = await _indexInspector.InspectAsync(
                endpointStates,
                cancellationToken,
                progress);

            if (summary.OfflineMediaCount > 0)
            {
                issues.Add(new LibraryHealthIssue(
                    MediaIndexEndpointId,
                    LibraryHealthIssueKind.IndexedMediaOffline,
                    LibraryHealthSeverity.Info,
                    $"{summary.OfflineMediaCount:N0} indexierte Medien liegen ausschließlich auf derzeit nicht verbundenen oder deaktivierten Speicherorten. Das ist kein Datenverlust; verbinde den Speicherort, um sie wieder verfügbar zu machen."));
            }

            if (summary.MissingMediaCount > 0)
            {
                issues.Add(new LibraryHealthIssue(
                    MediaIndexEndpointId,
                    LibraryHealthIssueKind.IndexedMediaMissing,
                    LibraryHealthSeverity.Problem,
                    $"{summary.MissingMediaCount:N0} indexierte Medien haben keinen erreichbaren Speicherort, obwohl kein aktuell offline befindlicher Speicherort ihre Abwesenheit erklärt. Medienindex und Dateisystem sollten geprüft werden."));
            }

            if (summary.UnconfiguredMediaCount > 0)
            {
                issues.Add(new LibraryHealthIssue(
                    MediaIndexEndpointId,
                    LibraryHealthIssueKind.IndexedMediaUnconfigured,
                    LibraryHealthSeverity.Warning,
                    $"{summary.UnconfiguredMediaCount:N0} indexierte Medien verweisen ausschließlich auf Speicherorte, die nicht mehr in Elbwald konfiguriert sind."));
            }

            if (summary.StaleLocationCount > 0)
            {
                issues.Add(new LibraryHealthIssue(
                    MediaIndexEndpointId,
                    LibraryHealthIssueKind.StaleIndexLocation,
                    LibraryHealthSeverity.Warning,
                    $"{summary.StaleLocationCount:N0} als aktuell markierte Indexorte existieren am erreichbaren Speicherort nicht mehr. Aktualisiere den Medienindex, damit Verschiebungen oder Löschungen sauber erkannt werden."));
            }

            return summary;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            issues.Add(new LibraryHealthIssue(
                MediaIndexEndpointId,
                LibraryHealthIssueKind.ScanError,
                LibraryHealthSeverity.Problem,
                "Der Medienindex konnte für den Gesundheitscheck nicht gelesen werden: "
                + exception.GetBaseException().Message));

            return LibraryHealthIndexSummary.Empty;
        }
    }

    private static MetadataQualitySummary EvaluateMetadataQuality(
        MediaAnalysisResult analysis)
    {
        var missingDimensionsCount = 0;
        var conflictingCaptureDateCount = 0;
        var suspiciousCaptureDateCount = 0;
        var latestPlausibleDate = DateTime.Now.AddDays(1);
        var earliestPlausibleDate = new DateTime(1900, 1, 1);

        foreach (var file in analysis.Files.Where(file => file.File.MediaType == MediaFileType.Image))
        {
            var metadata = file.ImageMetadata;

            if (metadata?.HasDimensions != true)
            {
                missingDimensionsCount++;
            }

            if (metadata is null)
            {
                continue;
            }

            var captureCandidates = new[]
                {
                    metadata.DateTimeOriginal,
                    metadata.DateTimeDigitized,
                    metadata.GpsDateTimeUtc,
                    metadata.CapturedAt
                }
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToArray();

            if (captureCandidates.Length >= 2)
            {
                var earliest = captureCandidates.Min();
                var latest = captureCandidates.Max();

                if (latest - earliest > TimeSpan.FromHours(24))
                {
                    conflictingCaptureDateCount++;
                }
            }

            if (metadata.CapturedAt is { } capturedAt
                && (capturedAt < earliestPlausibleDate || capturedAt > latestPlausibleDate))
            {
                suspiciousCaptureDateCount++;
            }
        }

        return new MetadataQualitySummary(
            missingDimensionsCount,
            conflictingCaptureDateCount,
            suspiciousCaptureDateCount);
    }

    private sealed class EndpointAnalysisProgress : IProgress<MediaAnalysisProgress>
    {
        private const long MinimumReportIntervalMilliseconds = 125;

        private readonly IProgress<LibraryHealthProgress> _progress;
        private readonly int _endpointIndex;
        private readonly int _endpointTotal;
        private readonly string _endpointName;
        private readonly string _rootPath;

        private MediaAnalysisStage? _lastStage;
        private long _lastReportAt;

        public EndpointAnalysisProgress(
            IProgress<LibraryHealthProgress> progress,
            int endpointIndex,
            int endpointTotal,
            string endpointName,
            string rootPath)
        {
            _progress = progress;
            _endpointIndex = endpointIndex;
            _endpointTotal = endpointTotal;
            _endpointName = endpointName;
            _rootPath = rootPath;
        }

        public void Report(MediaAnalysisProgress value)
        {
            var now = Environment.TickCount64;
            var stageChanged = _lastStage != value.Stage;
            var isTerminal = value.Stage == MediaAnalysisStage.Completed;

            if (!stageChanged
                && !isTerminal
                && now - _lastReportAt < MinimumReportIntervalMilliseconds)
            {
                return;
            }

            _lastStage = value.Stage;
            _lastReportAt = now;

            var stage = value.Stage switch
            {
                MediaAnalysisStage.ReadingMetadata => LibraryHealthScanStage.ReadingMetadata,
                MediaAnalysisStage.Completed => LibraryHealthScanStage.ReadingMetadata,
                _ => LibraryHealthScanStage.ScanningFiles
            };

            _progress.Report(new LibraryHealthProgress(
                stage,
                _endpointIndex,
                _endpointTotal,
                _endpointName,
                value.FilesDiscovered,
                value.MetadataProcessed,
                value.MetadataTotal,
                value.CurrentPath ?? _rootPath));
        }
    }

    private sealed record MetadataQualitySummary(
        int MissingDimensionsCount,
        int ConflictingCaptureDateCount,
        int SuspiciousCaptureDateCount);

    private static LibraryHealthIssue CreateStorageIssue(
        StorageEndpoint endpoint,
        StorageLocationResolution resolution)
    {
        var (kind, severity) = resolution.Status switch
        {
            StorageLocationStatus.VolumeUnavailable =>
                (LibraryHealthIssueKind.VolumeUnavailable, LibraryHealthSeverity.Info),
            StorageLocationStatus.PathMissing =>
                (LibraryHealthIssueKind.PathMissing, LibraryHealthSeverity.Problem),
            StorageLocationStatus.AmbiguousVolume =>
                (LibraryHealthIssueKind.AmbiguousVolume, LibraryHealthSeverity.Problem),
            _ =>
                (LibraryHealthIssueKind.InvalidConfiguration, LibraryHealthSeverity.Problem)
        };

        return new LibraryHealthIssue(
            endpoint.Id,
            kind,
            severity,
            resolution.Message,
            resolution.ResolvedPath);
    }
}
