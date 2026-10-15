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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var startedAt = DateTimeOffset.UtcNow;
        var endpointResults = new List<LibraryHealthEndpointResult>();
        var issues = new List<LibraryHealthIssue>();

        foreach (var endpoint in settings.Endpoints.Where(endpoint => endpoint.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolution = _storageResolver.Resolve(endpoint.Location);

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
                var analysis = await _mediaAnalyzer.AnalyzeAsync(
                    resolution.ResolvedPath!,
                    new MediaAnalysisOptions
                    {
                        Recursive = true,
                        IncludeUnknownFiles = true
                    },
                    cancellationToken: cancellationToken);

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

                if (analysis.MissingCaptureDateCount > 0)
                {
                    issues.Add(new LibraryHealthIssue(
                        endpoint.Id,
                        LibraryHealthIssueKind.MissingCaptureDate,
                        LibraryHealthSeverity.Warning,
                        $"{analysis.MissingCaptureDateCount:N0} Bilddateien besitzen kein ermitteltes Aufnahmedatum."));
                }

                endpointResults.Add(new LibraryHealthEndpointResult(
                    endpoint,
                    resolution.Status,
                    resolution.ResolvedPath,
                    analysis.TotalFiles,
                    analysis.Issues.Count + (analysis.MissingCaptureDateCount > 0 ? 1 : 0),
                    analysis.MissingCaptureDateCount));
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

        var indexSummary = await InspectMediaIndexAsync(issues, cancellationToken);

        return new LibraryHealthReport(
            startedAt,
            DateTimeOffset.UtcNow,
            endpointResults,
            issues,
            indexSummary);
    }

    private async Task<LibraryHealthIndexSummary> InspectMediaIndexAsync(
        ICollection<LibraryHealthIssue> issues,
        CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _indexInspector.InspectAsync(cancellationToken);

            if (summary.OrphanedMediaCount > 0)
            {
                issues.Add(new LibraryHealthIssue(
                    MediaIndexEndpointId,
                    LibraryHealthIssueKind.IndexedMediaUnavailable,
                    LibraryHealthSeverity.Warning,
                    $"{summary.OrphanedMediaCount:N0} indexierte Medien besitzen aktuell keinen als vorhanden markierten Speicherort. Prüfe den Medienindex und verbundene Laufwerke."));
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

    private static LibraryHealthIssue CreateStorageIssue(
        StorageEndpoint endpoint,
        StorageLocationResolution resolution)
    {
        var (kind, severity) = resolution.Status switch
        {
            StorageLocationStatus.VolumeUnavailable =>
                (LibraryHealthIssueKind.VolumeUnavailable, LibraryHealthSeverity.Warning),
            StorageLocationStatus.PathMissing =>
                (LibraryHealthIssueKind.PathMissing, LibraryHealthSeverity.Warning),
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
