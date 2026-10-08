namespace Elbwald.DesktopTools.Contracts.FileOperations;

public sealed record FileOperationProcessLockAcquireResult(
    FileOperationProcessLockAcquireState State,
    string? Message = null)
{
    public bool IsAcquired =>
        State == FileOperationProcessLockAcquireState.Acquired;
}
