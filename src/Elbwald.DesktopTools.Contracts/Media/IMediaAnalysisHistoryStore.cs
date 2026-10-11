namespace Elbwald.DesktopTools.Contracts.Media;

public interface IMediaAnalysisHistoryStore
{
    Task<MediaAnalysisHistoryEntry?> LoadLastAsync(
        CancellationToken cancellationToken = default);

    Task<MediaAnalysisResultSnapshot?> LoadLastResultAsync(
        CancellationToken cancellationToken = default);

    Task SaveLastAsync(
        MediaAnalysisHistoryEntry entry,
        CancellationToken cancellationToken = default);

    Task SaveLastResultAsync(
        MediaAnalysisResultSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
