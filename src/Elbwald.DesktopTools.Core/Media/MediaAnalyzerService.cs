using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.Core.Media;

public sealed class MediaAnalyzerService
    : IMediaAnalyzer
{
    private readonly IMediaScanner _scanner;
    private readonly IImageMetadataReader _imageMetadataReader;

    public MediaAnalyzerService(
        IMediaScanner scanner,
        IImageMetadataReader imageMetadataReader)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(imageMetadataReader);

        _scanner = scanner;
        _imageMetadataReader = imageMetadataReader;
    }

    public async Task<MediaAnalysisResult> AnalyzeAsync(
        string path,
        MediaAnalysisOptions? options = null,
        IProgress<MediaAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        options ??= MediaAnalysisOptions.Default;

        cancellationToken.ThrowIfCancellationRequested();

        var scanProgress =
            new InlineProgress<MediaScanProgress>(
                value =>
                    progress?.Report(
                        new MediaAnalysisProgress(
                            MediaAnalysisStage.Scanning,
                            value.FilesDiscovered,
                            MetadataProcessed: 0,
                            MetadataTotal: 0)));

        var scanResult =
            await _scanner.ScanAsync(
                path,
                new MediaScanOptions
                {
                    Recursive = options.Recursive,
                    IncludeUnknownFiles =
                        options.IncludeUnknownFiles
                },
                scanProgress,
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var issues =
            new List<MediaAnalysisIssue>();

        issues.AddRange(
            scanResult.Errors.Select(
                error =>
                    new MediaAnalysisIssue(
                        error.Path,
                        MediaAnalysisIssueKind.ScanError,
                        $"{error.Operation}: {error.Message}",
                        MediaAnalysisSeverity.Problem)));

        var imageCount =
            scanResult.Files.Count(file =>
                file.MediaType == MediaFileType.Image);

        var metadataProcessed = 0;
        var metadataCacheHits = 0;
        var metadataCacheMisses = 0;

        var analyzedFiles =
            new List<MediaAnalyzedFile>(
                scanResult.Files.Count);

        foreach (var file in scanResult.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ImageMetadata? metadata = null;

            if (file.MediaType == MediaFileType.Image)
            {
                progress?.Report(
                    new MediaAnalysisProgress(
                        MediaAnalysisStage.ReadingMetadata,
                        scanResult.FilesDiscovered,
                        metadataProcessed,
                        imageCount,
                        file.FullPath,
                        metadataCacheHits,
                        metadataCacheMisses));

                var metadataResult =
                    await _imageMetadataReader.ReadAsync(
                        file,
                        cancellationToken);

                if (metadataResult.IsFromCache)
                {
                    metadataCacheHits++;
                }
                else
                {
                    metadataCacheMisses++;
                }

                if (metadataResult.IsSuccessful)
                {
                    metadata =
                        metadataResult.Metadata;
                }
                else
                {
                    issues.Add(
                        new MediaAnalysisIssue(
                            file.FullPath,
                            MediaAnalysisIssueKind.MetadataError,
                            BuildMetadataErrorMessage(
                                metadataResult),
                            MediaAnalysisSeverity.Problem));
                }

                foreach (var warning in metadataResult.Warnings)
                {
                    issues.Add(
                        new MediaAnalysisIssue(
                            file.FullPath,
                            MediaAnalysisIssueKind.MetadataWarning,
                            warning,
                            MediaAnalysisSeverity.Info));
                }

                metadataProcessed++;

                progress?.Report(
                    new MediaAnalysisProgress(
                        MediaAnalysisStage.ReadingMetadata,
                        scanResult.FilesDiscovered,
                        metadataProcessed,
                        imageCount,
                        file.FullPath,
                        metadataCacheHits,
                        metadataCacheMisses));
            }

            analyzedFiles.Add(
                new MediaAnalyzedFile(
                    file,
                    metadata));
        }

        var result =
            new MediaAnalysisResult(
                scanResult.RootPath,
                analyzedFiles,
                issues,
                scanResult.DirectoriesVisited,
                scanResult.FilesDiscovered,
                scanResult.SkippedSymbolicLinkDirectories,
                metadataCacheHits,
                metadataCacheMisses);

        progress?.Report(
            new MediaAnalysisProgress(
                MediaAnalysisStage.Completed,
                result.FilesDiscovered,
                metadataProcessed,
                imageCount,
                CurrentPath: null,
                MetadataCacheHits: metadataCacheHits,
                MetadataCacheMisses: metadataCacheMisses));

        return result;
    }

    private static string BuildMetadataErrorMessage(
        ImageMetadataReadResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return result.ErrorMessage;
        }

        return result.State switch
        {
            ImageMetadataReadState.FileNotFound =>
                "Die Bilddatei wurde während der Analyse nicht mehr gefunden.",

            ImageMetadataReadState.AccessDenied =>
                "Auf die Bilddatei konnte nicht zugegriffen werden.",

            ImageMetadataReadState.UnsupportedFormat =>
                "Die Bildmetadaten konnten nicht gelesen werden.",

            ImageMetadataReadState.IoError =>
                "Beim Lesen der Bilddatei ist ein E/A-Fehler aufgetreten.",

            ImageMetadataReadState.NotImage =>
                "Die Datei wurde vom Metadatenreader nicht als Bild akzeptiert.",

            _ =>
                "Die Bildmetadaten konnten nicht vollständig analysiert werden."
        };
    }

    private sealed class InlineProgress<T>
        : IProgress<T>
    {
        private readonly Action<T> _report;

        public InlineProgress(
            Action<T> report)
        {
            ArgumentNullException.ThrowIfNull(report);
            _report = report;
        }

        public void Report(
            T value)
        {
            _report(value);
        }
    }
}
