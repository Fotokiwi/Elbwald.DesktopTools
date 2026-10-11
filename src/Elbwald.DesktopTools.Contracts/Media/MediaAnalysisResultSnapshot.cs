namespace Elbwald.DesktopTools.Contracts.Media;

/// <summary>
/// Persistierbare Momentaufnahme eines Analyseergebnisses.
/// Die Momentaufnahme enthält nur die Eingabedaten, aus denen
/// <see cref="MediaAnalysisResult"/> seine abgeleiteten Werte erneut berechnet.
/// </summary>
public sealed record MediaAnalysisResultSnapshot(
    DateTimeOffset CompletedUtc,
    string RootPath,
    MediaAnalyzedFile[] Files,
    MediaAnalysisIssue[] Issues,
    int DirectoriesVisited,
    int FilesDiscovered,
    int SkippedSymbolicLinkDirectories,
    int MetadataCacheHits,
    int MetadataCacheMisses)
{
    public static MediaAnalysisResultSnapshot FromResult(
        MediaAnalysisResult result,
        DateTimeOffset completedUtc)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new MediaAnalysisResultSnapshot(
            completedUtc,
            result.RootPath,
            result.Files.ToArray(),
            result.Issues.ToArray(),
            result.DirectoriesVisited,
            result.FilesDiscovered,
            result.SkippedSymbolicLinkDirectories,
            result.MetadataCacheHits,
            result.MetadataCacheMisses);
    }

    public MediaAnalysisResult ToResult()
    {
        return new MediaAnalysisResult(
            RootPath,
            Files ?? Array.Empty<MediaAnalyzedFile>(),
            Issues ?? Array.Empty<MediaAnalysisIssue>(),
            DirectoriesVisited,
            FilesDiscovered,
            SkippedSymbolicLinkDirectories,
            MetadataCacheHits,
            MetadataCacheMisses);
    }
}
