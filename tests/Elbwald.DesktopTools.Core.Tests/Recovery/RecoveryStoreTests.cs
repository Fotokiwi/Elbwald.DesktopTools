using System.Security.Cryptography;
using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Core.Recovery;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Recovery;

public sealed class RecoveryStoreTests
{
    [Fact]
    public async Task MemoryStore_PreserveRestoreRelease_RoundTripsData()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "source.bin",
            "important-data");

        var destination = directory.GetPath(
            "restored/source.bin");

        var options = CreateOptions(
            directory.GetPath("recovery"));

        var store = new MemoryRecoveryStore(options);

        var handle = await store.PreserveAsync(source);

        Assert.Equal(
            RecoveryStorageKind.Memory,
            handle.StorageKind);

        Assert.True(store.UsedBytes > 0);

        await store.RestoreAsync(
            handle,
            destination);

        Assert.Equal(
            "important-data",
            File.ReadAllText(destination));

        await store.ReleaseAsync(handle);

        Assert.Equal(0, store.UsedBytes);
    }

    [Fact]
    public async Task FileStore_PreserveRestoreRelease_RoundTripsData()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "source.bin",
            "persistent-recovery");

        var destination = directory.GetPath(
            "restored/source.bin");

        var options = CreateOptions(
            directory.GetPath("recovery"));

        var store = new FileRecoveryStore(options);

        var handle = await store.PreserveAsync(source);

        Assert.Equal(
            RecoveryStorageKind.PersistentFile,
            handle.StorageKind);

        Assert.NotNull(handle.StorageLocation);
        Assert.True(File.Exists(handle.StorageLocation));

        await store.RestoreAsync(
            handle,
            destination);

        Assert.Equal(
            "persistent-recovery",
            File.ReadAllText(destination));

        await store.ReleaseAsync(handle);

        Assert.False(File.Exists(handle.StorageLocation));
    }

    [Fact]
    public async Task HybridStore_SmallFile_UsesMemory()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "small.bin",
            "small");

        var options = CreateOptions(
            directory.GetPath("recovery")) with
        {
            MaxMemoryBytes = 1024,
            MaxSingleMemoryItemBytes = 1024
        };

        var memory = new MemoryRecoveryStore(options);
        var file = new FileRecoveryStore(options);
        var hybrid = new HybridRecoveryStore(
            memory,
            file);

        var handle = await hybrid.PreserveAsync(source);

        Assert.Equal(
            RecoveryStorageKind.Memory,
            handle.StorageKind);

        await hybrid.ReleaseAsync(handle);
    }

    [Fact]
    public async Task HybridStore_FileOverMemoryThreshold_UsesPersistentStore()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "large.bin",
            new string('x', 128));

        var options = CreateOptions(
            directory.GetPath("recovery")) with
        {
            MaxMemoryBytes = 64,
            MaxSingleMemoryItemBytes = 64
        };

        var memory = new MemoryRecoveryStore(options);
        var file = new FileRecoveryStore(options);
        var hybrid = new HybridRecoveryStore(
            memory,
            file);

        var handle = await hybrid.PreserveAsync(source);

        Assert.Equal(
            RecoveryStorageKind.PersistentFile,
            handle.StorageKind);

        Assert.True(File.Exists(handle.StorageLocation));

        await hybrid.ReleaseAsync(handle);
    }

    [Fact]
    public async Task Restore_DoesNotOverwriteExistingFile()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "source.bin",
            "source");

        var destination = directory.CreateFile(
            "destination.bin",
            "existing");

        var options = CreateOptions(
            directory.GetPath("recovery"));

        var store = new MemoryRecoveryStore(options);
        var handle = await store.PreserveAsync(source);

        await Assert.ThrowsAsync<IOException>(
            () => store.RestoreAsync(
                handle,
                destination));

        Assert.Equal(
            "existing",
            File.ReadAllText(destination));

        await store.ReleaseAsync(handle);
    }

    [Fact]
    public async Task MemoryHandle_ContainsCorrectSha256()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "source.bin",
            "hash-me");

        var options = CreateOptions(
            directory.GetPath("recovery"));

        var store = new MemoryRecoveryStore(options);
        var handle = await store.PreserveAsync(source);

        var expected = Convert.ToHexString(
            SHA256.HashData(
                File.ReadAllBytes(source)));

        Assert.Equal(
            expected,
            handle.Sha256);

        await store.ReleaseAsync(handle);
    }

    private static RecoveryOptions CreateOptions(
        string persistentDirectory)
    {
        return new RecoveryOptions
        {
            PersistentDirectory = persistentDirectory,
            MaxMemoryBytes = 1024 * 1024,
            MaxSingleMemoryItemBytes = 1024 * 1024,
            MinimumPersistentFreeSpaceReserveBytes = 0,
            PersistentFreeSpaceReserveRatio = 0,
            MaximumPersistentRatioReserveBytes = 0
        };
    }
}
