namespace Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;

public sealed record MediaSortLiveExecutionProgress(
    int CompletedOperationCount,
    int TotalOperationCount,
    int CompletedGroupCount,
    int TotalGroupCount,
    string CurrentSourcePath,
    string CurrentDestinationPath,
    int SkippedOperationCount = 0,
    int SkippedGroupCount = 0,
    MediaSortExecutionProblem? LatestProblem = null)
{
    public int ProcessedOperationCount =>
        CompletedOperationCount
        + SkippedOperationCount;

    public int Percentage =>
        TotalOperationCount <= 0
            ? 100
            : (int)Math.Round(
                ProcessedOperationCount * 100d / TotalOperationCount,
                MidpointRounding.AwayFromZero);
}
