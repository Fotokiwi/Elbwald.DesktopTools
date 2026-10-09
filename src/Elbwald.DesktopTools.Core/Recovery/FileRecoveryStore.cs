using System.Security.Cryptography;
using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Core.Storage;

namespace Elbwald.DesktopTools.Core.Recovery;

public sealed class FileRecoveryStore : IPersistentRecoveryStore
{
    private const int BufferSize = 1024 * 1024;

    private readonly RecoveryOptions _options;

    public FileRecoveryStore(
        RecoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    public async Task<RecoveryHandle> PreserveAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var sourceInfo = new FileInfo(fullSourcePath);

        if (!sourceInfo.Exists)
        {
            throw new FileNotFoundException(
                "Die Quelldatei für die Recovery-Sicherung existiert nicht.",
                fullSourcePath);
        }

        var recoveryDirectory =
            Path.GetFullPath(_options.PersistentDirectory);

        Directory.CreateDirectory(recoveryDirectory);

        EnsureFreeSpace(
            recoveryDirectory,
            sourceInfo.Length);

        var id = Guid.NewGuid().ToString("N");
        var finalPath = Path.Combine(
            recoveryDirectory,
            $"{id}.recovery");

        var temporaryPath = Path.Combine(
            recoveryDirectory,
            $".{id}.partial");

        try
        {
            var sourceHash = await CopyAndHashAsync(
                fullSourcePath,
                temporaryPath,
                cancellationToken);

            var storedHash = await ComputeSha256Async(
                temporaryPath,
                cancellationToken);

            if (!string.Equals(
                    sourceHash,
                    storedHash,
                    StringComparison.Ordinal))
            {
                throw new CryptographicException(
                    "Die persistente Recovery-Kopie hat die Integritätsprüfung nicht bestanden.");
            }

            var temporaryInfo = new FileInfo(temporaryPath);

            if (temporaryInfo.Length != sourceInfo.Length)
            {
                throw new IOException(
                    "Die persistente Recovery-Kopie hat eine unerwartete Dateigröße.");
            }

            File.Move(
                temporaryPath,
                finalPath);

            return new RecoveryHandle(
                id,
                fullSourcePath,
                sourceInfo.Length,
                sourceHash,
                sourceInfo.LastWriteTimeUtc,
                DateTimeOffset.UtcNow,
                RecoveryStorageKind.PersistentFile,
                finalPath);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public async Task<bool> VerifyAsync(
        RecoveryHandle handle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);

        var storedPath = ResolveStoredPath(handle);

        if (!File.Exists(storedPath))
        {
            return false;
        }

        var fileInfo = new FileInfo(storedPath);

        if (fileInfo.Length != handle.Length)
        {
            return false;
        }

        var storedHash = await ComputeSha256Async(
            storedPath,
            cancellationToken);

        return string.Equals(
            storedHash,
            handle.Sha256,
            StringComparison.Ordinal);
    }

    public async Task RestoreAsync(
        RecoveryHandle handle,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var storedPath = ResolveStoredPath(handle);

        if (!File.Exists(storedPath))
        {
            throw new FileNotFoundException(
                "Die persistente Recovery-Datei wurde nicht gefunden.",
                storedPath);
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

        EnsureFreeSpace(
            destinationDirectory,
            handle.Length);

        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(fullDestinationPath)}.elbwald-recovery-{Guid.NewGuid():N}.partial");

        try
        {
            var restoredHash = await CopyAndHashAsync(
                storedPath,
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

        if (handle.StorageKind != RecoveryStorageKind.PersistentFile)
        {
            return Task.CompletedTask;
        }

        var storedPath = ResolveStoredPath(handle);

        if (File.Exists(storedPath))
        {
            File.Delete(storedPath);
        }

        return Task.CompletedTask;
    }

    private string ResolveStoredPath(
        RecoveryHandle handle)
    {
        if (handle.StorageKind != RecoveryStorageKind.PersistentFile)
        {
            throw new InvalidOperationException(
                "Der Recovery-Eintrag gehört nicht zum persistenten Recovery Store.");
        }

        if (!Guid.TryParseExact(
                handle.Id,
                "N",
                out _))
        {
            throw new InvalidOperationException(
                "Die Recovery-ID hat kein gültiges internes Format.");
        }

        if (string.IsNullOrWhiteSpace(handle.StorageLocation))
        {
            throw new InvalidOperationException(
                "Dem Recovery-Eintrag fehlt der persistente Speicherpfad.");
        }

        var recoveryDirectory =
            Path.GetFullPath(_options.PersistentDirectory);

        var expectedPath = Path.GetFullPath(
            Path.Combine(
                recoveryDirectory,
                $"{handle.Id}.recovery"));

        var suppliedPath =
            Path.GetFullPath(handle.StorageLocation);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!string.Equals(
                expectedPath,
                suppliedPath,
                comparison))
        {
            throw new InvalidOperationException(
                "Der Recovery-Pfad liegt nicht am erwarteten internen Speicherort.");
        }

        return expectedPath;
    }

    private async Task<string> CopyAndHashAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        FileStream source;

        try
        {
            source =
                new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    BufferSize,
                    FileOptions.Asynchronous
                    | FileOptions.SequentialScan);
        }
        catch (IOException exception)
        {
            throw new SourceReadIOException(
                sourcePath,
                "Öffnen für die Recovery-Sicherung",
                exception);
        }

        await using var sourceStream =
            source;

        await using var destination =
            new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous
                | FileOptions.SequentialScan);

