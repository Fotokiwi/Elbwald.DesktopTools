namespace Elbwald.DesktopTools.Contracts.FileOperations;

public enum FileOperationExecutionState
{
    Completed,
    Failed,
    Cancelled,
    RecoveryRequired
}
