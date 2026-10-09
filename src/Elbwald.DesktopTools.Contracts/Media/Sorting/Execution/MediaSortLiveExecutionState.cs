namespace Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;

public enum MediaSortLiveExecutionState
{
    Completed,
    CompletedWithIssues,
    Failed,
    Cancelled,
    RecoveryRequired,
    BlockedByConfirmation,
    BlockedByPlan,
    BlockedByValidation,
    BlockedByProcessLock,
    MoveNotYetEnabled
}
