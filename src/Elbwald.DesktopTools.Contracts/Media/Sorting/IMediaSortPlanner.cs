namespace Elbwald.DesktopTools.Contracts.Media.Sorting;

public interface IMediaSortPlanner
{
    MediaSortPlan CreatePlan(
        string sourceRoot,
        string destinationRoot,
        IEnumerable<MediaAnalyzedFile> files,
        MediaSortOptions? options = null,
        CancellationToken cancellationToken = default);
}
