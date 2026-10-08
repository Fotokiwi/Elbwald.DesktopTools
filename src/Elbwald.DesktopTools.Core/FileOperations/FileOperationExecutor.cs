using System.Security.Cryptography;
using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.Recovery;

namespace Elbwald.DesktopTools.Core.FileOperations;

public sealed class FileOperationExecutor : IFileOperationExecutor
{
    private const int CopyBufferSize = 1024 * 1024;

    private readonly IFileOperationSafetyChecker _safetyChecker;
    private readonly IOperationJournal _journal;
    private readonly IPersistentRecoveryStore _persistentRecoveryStore;
    private readonly IStartupRecoveryService _startupRecoveryService;
    private readonly IFileOperationProcessLock _processLock;

    public FileOperationExecutor(
        IFileOperationSafetyChecker safetyChecker,
        IOperationJournal journal,
        IPersistentRecoveryStore persistentRecoveryStore,
        IStartupRecoveryService startupRecoveryService,
        IFileOperationProcessLock processLock)
    {
        ArgumentNullException.ThrowIfNull(safetyChecker);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(persistentRecoveryStore);
        ArgumentNullException.ThrowIfNull(startupRecoveryService);
        ArgumentNullException.ThrowIfNull(processLock);

        _safetyChecker = safetyChecker;
        _journal = journal;
        _persistentRecoveryStore = persistentRecoveryStore;
        _startupRecoveryService = startupRecoveryService;
        _processLock = processLock;
    }

    public async Task<FileOperationExecutionResult> ExecuteAsync(
        FileOperationPlan plan,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.CanExecute)
        {
            throw new InvalidOperationException(
                "Ein Dateioperationsplan mit Konflikten darf nicht ausgeführt werden.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateResult(
                FileOperationExecutionState.Cancelled,
                Array.Empty<FileOperationItemResult>(),
                plan,
                transactionId: null);
        }

        FileOperationProcessLockAcquireResult processLockResult;

