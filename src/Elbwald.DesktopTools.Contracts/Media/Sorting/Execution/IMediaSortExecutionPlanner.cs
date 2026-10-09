namespace Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;

public interface IMediaSortExecutionPlanner
{
    Task<MediaSortExecutionPlan> CreateAsync(
        MediaSortPlan sortPlan,
        CancellationToken cancellationToken = default);

    Task<MediaSortExecutionValidationResult> ValidateCurrentStateAsync(
        MediaSortExecutionPlan executionPlan,
        CancellationToken cancellationToken = default);
}
