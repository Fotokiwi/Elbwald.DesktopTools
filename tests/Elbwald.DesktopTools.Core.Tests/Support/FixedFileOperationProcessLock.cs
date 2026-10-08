using Elbwald.DesktopTools.Contracts.FileOperations;

namespace Elbwald.DesktopTools.Core.Tests.Support;

internal sealed class FixedFileOperationProcessLock
    : IFileOperationProcessLock
{
    private readonly FileOperationProcessLockAcquireResult _result;

    public FixedFileOperationProcessLock(
        FileOperationProcessLockAcquireResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _result = result;
    }

    public bool IsHeld =>
        _result.IsAcquired;

    public ValueTask<FileOperationProcessLockAcquireResult> TryAcquireAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(
            _result);
    }

    public void Dispose()
    {
    }

    public static FixedFileOperationProcessLock Held()
    {
        return new FixedFileOperationProcessLock(
            new FileOperationProcessLockAcquireResult(
                FileOperationProcessLockAcquireState.Acquired));
    }

    public static FixedFileOperationProcessLock Unavailable()
    {
        return new FixedFileOperationProcessLock(
            new FileOperationProcessLockAcquireResult(
                FileOperationProcessLockAcquireState.Unavailable,
                "Simulierter belegter Prozess-Lock."));
    }
}