        try
        {
            processLockResult =
                await _processLock.TryAcquireAsync(
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return CreateResult(
                FileOperationExecutionState.Cancelled,
                Array.Empty<FileOperationItemResult>(),
                plan,
                transactionId: null);
        }

        if (!processLockResult.IsAcquired)
        {
            return CreateResult(
                FileOperationExecutionState.BlockedByProcessLock,
                Array.Empty<FileOperationItemResult>(),
                plan,
                transactionId: null,
                errorMessage:
                    processLockResult.Message
                    ?? "Der exklusive Dateisicherheits-Lock konnte nicht "
                       + "übernommen werden.");
        }

        StartupRecoverySnapshot recoverySnapshot;

        try
        {
            recoverySnapshot =
                await _startupRecoveryService.ScanAsync(
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return CreateResult(
                FileOperationExecutionState.Cancelled,
                Array.Empty<FileOperationItemResult>(),
                plan,
                transactionId: null);
        }

        if (!recoverySnapshot.CanStartFileOperations)
        {
            return CreateResult(
                FileOperationExecutionState.BlockedByRecovery,
                Array.Empty<FileOperationItemResult>(),
                plan,
                transactionId: null,
                errorMessage:
                    BuildRecoveryBlockMessage(recoverySnapshot));
        }

        var transactionId = Guid.NewGuid();

        var initialSafetyCheck = _safetyChecker.Check(plan);

        if (!initialSafetyCheck.IsSafe)
        {
            return CreateResult(
                FileOperationExecutionState.Failed,
                Array.Empty<FileOperationItemResult>(),
                plan,
                transactionId,
                initialSafetyCheck.Issues);
        }

        var results = new List<FileOperationItemResult>();

        foreach (var operation in plan.Operations.OrderBy(item => item.Index))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                await TryWriteJournalAsync(
                    transactionId,
                    OperationJournalState.Cancelled,
                    operation,
                    recovery: null,
                    "Ausführung vor Beginn der nächsten Operation abgebrochen.");

                return CreateResult(
                    FileOperationExecutionState.Cancelled,
                    results,
                    plan,
                    transactionId);
            }

            var operationSafetyCheck =
                _safetyChecker.CheckOperation(operation);

            if (!operationSafetyCheck.IsSafe)
            {
                await TryWriteJournalAsync(
                    transactionId,
                    OperationJournalState.Failed,
                    operation,
                    recovery: null,
                    "Laufzeit-Sicherheitsprüfung fehlgeschlagen.");

                return CreateResult(
                    FileOperationExecutionState.Failed,
                    results,
                    plan,
                    transactionId,
                    operationSafetyCheck.Issues);
            }

            RecoveryHandle? recovery = null;
            var destructiveStepStarted = false;

            try
            {
                ValidateRuntimeState(operation);
                EnsureDestinationDirectory(operation.DestinationPath);

                await WriteJournalAsync(
                    transactionId,
                    OperationJournalState.Prepared,
                    operation,
                    recovery: null,
                    message: "Operation vorbereitet.",
                    cancellationToken);

                if (operation.Kind == FileOperationKind.Move)
                {
                    recovery = await _persistentRecoveryStore.PreserveAsync(
                        operation.SourcePath,
                        cancellationToken);

                    await WriteJournalAsync(
                        transactionId,
                        OperationJournalState.Prepared,
                        operation,
                        recovery,
                        "Persistente Recovery-Sicherung erstellt.",
                        cancellationToken);
                }

                await WriteJournalAsync(
                    transactionId,
                    OperationJournalState.Executing,
                    operation,
                    recovery,
                    "Dateioperation wird ausgeführt.",
                    cancellationToken);

                if (operation.Kind == FileOperationKind.Move)
                {
                    destructiveStepStarted = true;
                }

                await ExecuteOperationAsync(
                    operation,
                    cancellationToken);

                await WriteJournalAsync(
                    transactionId,
                    OperationJournalState.Committed,
                    operation,
                    recovery,
                    "Dateioperation wurde auf dem Dateisystem committed.",
                    CancellationToken.None);

                await WriteJournalAsync(
                    transactionId,
                    OperationJournalState.Completed,
                    operation,
                    recovery,
                    "Dateioperation vollständig abgeschlossen.",
                    CancellationToken.None);

                results.Add(new FileOperationItemResult(
                    operation,
                    FileOperationItemStatus.Completed));

                progress?.Report(new FileOperationProgress(
                    results.Count,
                    plan.Operations.Count,
                    operation));

                if (recovery is not null)
                {
                    await TryReleaseRecoveryAsync(recovery);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                var recoveryRequired =
                    destructiveStepStarted
                    && RequiresRecovery(operation);

                var journalState = recoveryRequired
                    ? OperationJournalState.RecoveryRequired
                    : OperationJournalState.Cancelled;

                var journalWritten = await TryWriteJournalAsync(
                    transactionId,
                    journalState,
                    operation,
                    recovery,
                    recoveryRequired
                        ? "Abbruch nach möglicher Änderung am Dateisystem. Recovery erforderlich."
                        : "Operation sicher abgebrochen.");

                if (!recoveryRequired
                    && journalWritten
                    && recovery is not null)
                {
                    await TryReleaseRecoveryAsync(recovery);
                }

                return CreateResult(
                    recoveryRequired
                        ? FileOperationExecutionState.RecoveryRequired
                        : FileOperationExecutionState.Cancelled,
                    results,
                    plan,
                    transactionId,
                    errorMessage: recoveryRequired
                        ? "Die Operation wurde abgebrochen, nachdem sich der Dateisystemzustand geändert haben könnte."
                        : null);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or NotSupportedException
                    or InvalidOperationException
                    or CryptographicException)
            {
                var recoveryRequired =
                    destructiveStepStarted
                    && RequiresRecovery(operation);

                var journalState = recoveryRequired
                    ? OperationJournalState.RecoveryRequired
                    : OperationJournalState.Failed;

                var journalWritten = await TryWriteJournalAsync(
                    transactionId,
                    journalState,
                    operation,
                    recovery,
                    exception.Message);

                if (!recoveryRequired
                    && journalWritten
                    && recovery is not null)
                {
                    await TryReleaseRecoveryAsync(recovery);
                }

                results.Add(new FileOperationItemResult(
                    operation,
                    FileOperationItemStatus.Failed,
                    exception.Message));

                return CreateResult(
                    recoveryRequired
                        ? FileOperationExecutionState.RecoveryRequired
                        : FileOperationExecutionState.Failed,
                    results,
                    plan,
                    transactionId,
                    errorMessage: exception.Message);
            }
        }

        return CreateResult(
            FileOperationExecutionState.Completed,
            results,
            plan,
            transactionId);
    }

