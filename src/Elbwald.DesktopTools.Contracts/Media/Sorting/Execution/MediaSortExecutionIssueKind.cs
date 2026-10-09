namespace Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;

public enum MediaSortExecutionIssueKind
{
    SortPlanNotExecutable,
    ReviewRequiresConfirmation,
    CompanionReviewRequiresConfirmation,
    ExpandedFilePlanConflict,
    FileOperationSafety,
    RecoveryNotReady,
    RecoveryStorageUnavailable,
    RecoveryStorageInsufficientSpace,
    SourceSnapshotUnavailable,
    SourceMissing,
    SourceLengthChanged,
    SourceLastWriteTimeChanged,
    DestinationAppeared
}
