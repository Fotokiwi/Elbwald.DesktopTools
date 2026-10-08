namespace Elbwald.DesktopTools.Contracts.FileOperations;

public interface IFileOperationProcessLock : IDisposable
{
    bool IsHeld { get; }

    ValueTask<FileOperationProcessLockAcquireResult> TryAcquireAsync(
        CancellationToken cancellationToken = default);
}