    private async Task WriteJournalAsync(
        Guid transactionId,
        OperationJournalState state,
        FileOperationPlanItem operation,
        RecoveryHandle? recovery,
        string? message,
        CancellationToken cancellationToken)
    {
        await _journal.AppendAsync(
            OperationJournalEntry.Create(
                transactionId,
                state,
                operation.Kind.ToString(),
                operation.SourcePath,
                operation.DestinationPath,
                operation.Index,
                recovery,
                message),
            cancellationToken);
    }

    private async Task<bool> TryWriteJournalAsync(
        Guid transactionId,
        OperationJournalState state,
        FileOperationPlanItem operation,
        RecoveryHandle? recovery,
        string? message)
    {
        try
        {
            await WriteJournalAsync(
                transactionId,
                state,
                operation,
                recovery,
                message,
                CancellationToken.None);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task TryReleaseRecoveryAsync(
        RecoveryHandle recovery)
    {
        try
        {
            await _persistentRecoveryStore.ReleaseAsync(
                recovery,
                CancellationToken.None);
        }
        catch
        {
            // Eine übrig gebliebene Recovery-Datei ist sicherer als das
            // Verbergen eines ansonsten erfolgreich abgeschlossenen Vorgangs.
        }
    }

    private static bool RequiresRecovery(
        FileOperationPlanItem operation)
    {
        if (operation.Kind != FileOperationKind.Move)
        {
            return false;
        }

        var sourceExists = File.Exists(operation.SourcePath);
        var destinationExists = File.Exists(operation.DestinationPath);

        return !sourceExists || destinationExists;
    }

    private static void ValidateRuntimeState(
        FileOperationPlanItem operation)
    {
        if (!File.Exists(operation.SourcePath))
        {
            throw new FileNotFoundException(
                "Die Quelldatei existiert nicht mehr.",
                operation.SourcePath);
        }

        if (File.Exists(operation.DestinationPath)
            || Directory.Exists(operation.DestinationPath))
        {
            throw new IOException(
                $"Das Ziel '{operation.DestinationPath}' existiert bereits. "
                + "Es wird nicht überschrieben.");
        }
    }

    private static void EnsureDestinationDirectory(
        string destinationPath)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath);

        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }
    }

    private static Task ExecuteOperationAsync(
        FileOperationPlanItem operation,
        CancellationToken cancellationToken)
    {
        return operation.Kind switch
        {
            FileOperationKind.Move => ExecuteMoveAsync(
                operation,
                cancellationToken),

            FileOperationKind.Copy => ExecuteSafeCopyAsync(
                operation.SourcePath,
                operation.DestinationPath,
                cancellationToken),

            _ => throw new InvalidOperationException(
                $"Nicht unterstützte Dateioperation: {operation.Kind}.")
        };
    }

    private static Task ExecuteMoveAsync(
        FileOperationPlanItem operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        File.Move(
            operation.SourcePath,
            operation.DestinationPath);

        return Task.CompletedTask;
    }

    private static async Task ExecuteSafeCopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var destinationDirectory =
            Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException(
                "Das Zielverzeichnis konnte nicht bestimmt werden.");

        var destinationFileName = Path.GetFileName(destinationPath);

        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{destinationFileName}.elbwald-{Guid.NewGuid():N}.partial");

        var sourceInfo = new FileInfo(sourcePath);
        var originalLength = sourceInfo.Length;
        var originalLastWriteTimeUtc = sourceInfo.LastWriteTimeUtc;

        byte[] sourceHash;

        try
        {
            sourceHash = await CopyToTemporaryFileAsync(
                sourcePath,
                temporaryPath,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var temporaryInfo = new FileInfo(temporaryPath);

            if (temporaryInfo.Length != originalLength)
            {
                throw new IOException(
                    "Die temporäre Kopie hat eine unerwartete Dateigröße.");
            }

            var destinationHash = await ComputeSha256Async(
                temporaryPath,
                cancellationToken);

            if (!CryptographicOperations.FixedTimeEquals(
                    sourceHash,
                    destinationHash))
            {
                throw new CryptographicException(
                    "Die SHA-256-Prüfung der kopierten Datei ist fehlgeschlagen.");
            }

            File.SetLastWriteTimeUtc(
                temporaryPath,
                originalLastWriteTimeUtc);

            if (File.Exists(destinationPath)
                || Directory.Exists(destinationPath))
            {
                throw new IOException(
                    $"Das Ziel '{destinationPath}' wurde während des Kopiervorgangs "
                    + "erstellt. Die temporäre Datei wird verworfen.");
            }

            File.Move(
                temporaryPath,
                destinationPath);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static async Task<byte[]> CopyToTemporaryFileAsync(
        string sourcePath,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using var destination = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);

        var buffer = new byte[CopyBufferSize];

        while (true)
        {
            var bytesRead = await source.ReadAsync(
                buffer.AsMemory(0, buffer.Length),
                cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            hash.AppendData(
                buffer,
                0,
                bytesRead);

            await destination.WriteAsync(
                buffer.AsMemory(0, bytesRead),
                cancellationToken);
        }

        await destination.FlushAsync(cancellationToken);
        destination.Flush(flushToDisk: true);

        return hash.GetHashAndReset();
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
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);

        var buffer = new byte[CopyBufferSize];

        while (true)
        {
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(0, buffer.Length),
                cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            hash.AppendData(
                buffer,
                0,
                bytesRead);
        }

        return hash.GetHashAndReset();
    }

    private static void TryDeleteTemporaryFile(
        string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch
        {
            // Cleanup failures must never hide the original copy error.
        }
    }

    private static string BuildRecoveryBlockMessage(
        StartupRecoverySnapshot snapshot)
    {
        return snapshot.State switch
        {
            StartupRecoveryState.AttentionRequired =>
                "Neue Dateioperationen sind gesperrt, weil die Recovery-Prüfung "
                + $"offene oder unklare Zustände gefunden hat. "
                + $"Offene Transaktionen: {snapshot.Candidates.Count}; "
                + $"beschädigte Journalzeilen: {snapshot.CorruptJournalLineCount}.",

            StartupRecoveryState.ScanFailed =>
                "Neue Dateioperationen sind gesperrt, weil die Recovery-Prüfung "
                + "nicht sicher abgeschlossen werden konnte. "
                + (string.IsNullOrWhiteSpace(snapshot.ErrorMessage)
                    ? "Das Journal muss zuerst zuverlässig geprüft werden."
                    : snapshot.ErrorMessage),

            StartupRecoveryState.NotScanned =>
                "Neue Dateioperationen sind gesperrt, solange die Recovery-Prüfung "
                + "noch nicht abgeschlossen wurde.",

            _ =>
                "Neue Dateioperationen sind aufgrund des aktuellen "
                + "Recovery-Zustands gesperrt."
        };
    }

    private static FileOperationExecutionResult CreateResult(
        FileOperationExecutionState state,
        IEnumerable<FileOperationItemResult> results,
        FileOperationPlan plan,
        Guid? transactionId,
        IEnumerable<FileOperationSafetyIssue>? safetyIssues = null,
        string? errorMessage = null)
    {
        return new FileOperationExecutionResult(
            state,
            results,
            plan.Operations.Count,
            safetyIssues,
            transactionId,
            errorMessage);
    }
}
