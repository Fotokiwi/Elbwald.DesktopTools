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
    public async Task TryAcquireAsync_SecondInstanceInSameProcessSharesLease()
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
        Assert.True(secondResult.IsAcquired);
        Assert.True(first.IsHeld);
        Assert.True(second.IsHeld);
    }

    [Fact]
    public async Task Dispose_FirstInstance_DoesNotReleaseSharedProcessLease()
    {
        using var directory = new TemporaryDirectory();

        var lockPath =
            directory.GetPath(
                "recovery/desktop-tools.process.lock");

        var first =
            new FileSystemOperationProcessLock(
                lockPath);

        using var second =
            new FileSystemOperationProcessLock(
                lockPath);

        Assert.True(
            (await first.TryAcquireAsync()).IsAcquired);

        Assert.True(
            (await second.TryAcquireAsync()).IsAcquired);

        first.Dispose();

        Assert.False(first.IsHeld);
        Assert.True(second.IsHeld);

        _ = Assert.Throws<IOException>(() =>
            new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None));

        second.Dispose();

        using var afterRelease =
            new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
    }

    [Fact]
    public async Task TryAcquireAsync_ExternalExclusiveOwnerIsBlocked()
    {
        using var directory = new TemporaryDirectory();

        var lockPath =
            directory.GetPath(
                "recovery/desktop-tools.process.lock");

        Directory.CreateDirectory(
            Path.GetDirectoryName(lockPath)!);

        using var externalOwner =
            new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

        using var processLock =
            new FileSystemOperationProcessLock(
                lockPath);

        var result =
            await processLock.TryAcquireAsync();

        Assert.Equal(
            FileOperationProcessLockAcquireState.Unavailable,
            result.State);

        Assert.False(processLock.IsHeld);
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
