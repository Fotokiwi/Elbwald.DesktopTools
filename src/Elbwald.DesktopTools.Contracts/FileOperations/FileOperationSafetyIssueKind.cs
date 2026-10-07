namespace Elbwald.DesktopTools.Contracts.FileOperations;

public enum FileOperationSafetyIssueKind
{
    PlanContainsConflicts,
    StorageVolumeUnknown,
    StorageCapacityUnavailable,
    InsufficientFreeSpace,
    CrossVolumeMoveBlocked
}
