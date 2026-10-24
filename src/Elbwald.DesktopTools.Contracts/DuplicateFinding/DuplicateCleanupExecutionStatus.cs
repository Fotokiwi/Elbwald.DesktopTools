namespace Elbwald.DesktopTools.Contracts.DuplicateFinding;

public enum DuplicateCleanupExecutionStatus
{
    MovedToTrash = 0,
    BlockedByPreflight = 1,
    TrashFailed = 2
}
