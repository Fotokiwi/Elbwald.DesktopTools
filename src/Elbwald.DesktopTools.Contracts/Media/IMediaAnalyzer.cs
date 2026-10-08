namespace Elbwald.DesktopTools.Contracts.Media;

public interface IMediaAnalyzer
{
    Task<MediaAnalysisResult> AnalyzeAsync(
        string path,
        MediaAnalysisOptions? options = null,
        IProgress<MediaAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
