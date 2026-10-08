using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Core.FileOperations;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.FileOperations;

public sealed class FileSystemOperationProcessLockTests
{
    [Fact]
    public async Task TryAcquireAsync_FirstOwnerGetsExclusiveLock()
    {
        using var directory = new TemporaryDirectory();

        var lockPath =
            directory.GetPath(
                "recovery/desktop-tools.process.lock");

        using var processLock =
            new FileSystemOperationProcessLock(
                lockPath);

        var result =
            await processLock.TryAcquireAsync();

        Assert.True(result.IsAcquired);
        Assert.True(processLock.IsHeld);
        Assert.True(File.Exists(lockPath));
    }

    [Fact]
    public async Task TryAcquireAsync_SecondOwnerIsBlockedUntilFirstReleases()
    {
        using var directory = new TemporaryDirectory();

        var lockPath =
            directory.GetPath(
                "recovery/desktop-tools.process.lock");

        using var first =
            new FileSystemOperationProcessLock(
                lockPath);

        using var second =
            new FileSystemOperationProcessLock(
                lockPath);

        var firstResult =
            await first.TryAcquireAsync();

        var secondResult =
            await second.TryAcquireAsync();

        Assert.True(firstResult.IsAcquired);

        Assert.Equal(
            FileOperationProcessLockAcquireState.Unavailable,
            secondResult.State);

        Assert.False(second.IsHeld);

        first.Dispose();

        var retry =
            await second.TryAcquireAsync();

        Assert.True(retry.IsAcquired);
        Assert.True(second.IsHeld);
    }

    [Fact]
    public async Task TryAcquireAsync_StaleLockFileWithoutOwner_DoesNotBlock()
    {
        using var directory = new TemporaryDirectory();

        var lockPath =
            directory.GetPath(
                "recovery/desktop-tools.process.lock");

        Directory.CreateDirectory(
            Path.GetDirectoryName(lockPath)!);

        await File.WriteAllTextAsync(
            lockPath,
            "stale-file-is-not-a-lock");

        using var processLock =
            new FileSystemOperationProcessLock(
                lockPath);

        var result =
            await processLock.TryAcquireAsync();

        Assert.True(result.IsAcquired);
        Assert.True(processLock.IsHeld);
    }
}
