using System.Security.Cryptography;
using Elbwald.DesktopTools.Contracts.Recovery;

namespace Elbwald.DesktopTools.Core.Recovery;

public sealed class MemoryRecoveryStore : IRecoveryStore
{
    private readonly RecoveryOptions _options;
    private readonly object _gate = new();
    private readonly Dictionary<string, MemoryEntry> _entries =
        new(StringComparer.Ordinal);

    private long _usedBytes;

    public MemoryRecoveryStore(
        RecoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    public long UsedBytes
    {
        get
        {
            lock (_gate)
            {
                return _usedBytes;
            }
        }
    }

    public bool CanPreserve(long length)
    {
        if (length < 0
            || length > _options.MaxSingleMemoryItemBytes
            || length > int.MaxValue)
        {
            return false;
        }

        lock (_gate)
        {
            return length <= _options.MaxMemoryBytes - _usedBytes;
        }
    }

    public async Task<RecoveryHandle> PreserveAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fullSourcePath = Path.GetFullPath(sourcePath);

        await using var stream = new FileStream(
            fullSourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var length = stream.Length;

        if (!CanPreserve(length))
        {
            throw new InvalidOperationException(
                "Die Datei passt nicht mehr sicher in den konfigurierten RAM-Recovery-Cache.");
        }

        var buffer = new byte[checked((int)length)];
        var offset = 0;

        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(offset),
                cancellationToken);

            if (read == 0)
            {
                throw new EndOfStreamException(
                    "Die Quelldatei endete während der Recovery-Sicherung unerwartet.");
            }

            offset += read;
        }

        if (await stream.ReadAsync(
                new byte[1],
                cancellationToken) != 0)
        {
            throw new IOException(
                "Die Quelldatei wurde während der Recovery-Sicherung größer.");
        }

        var fileInfo = new FileInfo(fullSourcePath);
        var sha256 = Convert.ToHexString(
            SHA256.HashData(buffer));

        var id = Guid.NewGuid().ToString("N");
        var handle = new RecoveryHandle(
            id,
            fullSourcePath,
            length,
            sha256,
            fileInfo.LastWriteTimeUtc,
            DateTimeOffset.UtcNow,
            RecoveryStorageKind.Memory);

        lock (_gate)
        {
            if (length > _options.MaxMemoryBytes - _usedBytes)
            {
                CryptographicOperations.ZeroMemory(buffer);

                throw new InvalidOperationException(
                    "Der RAM-Recovery-Cache wurde zwischenzeitlich ausgelastet.");
            }

            _entries.Add(
                id,
                new MemoryEntry(handle, buffer));

            _usedBytes += length;
        }

        return handle;
    }

    public async Task RestoreAsync(
        RecoveryHandle handle,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        MemoryEntry entry;

        lock (_gate)
        {
            if (!_entries.TryGetValue(handle.Id, out entry!))
            {
                throw new FileNotFoundException(
                    "Der RAM-Recovery-Eintrag wurde nicht gefunden.",
                    handle.Id);
            }
        }

        var fullDestinationPath = Path.GetFullPath(destinationPath);

        if (File.Exists(fullDestinationPath)
            || Directory.Exists(fullDestinationPath))
        {
            throw new IOException(
                $"Das Wiederherstellungsziel '{fullDestinationPath}' existiert bereits.");
        }

        var destinationDirectory =
            Path.GetDirectoryName(fullDestinationPath)
            ?? throw new InvalidOperationException(
                "Das Wiederherstellungsziel hat kein gültiges Verzeichnis.");

        Directory.CreateDirectory(destinationDirectory);

        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(fullDestinationPath)}.elbwald-recovery-{Guid.NewGuid():N}.partial");

        try
        {
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             1024 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await destination.WriteAsync(
                    entry.Data.AsMemory(),
                    cancellationToken);

                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            var restoredHash = await ComputeSha256Async(
                temporaryPath,
                cancellationToken);

            if (!string.Equals(
                    restoredHash,
                    handle.Sha256,
                    StringComparison.Ordinal))
            {
                throw new CryptographicException(
                    "Die wiederhergestellte Datei stimmt nicht mit der Recovery-Sicherung überein.");
            }

            File.SetLastWriteTimeUtc(
                temporaryPath,
                handle.LastWriteTimeUtc);

            File.Move(
                temporaryPath,
                fullDestinationPath);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public Task ReleaseAsync(
        RecoveryHandle handle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_entries.Remove(
                    handle.Id,
                    out var entry))
            {
                _usedBytes -= entry.Data.LongLength;
                CryptographicOperations.ZeroMemory(entry.Data);
            }
        }

        return Task.CompletedTask;
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken);

        return Convert.ToHexString(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cleanup must not hide the original recovery error.
        }
    }

    private sealed record MemoryEntry(
        RecoveryHandle Handle,
        byte[] Data);
}
