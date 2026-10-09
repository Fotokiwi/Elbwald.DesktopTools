using System.Collections.Concurrent;
using Elbwald.DesktopTools.Contracts.FileOperations;

namespace Elbwald.DesktopTools.Core.FileOperations;

public sealed class FileSystemOperationProcessLock
    : IFileOperationProcessLock
{
    private static readonly StringComparer LockPathComparer =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static readonly ConcurrentDictionary<string, SharedLockState>
        SharedStates =
            new(LockPathComparer);

    private readonly string _lockFilePath;
    private readonly SemaphoreSlim _instanceGate = new(1, 1);

    private SharedLockState? _sharedState;
    private bool _designerBypassHeld;
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
        await _instanceGate.WaitAsync(
            cancellationToken);

        try
        {
            if (_disposed)
            {
                return new FileOperationProcessLockAcquireResult(
                    FileOperationProcessLockAcquireState.Failed,
                    "Der Prozess-Lock wurde bereits freigegeben.");
            }

            if (_designerBypassHeld
                || IsAvaloniaDesignerHost())
            {
                // Rider/Avalonia Designer lädt die echte App-DLL in separaten
                // Designer.HostApp-Prozessen. Diese Prozesse dürfen den globalen
                // Dateisicherheits-Lock niemals besitzen, weil sie keine echten
                // Benutzer-Dateioperationen ausführen.
                _designerBypassHeld = true;

                Volatile.Write(
                    ref _isHeld,
                    1);

                return new FileOperationProcessLockAcquireResult(
                    FileOperationProcessLockAcquireState.Acquired);
            }

            if (_sharedState is not null)
            {
                return new FileOperationProcessLockAcquireResult(
                    FileOperationProcessLockAcquireState.Acquired);
            }

            var sharedState =
                SharedStates.GetOrAdd(
                    _lockFilePath,
                    _ => new SharedLockState());

            await sharedState.Gate.WaitAsync(
                cancellationToken);

            try
            {
                if (sharedState.LockStream is null)
                {
                    var directory =
                        Path.GetDirectoryName(_lockFilePath)
                        ?? throw new InvalidOperationException(
                            "Der Prozess-Lock hat kein gültiges Verzeichnis.");

                    Directory.CreateDirectory(directory);

                    try
                    {
                        sharedState.LockStream =
                            new FileStream(
                                _lockFilePath,
                                FileMode.OpenOrCreate,
                                FileAccess.ReadWrite,
                                FileShare.None,
                                bufferSize: 1,
                                FileOptions.WriteThrough);
                    }
                    catch (IOException exception)
                    {
                        return new FileOperationProcessLockAcquireResult(
                            FileOperationProcessLockAcquireState.Unavailable,
                            "Der exklusive Dateisicherheits-Lock ist bereits von einem anderen Prozess belegt "
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

                sharedState.LeaseCount++;

                _sharedState =
                    sharedState;

                Volatile.Write(
                    ref _isHeld,
                    1);

                return new FileOperationProcessLockAcquireResult(
                    FileOperationProcessLockAcquireState.Acquired);
            }
            finally
            {
                sharedState.Gate.Release();
            }
        }
        finally
        {
            _instanceGate.Release();
        }
    }

    public void Dispose()
    {
        _instanceGate.Wait();

        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _designerBypassHeld = false;

            var sharedState =
                _sharedState;

            _sharedState = null;

            if (sharedState is not null)
            {
                sharedState.Gate.Wait();

                try
                {
                    if (sharedState.LeaseCount > 0)
                    {
                        sharedState.LeaseCount--;
                    }

                    if (sharedState.LeaseCount == 0)
                    {
                        sharedState.LockStream?.Dispose();
                        sharedState.LockStream = null;
                    }
                }
                finally
                {
                    sharedState.Gate.Release();
                }
            }

            Volatile.Write(
                ref _isHeld,
                0);
        }
        finally
        {
            _instanceGate.Release();
        }
    }

    private static bool IsAvaloniaDesignerHost()
    {
        foreach (var argument in Environment.GetCommandLineArgs())
        {
            var fileName =
                Path.GetFileName(argument);

            if (string.Equals(
                    fileName,
                    "Avalonia.Designer.HostApp.dll",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class SharedLockState
    {
        public SemaphoreSlim Gate { get; } =
            new(1, 1);

        public FileStream? LockStream { get; set; }

        public int LeaseCount { get; set; }
    }
}
