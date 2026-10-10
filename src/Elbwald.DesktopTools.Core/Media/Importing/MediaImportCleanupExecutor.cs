using System.Security.Cryptography;
using System.Text.Json;
using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Media.Importing;
using Elbwald.DesktopTools.Contracts.Paths;
using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Core.Media.Importing;

public sealed class MediaImportCleanupExecutor : IMediaImportCleanupExecutor
{
    private readonly IStorageVolumeProvider _volumeProvider;
    private readonly IFileOperationProcessLock _processLock;
    private readonly IStartupRecoveryService _startupRecoveryService;
    private readonly IAppPaths _appPaths;

    public MediaImportCleanupExecutor(
        IStorageVolumeProvider volumeProvider,
        IFileOperationProcessLock processLock,
        IStartupRecoveryService startupRecoveryService,
        IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(volumeProvider);
        ArgumentNullException.ThrowIfNull(processLock);
        ArgumentNullException.ThrowIfNull(startupRecoveryService);
        ArgumentNullException.ThrowIfNull(appPaths);
        _volumeProvider = volumeProvider;
        _processLock = processLock;
        _startupRecoveryService = startupRecoveryService;
        _appPaths = appPaths;
    }

    public async Task<MediaImportCleanupExecutionResult> ExecuteAsync(
        MediaImportCleanupPlan plan,
        bool removeEmptyDirectories,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var correlationId = Guid.NewGuid();

        if (!plan.CanExecute)
        {
            return Blocked(plan, correlationId, "Der Quellbereinigungsplan ist unvollständig oder leer.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Blocked(plan, correlationId, "Quellbereinigung wurde vor der destruktiven Phase abgebrochen.");
        }

        var lockResult = await _processLock.TryAcquireAsync(cancellationToken);
        if (!lockResult.IsAcquired)
        {
            return Blocked(plan, correlationId, lockResult.Message ?? "Der exklusive Dateisicherheits-Lock konnte nicht übernommen werden.");
        }

        var recovery = await _startupRecoveryService.ScanAsync(cancellationToken);
        if (!recovery.CanStartFileOperations)
        {
            return Blocked(plan, correlationId, recovery.ErrorMessage ?? "Offene Recovery-Fälle blockieren destruktive Dateioperationen.");
        }

        var sourceVolume = _volumeProvider.FindVolumeForPath(plan.SourceRoot);
        var destinationVolume = _volumeProvider.FindVolumeForPath(plan.DestinationRoot);
        if (sourceVolume is null || !string.Equals(sourceVolume.VolumeId, plan.SourceVolumeId, StringComparison.Ordinal))
        {
            return Blocked(plan, correlationId, "Der Datenträger der Quelle hat sich seit der Freigabe geändert. Es wird nichts gelöscht.");
        }
        if (destinationVolume is null || !string.Equals(destinationVolume.VolumeId, plan.DestinationVolumeId, StringComparison.Ordinal))
        {
            return Blocked(plan, correlationId, "Der Datenträger der Import-Wartehalle hat sich seit der Freigabe geändert. Es wird nichts gelöscht.");
        }

        // Final fail-closed verification of every item before the first delete.
        foreach (var item in plan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var verificationError = await VerifyItemAsync(plan, item, cancellationToken);
            if (verificationError is not null)
            {
                return Blocked(plan, correlationId, verificationError);
            }
        }

        try
        {
            await AppendAuditAsync(new CleanupAuditRecord(
                correlationId,
                DateTimeOffset.UtcNow,
                "Prepared",
                plan.SourceRoot,
                plan.DestinationRoot,
                plan.FileCount,
                0,
                removeEmptyDirectories,
                "Alle Quelle-Ziel-Paare wurden unmittelbar vor der destruktiven Phase per SHA-256 verifiziert."),
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Blocked(plan, correlationId, "Das Sicherheitsprotokoll konnte nicht geschrieben werden. Es wird nichts gelöscht. " + exception.Message);
        }

        // From here on, cancellation is deliberately ignored. A partially deleted source
        // is less predictable than completing the already fully verified delete set.
        var deleted = 0;
        var removedDirectories = 0;
        try
        {
            foreach (var item in plan.Items)
            {
                var immediateVerificationError = await VerifyItemAsync(plan, item, CancellationToken.None);
                if (immediateVerificationError is not null)
                {
                    throw new IOException("Unmittelbare Sicherheitsprüfung vor dem Löschen fehlgeschlagen: " + immediateVerificationError);
                }

                File.Delete(item.SourcePath);
                if (File.Exists(item.SourcePath))
                {
                    throw new IOException($"Die Quelle '{item.SourcePath}' wurde nach File.Delete weiterhin gefunden.");
                }

                deleted++;
                await AppendAuditAsync(new CleanupAuditRecord(
                    correlationId,
                    DateTimeOffset.UtcNow,
                    "SourceDeleted",
                    item.SourcePath,
                    item.DestinationPath,
                    plan.FileCount,
                    deleted,
                    removeEmptyDirectories,
                    item.RelativePath),
                    CancellationToken.None);
            }

            if (removeEmptyDirectories)
            {
                removedDirectories = RemoveEmptyDirectories(plan.SourceRoot);
            }

            await AppendAuditAsync(new CleanupAuditRecord(
                correlationId,
                DateTimeOffset.UtcNow,
                "Completed",
                plan.SourceRoot,
                plan.DestinationRoot,
                plan.FileCount,
                deleted,
                removeEmptyDirectories,
                $"Quellbereinigung abgeschlossen. Leere Unterordner entfernt: {removedDirectories}."),
                CancellationToken.None);

            return new MediaImportCleanupExecutionResult(
                MediaImportCleanupExecutionState.Completed,
                plan.FileCount,
                deleted,
                removedDirectories,
                "Quellbereinigung abgeschlossen. Nur zuvor vollständig verifizierte Importdateien wurden von der Quelle entfernt.",
                correlationId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            try
            {
                await AppendAuditAsync(new CleanupAuditRecord(
                    correlationId,
                    DateTimeOffset.UtcNow,
                    "Failed",
                    plan.SourceRoot,
                    plan.DestinationRoot,
                    plan.FileCount,
                    deleted,
                    removeEmptyDirectories,
                    exception.Message),
                    CancellationToken.None);
            }
            catch
            {
                // Preserve the original destructive-operation failure for the UI.
            }

            var state = deleted > 0
                ? MediaImportCleanupExecutionState.PartialFailure
                : MediaImportCleanupExecutionState.Failed;

            return new MediaImportCleanupExecutionResult(
                state,
                plan.FileCount,
                deleted,
                removedDirectories,
                deleted > 0
                    ? "Quellbereinigung konnte nicht vollständig beendet werden. Ein Teil der verifizierten Dateien wurde bereits entfernt; die Zielkopien bleiben erhalten. Details: " + exception.Message
                    : "Quellbereinigung fehlgeschlagen, bevor eine Quelldatei entfernt wurde. Details: " + exception.Message,
                correlationId);
        }
    }

    private async Task<string?> VerifyItemAsync(
        MediaImportCleanupPlan plan,
        MediaImportCleanupPlanItem item,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!IsPathWithinRoot(item.SourcePath, plan.SourceRoot) || !IsPathWithinRoot(item.DestinationPath, plan.DestinationRoot))
            {
                return $"Pfadprüfung fehlgeschlagen: {item.RelativePath}";
            }
            if (!File.Exists(item.SourcePath) || !File.Exists(item.DestinationPath))
            {
                return $"Quelle oder Zielkopie fehlt: {item.RelativePath}";
            }

            var sourceFileVolume = _volumeProvider.FindVolumeForPath(item.SourcePath);
            var destinationFileVolume = _volumeProvider.FindVolumeForPath(item.DestinationPath);
            if (sourceFileVolume is null || !string.Equals(sourceFileVolume.VolumeId, plan.SourceVolumeId, StringComparison.Ordinal)
                || destinationFileVolume is null || !string.Equals(destinationFileVolume.VolumeId, plan.DestinationVolumeId, StringComparison.Ordinal))
            {
                return $"Unerwarteter eingebundener Datenträger: {item.RelativePath}";
            }

            if (IsReparsePoint(item.SourcePath) || IsReparsePoint(item.DestinationPath))
            {
                return $"Reparse-Point erkannt: {item.RelativePath}";
            }

            var sourceInfo = new FileInfo(item.SourcePath);
            var destinationInfo = new FileInfo(item.DestinationPath);
            if (sourceInfo.Length != item.Length || destinationInfo.Length != item.Length)
            {
                return $"Dateigröße hat sich geändert: {item.RelativePath}";
            }

            var sourceHash = await ComputeSha256HexAsync(item.SourcePath, cancellationToken);
            var destinationHash = await ComputeSha256HexAsync(item.DestinationPath, cancellationToken);
            if (!string.Equals(sourceHash, item.Sha256Hex, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(destinationHash, item.Sha256Hex, StringComparison.OrdinalIgnoreCase))
            {
                return $"SHA-256-Prüfung fehlgeschlagen: {item.RelativePath}";
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or NotSupportedException)
        {
            return $"Finale Sicherheitsprüfung fehlgeschlagen ({item.RelativePath}): {exception.Message}";
        }
    }

    private static int RemoveEmptyDirectories(string sourceRoot)
    {
        var discovered = new List<string>();
        var pending = new Stack<string>();
        pending.Push(sourceRoot);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(directory).ToArray();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }
                    discovered.Add(child);
                    pending.Push(child);
                }
                catch (IOException)
                {
                    // Optional directory cleanup: skip uncertain entries.
                }
                catch (UnauthorizedAccessException)
                {
                    // Optional directory cleanup: skip uncertain entries.
                }
            }
        }