        using var hash =
            IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);

            var buffer =
                new byte[BufferSize];

            while (true)
            {
                int bytesRead;

                try
                {
                    bytesRead =
                        await sourceStream.ReadAsync(
                            buffer.AsMemory(
                                0,
                                buffer.Length),
                            cancellationToken);
                }
                catch (IOException exception)
                {
                    throw new SourceReadIOException(
                        sourcePath,
                        "Lesen für die Recovery-Sicherung",
                        exception);
                }

                if (bytesRead == 0)
                {
                    break;
                }

                hash.AppendData(
                    buffer,
                    0,
                    bytesRead);

                await destination.WriteAsync(
                    buffer.AsMemory(
                        0,
                        bytesRead),
                    cancellationToken);
            }

            await destination.FlushAsync(
                cancellationToken);

            destination.Flush(
                flushToDisk: true);

        return Convert.ToHexString(
            hash.GetHashAndReset());
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
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken);

        return Convert.ToHexString(hash);
    }

    private void EnsureFreeSpace(
        string targetDirectory,
        long requiredBytes)
    {
        var volume = ResolveVolume(targetDirectory);

        long available;
        long total;

        try
        {
            available = volume.AvailableFreeSpace;
            total = volume.TotalSize;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Der freie Speicher auf '{volume.Name}' konnte nicht zuverlässig bestimmt werden.",
                exception);
        }

        var reserve = _options.CalculatePersistentReserveBytes(total);

        long requiredWithReserve;

        try
        {
            requiredWithReserve = checked(
                requiredBytes + reserve);
        }
        catch (OverflowException)
        {
            requiredWithReserve = long.MaxValue;
        }

        if (available < requiredWithReserve)
        {
            throw new IOException(
                $"Nicht genügend sicherer freier Speicher für die Recovery-Sicherung. "
                + $"Benötigt einschließlich Reserve: {requiredWithReserve} Bytes, "
                + $"verfügbar: {available} Bytes.");
        }
    }

    private static DriveInfo ResolveVolume(
        string path)
    {
        var fullPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        DriveInfo? bestMatch = null;
        var bestLength = -1;

        foreach (var drive in DriveInfo.GetDrives())
        {
            string root;

            try
            {
                root = Path.GetFullPath(
                    drive.RootDirectory.FullName);
            }
            catch
            {
                continue;
            }

            var normalizedRoot = root.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            if (string.IsNullOrEmpty(normalizedRoot))
            {
                normalizedRoot =
                    Path.DirectorySeparatorChar.ToString();
            }

            var rootWithSeparator =
                normalizedRoot == Path.DirectorySeparatorChar.ToString()
                    ? normalizedRoot
                    : normalizedRoot + Path.DirectorySeparatorChar;

            var matches =
                string.Equals(
                    fullPath,
                    normalizedRoot,
                    comparison)
                || fullPath.StartsWith(
                    rootWithSeparator,
                    comparison);

            if (matches && root.Length > bestLength)
            {
                bestMatch = drive;
                bestLength = root.Length;
            }
        }

        return bestMatch
            ?? throw new IOException(
                $"Für '{fullPath}' konnte kein Datenträger bestimmt werden.");
    }

    private static void TryDelete(
        string path)
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
            // Leaving a recovery file behind is safer than hiding the
            // original error. Startup cleanup/recovery follows later.
        }
    }
}
