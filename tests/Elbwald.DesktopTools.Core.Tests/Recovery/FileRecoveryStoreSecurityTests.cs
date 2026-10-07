using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Core.Recovery;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Recovery;

public sealed class FileRecoveryStoreSecurityTests
{
    [Fact]
    public async Task VerifyAsync_ValidRecovery_ReturnsTrue()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "source.bin",
            "important-data");

        var store = CreateStore(directory);
        var handle = await store.PreserveAsync(source);

        Assert.True(
            await store.VerifyAsync(handle));
    }

    [Fact]
    public async Task VerifyAsync_CorruptedRecovery_ReturnsFalse()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "source.bin",
            "important-data");

        var store = CreateStore(directory);
        var handle = await store.PreserveAsync(source);

        await File.WriteAllTextAsync(
            handle.StorageLocation!,
            "corrupted");

        Assert.False(
            await store.VerifyAsync(handle));
    }

    [Fact]
    public async Task ReleaseAsync_TamperedStorageLocation_DoesNotDeleteExternalFile()
    {
        using var directory = new TemporaryDirectory();

        var externalFile = directory.CreateFile(
            "external/do-not-delete.txt",
            "keep-me");

        var recoveryDirectory =
            directory.GetPath("recovery");

        var store = CreateStore(directory);

        var handle = new RecoveryHandle(
            Guid.NewGuid().ToString("N"),
            directory.GetPath("source.bin"),
            7,
            "ABCDEF",
            DateTime.UtcNow,
            DateTimeOffset.UtcNow,
            RecoveryStorageKind.PersistentFile,
            externalFile);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.ReleaseAsync(handle));

        Assert.True(File.Exists(externalFile));
        Assert.Equal(
            "keep-me",
            File.ReadAllText(externalFile));

        Assert.NotEqual(
            Path.GetFullPath(recoveryDirectory),
            Path.GetDirectoryName(
                Path.GetFullPath(externalFile)));
    }

    [Fact]
    public async Task VerifyAsync_InvalidRecoveryId_IsRejected()
    {
        using var directory = new TemporaryDirectory();

        var store = CreateStore(directory);

        var handle = new RecoveryHandle(
            "../outside",
            directory.GetPath("source.bin"),
            0,
            string.Empty,
            DateTime.UtcNow,
            DateTimeOffset.UtcNow,
            RecoveryStorageKind.PersistentFile,
            directory.GetPath("outside.recovery"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.VerifyAsync(handle));
    }

    private static FileRecoveryStore CreateStore(
        TemporaryDirectory directory)
    {
        return new FileRecoveryStore(
            new RecoveryOptions
            {
                PersistentDirectory =
                    directory.GetPath("recovery"),
                MaxMemoryBytes = 0,
                MaxSingleMemoryItemBytes = 0,
                MinimumPersistentFreeSpaceReserveBytes = 0,
                PersistentFreeSpaceReserveRatio = 0,
                MaximumPersistentRatioReserveBytes = 0
            });
    }
}