        var removed = 0;
        foreach (var directory in discovered.OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory, recursive: false);
                    removed++;
                }
            }
            catch (IOException)
            {
                // Optional only. Never broaden or force deletion.
            }
            catch (UnauthorizedAccessException)
            {
                // Optional only. Never broaden or force deletion.
            }
        }

        return removed;
    }

    private async Task AppendAuditAsync(CleanupAuditRecord record, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_appPaths.DiagnosticsDirectory, "import-cleanup");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "source-cleanup.jsonl");
        var json = JsonSerializer.Serialize(record);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json + Environment.NewLine);

        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
            16 * 1024, FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static MediaImportCleanupExecutionResult Blocked(MediaImportCleanupPlan plan, Guid correlationId, string message) =>
        new(MediaImportCleanupExecutionState.Blocked, plan.FileCount, 0, 0, message, correlationId);

    private static bool IsReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static async Task<string> ComputeSha256HexAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static bool IsPathWithinRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedPath, normalizedRoot, comparison)
               || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private sealed record CleanupAuditRecord(
        Guid CorrelationId,
        DateTimeOffset TimestampUtc,
        string State,
        string Source,
        string Destination,
        int PlannedFileCount,
        int DeletedFileCount,
        bool RemoveEmptyDirectories,
        string Message);
}
