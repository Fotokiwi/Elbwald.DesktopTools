using Elbwald.DesktopTools.Contracts.Recovery;

namespace Elbwald.DesktopTools.Core.Recovery;

public sealed class HybridRecoveryStore : IRecoveryStore
{
    private readonly MemoryRecoveryStore _memoryStore;
    private readonly FileRecoveryStore _fileStore;
    private readonly SemaphoreSlim _selectionLock = new(1, 1);

    public HybridRecoveryStore(
        MemoryRecoveryStore memoryStore,
        FileRecoveryStore fileStore)
    {
        ArgumentNullException.ThrowIfNull(memoryStore);
        ArgumentNullException.ThrowIfNull(fileStore);

        _memoryStore = memoryStore;
        _fileStore = fileStore;
    }

    public async Task<RecoveryHandle> PreserveAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var length = new FileInfo(fullSourcePath).Length;

        await _selectionLock.WaitAsync(cancellationToken);

        try
        {
            if (_memoryStore.CanPreserve(length))
            {
                return await _memoryStore.PreserveAsync(
                    fullSourcePath,
                    cancellationToken);
            }

            return await _fileStore.PreserveAsync(
                fullSourcePath,
                cancellationToken);
        }
        finally
        {
            _selectionLock.Release();
        }
    }

    public Task RestoreAsync(
        RecoveryHandle handle,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);

        return handle.StorageKind switch
        {
            RecoveryStorageKind.Memory =>
                _memoryStore.RestoreAsync(
                    handle,
                    destinationPath,
                    cancellationToken),

            RecoveryStorageKind.PersistentFile =>
                _fileStore.RestoreAsync(
                    handle,
                    destinationPath,
                    cancellationToken),

            _ => throw new InvalidOperationException(
                $"Unbekannter Recovery-Speichertyp: {handle.StorageKind}.")
        };
    }

    public Task ReleaseAsync(
        RecoveryHandle handle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);

        return handle.StorageKind switch
        {
            RecoveryStorageKind.Memory =>
                _memoryStore.ReleaseAsync(
                    handle,
                    cancellationToken),

            RecoveryStorageKind.PersistentFile =>
                _fileStore.ReleaseAsync(
                    handle,
                    cancellationToken),

            _ => throw new InvalidOperationException(
                $"Unbekannter Recovery-Speichertyp: {handle.StorageKind}.")
        };
    }
}
