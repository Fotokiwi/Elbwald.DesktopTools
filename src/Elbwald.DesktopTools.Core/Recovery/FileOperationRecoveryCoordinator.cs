using System.Security.Cryptography;
using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.Recovery;

namespace Elbwald.DesktopTools.Core.Recovery;

public sealed class FileOperationRecoveryCoordinator
    : IFileOperationRecoveryCoordinator
{
    private const int HashBufferSize = 1024 * 1024;

    private readonly IFileOperationRecoveryInspector _inspector;
    private readonly IOperationJournal _journal;
    private readonly IPersistentRecoveryStore _persistentRecoveryStore;
    private readonly IFileOperationProcessLock _processLock;

    public FileOperationRecoveryCoordinator(
        IFileOperationRecoveryInspector inspector,
        IOperationJournal journal,
        IPersistentRecoveryStore persistentRecoveryStore,
        IFileOperationProcessLock processLock)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(persistentRecoveryStore);
        ArgumentNullException.ThrowIfNull(processLock);

        _inspector = inspector;
        _journal = journal;
        _persistentRecoveryStore = persistentRecoveryStore;
        _processLock = processLock;
    }

    public async Task<IReadOnlyList<FileOperationRecoveryResult>>
        RecoverPendingAsync(
            CancellationToken cancellationToken = default)
    {
        var processLockResult =
            await _processLock.TryAcquireAsync(
                cancellationToken);

        if (!processLockResult.IsAcquired)
        {
            throw new InvalidOperationException(
                processLockResult.Message
                ?? "Der exklusive Dateisicherheits-Lock konnte nicht "
                   + "übernommen werden.");
        }

        var candidates = await _inspector.InspectAsync(
            cancellationToken);

        var results =
            new List<FileOperationRecoveryResult>(
                candidates.Count);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                results.Add(
                    await RecoverCandidateAsync(
                        candidate,
                        cancellationToken));
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or InvalidOperationException
                    or NotSupportedException
                    or CryptographicException)
            {
                results.Add(
                    new FileOperationRecoveryResult(
                        candidate,
                        FileOperationRecoveryOutcome.Failed,
                        $"Recovery konnte nicht sicher abgeschlossen werden: "
                        + exception.Message,
                        RecoveryRetained:
                            candidate.Recovery is not null));
            }
        }

        return results;
    }

    private Task<FileOperationRecoveryResult> RecoverCandidateAsync(
        FileOperationRecoveryCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(candidate.SourcePath)
            || Directory.Exists(candidate.DestinationPath))
        {
            return Task.FromResult(
                Manual(
                    candidate,
                    "Quelle oder Ziel ist inzwischen ein Verzeichnis. "
                    + "Es wird nichts automatisch verändert."));
        }

        if (string.Equals(
                candidate.OperationKind,
                "Move",
                StringComparison.OrdinalIgnoreCase))
        {
            return RecoverMoveAsync(
                candidate,
                cancellationToken);
        }

        if (string.Equals(
                candidate.OperationKind,
                "Copy",
                StringComparison.OrdinalIgnoreCase))
        {
            return RecoverCopyAsync(
                candidate,
                cancellationToken);
        }

        return Task.FromResult(
            Manual(
                candidate,
                $"Der Operationstyp '{candidate.OperationKind}' "
                + "wird von der automatischen Recovery noch nicht unterstützt."));
    }

    private async Task<FileOperationRecoveryResult> RecoverMoveAsync(
        FileOperationRecoveryCandidate candidate,
        CancellationToken cancellationToken)
    {
        var sourceExists = File.Exists(candidate.SourcePath);
        var destinationExists = File.Exists(candidate.DestinationPath);

        if (sourceExists && destinationExists)
        {
            return Manual(
                candidate,
                "Quelle und Ziel existieren gleichzeitig. "
                + "Dieser Zustand ist absichtlich nicht automatisch auflösbar.");
        }

        if (sourceExists)
        {
            await AppendTerminalStateAsync(
                candidate,
                OperationJournalState.Recovered,
                "Recovery geprüft: Die ursprüngliche Quelldatei ist vorhanden. "
                + "Es war keine Wiederherstellung notwendig.",
                cancellationToken);

            var retained =
                !await TryReleaseRecoveryAsync(
                    candidate.Recovery);

            return new FileOperationRecoveryResult(
                candidate,
                FileOperationRecoveryOutcome.Recovered,
                "Die Quelldatei ist unverändert vorhanden. "
                + "Die unvollständige Transaktion wurde sicher abgeschlossen.",
                retained);
        }

        if (candidate.Recovery is null)
        {
            return Manual(
                candidate,
                "Die Quelldatei fehlt und es existiert keine persistente "
                + "Recovery-Sicherung. Automatische Änderungen sind gesperrt.");
        }

        var recoveryValid =
            await _persistentRecoveryStore.VerifyAsync(
                candidate.Recovery,
                cancellationToken);

        if (!recoveryValid)
        {
            return new FileOperationRecoveryResult(
                candidate,
                FileOperationRecoveryOutcome.Failed,
                "Die persistente Recovery-Sicherung fehlt oder hat die "
                + "Integritätsprüfung nicht bestanden. Es wurde nichts verändert.",
                RecoveryRetained: true);
        }

        // RestoreAsync refuses to overwrite. The destination, if present,
        // is intentionally left untouched. Safety is more important than
        // automatically removing a possibly valid second copy.
        await _persistentRecoveryStore.RestoreAsync(
            candidate.Recovery,
            candidate.SourcePath,
            cancellationToken);

        await AppendTerminalStateAsync(
            candidate,
            OperationJournalState.Recovered,
            destinationExists
                ? "Quelldatei aus persistenter Recovery wiederhergestellt. "
                  + "Das vorhandene Ziel wurde aus Sicherheitsgründen nicht gelöscht."
                : "Quelldatei aus persistenter Recovery wiederhergestellt.",
            cancellationToken);

        var recoveryRetained =
            !await TryReleaseRecoveryAsync(
                candidate.Recovery);

        return new FileOperationRecoveryResult(
            candidate,
            FileOperationRecoveryOutcome.Recovered,
            destinationExists
                ? "Die Quelle wurde wiederhergestellt. Das bereits vorhandene Ziel "
                  + "bleibt als zusätzliche Sicherheitskopie bestehen."
                : "Die ursprüngliche Quelldatei wurde erfolgreich wiederhergestellt.",
            recoveryRetained);
    }

    private async Task<FileOperationRecoveryResult> RecoverCopyAsync(
        FileOperationRecoveryCandidate candidate,
        CancellationToken cancellationToken)
    {
        var sourceExists = File.Exists(candidate.SourcePath);
        var destinationExists = File.Exists(candidate.DestinationPath);

        if (!sourceExists)
        {
            return Manual(
                candidate,
                "Bei einer Copy-Operation darf die Quelle nicht verschwinden. "
                + "Der Zustand muss manuell geprüft werden.");
        }

        if (!destinationExists)
        {
            await AppendTerminalStateAsync(
                candidate,
                OperationJournalState.Recovered,
                "Unvollständige Copy-Operation verworfen. "
                + "Die Quelle ist vorhanden und es existiert kein endgültiges Ziel.",
                cancellationToken);

            return new FileOperationRecoveryResult(
                candidate,
                FileOperationRecoveryOutcome.Recovered,
                "Die Quelle ist vorhanden; es war keine Dateiwiederherstellung notwendig.",
                RecoveryRetained: false);
        }

        var filesMatch = await FilesAreIdenticalAsync(
            candidate.SourcePath,
            candidate.DestinationPath,
            cancellationToken);

        if (!filesMatch)
        {
            return Manual(
                candidate,
                "Quelle und Ziel existieren, sind aber nicht byteidentisch. "
                + "Das Ziel wird nicht überschrieben oder gelöscht.");
        }

        await AppendTerminalStateAsync(
            candidate,
            OperationJournalState.Completed,
            "Unvollständige Copy-Transaktion nachträglich verifiziert. "
            + "Quelle und Ziel sind byteidentisch.",
            cancellationToken);

        return new FileOperationRecoveryResult(
            candidate,
            FileOperationRecoveryOutcome.Finalized,
            "Die Kopie wurde per SHA-256 verifiziert und die Transaktion "
            + "sicher als abgeschlossen markiert.",
            RecoveryRetained: false);
    }

    private async Task AppendTerminalStateAsync(
        FileOperationRecoveryCandidate candidate,
        OperationJournalState state,
        string message,
        CancellationToken cancellationToken)
    {
        await _journal.AppendAsync(
            OperationJournalEntry.Create(
                candidate.TransactionId,
                state,
                candidate.OperationKind,
                candidate.SourcePath,
                candidate.DestinationPath,
                candidate.OperationIndex,
                candidate.Recovery,
                message),
            cancellationToken);
    }

    private async Task<bool> TryReleaseRecoveryAsync(
        RecoveryHandle? recovery)
    {
        if (recovery is null)
        {
            return true;
        }

        try
        {
            await _persistentRecoveryStore.ReleaseAsync(
                recovery,
                CancellationToken.None);

            return true;
        }
        catch
        {
            // A stale recovery file is harmless and intentionally preferred
            // over turning a successful recovery into another destructive step.
            return false;
        }
    }

    private static async Task<bool> FilesAreIdenticalAsync(
        string firstPath,
        string secondPath,
        CancellationToken cancellationToken)
    {
        var firstInfo = new FileInfo(firstPath);
        var secondInfo = new FileInfo(secondPath);

        if (firstInfo.Length != secondInfo.Length)
        {
            return false;
        }

        var firstHash = await ComputeSha256Async(
            firstPath,
            cancellationToken);

        var secondHash = await ComputeSha256Async(
            secondPath,
            cancellationToken);

        return CryptographicOperations.FixedTimeEquals(
            firstHash,
            secondHash);
    }

    private static async Task<byte[]> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            HashBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await SHA256.HashDataAsync(
            stream,
            cancellationToken);
    }

    private static FileOperationRecoveryResult Manual(
        FileOperationRecoveryCandidate candidate,
        string message)
    {
        return new FileOperationRecoveryResult(
            candidate,
            FileOperationRecoveryOutcome.ManualActionRequired,
            message,
            RecoveryRetained:
                candidate.Recovery is not null);
    }
}
