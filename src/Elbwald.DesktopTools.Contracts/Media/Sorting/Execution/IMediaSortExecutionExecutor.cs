namespace Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;

public interface IMediaSortExecutionExecutor
{
    Task<MediaSortLiveExecutionResult> ExecuteAsync(
        MediaSortExecutionPlan plan,
        MediaSortExecutionConfirmation confirmation,
        IProgress<MediaSortLiveExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
