using Elbwald.DesktopTools.Contracts.FileOperations;

namespace Elbwald.DesktopTools.Core.FileOperations;

public sealed class FileSystemOperationProcessLock
    : IFileOperationProcessLock
{
    private readonly string _lockFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private FileStream? _lockStream;
    private bool _disposed;
    private int _isHeld;

    public FileSystemOperationProcessLock(
        string lockFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            lockFilePath);

        _lockFilePath =
            Path.GetFullPath(lockFilePath);
    }

    public bool IsHeld =>
        Volatile.Read(ref _isHeld) == 1;

    public async ValueTask<FileOperationProcessLockAcquireResult>
        TryAcquireAsync(
            CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(
            cancellationToken);

        try
        {
            if (_disposed)
            {
                return new FileOperationProcessLockAcquireResult(
                    FileOperationProcessLockAcquireState.Failed,
                    "Der Prozess-Lock wurde bereits freigegeben.");
            }

            if (_lockStream is not null)
            {
                return new FileOperationProcessLockAcquireResult(
                    FileOperationProcessLockAcquireState.Acquired);
            }

            var directory =
                Path.GetDirectoryName(_lockFilePath)
                ?? throw new InvalidOperationException(
                    "Der Prozess-Lock hat kein gültiges Verzeichnis.");

            Directory.CreateDirectory(directory);

            try
            {
                _lockStream = new FileStream(
                    _lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.WriteThrough);

                Volatile.Write(
                    ref _isHeld,
                    1);

                return new FileOperationProcessLockAcquireResult(
                    FileOperationProcessLockAcquireState.Acquired);
            }
            catch (IOException exception)
            {
                return new FileOperationProcessLockAcquireResult(
                    FileOperationProcessLockAcquireState.Unavailable,
                    "Der exklusive Dateisicherheits-Lock ist bereits belegt "
                    + "oder konnte vom Dateisystem nicht exklusiv geöffnet werden. "
                    + $"Details: {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                return new FileOperationProcessLockAcquireResult(
                    FileOperationProcessLockAcquireState.Failed,
                    "Auf den Dateisicherheits-Lock kann nicht zugegriffen werden. "
                    + $"Details: {exception.Message}");
            }
            catch (NotSupportedException exception)
            {
                return new FileOperationProcessLockAcquireResult(
                    FileOperationProcessLockAcquireState.Failed,
                    "Das Dateisystem unterstützt den Dateisicherheits-Lock "
                    + $"nicht wie benötigt. Details: {exception.Message}");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Wait();

        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _lockStream?.Dispose();
            _lockStream = null;

            Volatile.Write(
                ref _isHeld,
                0);
        }
        finally
        {
            _gate.Release();
        }
    }
}
