using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;

public sealed record MediaSortExecutionProblem(
    MediaSortExecutionProblemKind Kind,
    string SourcePath,
    string? DestinationPath,
    int SkippedOperationCount,
    string Message,
    StorageHealthSnapshot StorageHealth)
{
    public bool SuspectsPhysicalDevice =>
        Kind == MediaSortExecutionProblemKind.SuspectedStorageFailure
        || StorageHealth.SuspectsPhysicalDevice;
}
