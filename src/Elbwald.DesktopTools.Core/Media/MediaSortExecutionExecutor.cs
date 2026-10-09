using System.Security.Cryptography;
using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;
using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.Core.Storage;

namespace Elbwald.DesktopTools.Core.Media;

public sealed class MediaSortExecutionExecutor
    : IMediaSortExecutionExecutor
{
    private const int CopyBufferSize =
        1024 * 1024;

    private readonly IMediaSortExecutionPlanner _executionPlanner;
    private readonly IFileOperationProcessLock _processLock;
    private readonly IOperationJournal _journal;
    private readonly IPersistentRecoveryStore _persistentRecoveryStore;
    private readonly IStorageHealthService _storageHealthService;

    public MediaSortExecutionExecutor(
        IMediaSortExecutionPlanner executionPlanner,
        IFileOperationProcessLock processLock,
        IOperationJournal journal,
        IPersistentRecoveryStore persistentRecoveryStore,
        IStorageHealthService storageHealthService)
    {
        ArgumentNullException.ThrowIfNull(executionPlanner);
        ArgumentNullException.ThrowIfNull(processLock);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(persistentRecoveryStore);
        ArgumentNullException.ThrowIfNull(storageHealthService);

        _executionPlanner =
            executionPlanner;

        _processLock =
            processLock;

        _journal =
            journal;

        _persistentRecoveryStore =
            persistentRecoveryStore;

        _storageHealthService =
            storageHealthService;
    }

    public async Task<MediaSortLiveExecutionResult> ExecuteAsync(
        MediaSortExecutionPlan plan,
        MediaSortExecutionConfirmation confirmation,
        IProgress<MediaSortLiveExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(confirmation);

        var confirmationError =
            ValidateConfirmation(
                plan,
                confirmation);

        if (confirmationError is not null)
        {
            return Result(
                MediaSortLiveExecutionState.BlockedByConfirmation,
                plan,
                completedOperations: 0,
                completedGroups: 0,
                errorMessage:
                    confirmationError);
        }

        if (!CanPassPlanningGate(
                plan,
                confirmation))
        {
            return Result(
                MediaSortLiveExecutionState.BlockedByPlan,
                plan,
                completedOperations: 0,
                completedGroups: 0,
                issues:
                    plan.PlanningIssues,
                errorMessage:
                    "Der Safe Execution Plan enthält noch blockierende Planungs- oder Review-Punkte.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Result(
                MediaSortLiveExecutionState.Cancelled,
                plan,
                completedOperations: 0,
                completedGroups: 0);
        }

        FileOperationProcessLockAcquireResult lockResult;

        try
        {
            lockResult =
                await _processLock.TryAcquireAsync(
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result(
                MediaSortLiveExecutionState.Cancelled,
                plan,
                completedOperations: 0,
                completedGroups: 0);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or NotSupportedException)
        {
            return Result(
                MediaSortLiveExecutionState.BlockedByProcessLock,
                plan,
                completedOperations: 0,
                completedGroups: 0,
                errorMessage:
                    "Der Dateisicherheits-Lock konnte nicht zuverlässig übernommen werden: "
                    + exception.Message);
        }

        if (!lockResult.IsAcquired)
        {
            return Result(
                MediaSortLiveExecutionState.BlockedByProcessLock,
                plan,
                completedOperations: 0,
                completedGroups: 0,
                errorMessage:
                    lockResult.Message
                    ?? "Der exklusive Dateisicherheits-Lock konnte nicht übernommen werden.");
        }

        MediaSortExecutionValidationResult validation;

        try
        {
            validation =
                await _executionPlanner.ValidateCurrentStateAsync(
                    plan,
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result(
                MediaSortLiveExecutionState.Cancelled,
                plan,
                completedOperations: 0,
                completedGroups: 0);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or InvalidOperationException)
        {
            return Result(
                MediaSortLiveExecutionState.BlockedByValidation,
                plan,
                completedOperations: 0,
                completedGroups: 0,
                errorMessage:
                    "Die erneute Sicherheitsprüfung konnte nicht zuverlässig abgeschlossen werden: "
                    + exception.Message);
        }

        if (!validation.IsValid)
        {
            return Result(
                MediaSortLiveExecutionState.BlockedByValidation,
                plan,
                completedOperations: 0,
                completedGroups: 0,
                issues:
                    validation.Issues,
                errorMessage:
                    "Die Sicherheitsbedingungen haben sich seit dem Dry Run geändert. "
                    + "Es wurde nichts ausgeführt.");
        }

        var completedOperations = 0;
        var completedGroups = 0;
        var skippedOperations = 0;
        var skippedGroups = 0;
        var problems =
            new List<MediaSortExecutionProblem>();

        foreach (var group in plan.Groups)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Result(
                    MediaSortLiveExecutionState.Cancelled,
                    plan,
                    completedOperations,
                    completedGroups,
                    skippedOperations: skippedOperations,
                    skippedGroups: skippedGroups,
                    problems: problems);
            }

            var groupResult =
                await ExecuteGroupAsync(
                    group,
                    completedOperations,
                    completedGroups,
                    skippedOperations,
                    skippedGroups,
                    plan.OperationCount,
                    plan.GroupCount,
                    progress,
                    cancellationToken);

            completedOperations +=
                groupResult.CompletedOperationCount;

            if (groupResult.State
                == GroupExecutionState.Completed)
            {
                completedGroups++;
                continue;
            }

            if (groupResult.State
                == GroupExecutionState.Skipped)
            {
                skippedOperations +=
                    group.Operations.Count;

                skippedGroups++;

                if (groupResult.Problem is not null)
                {
                    problems.Add(
                        groupResult.Problem);
                }

                var currentOperation =
                    group.Operations
                        .OrderBy(operation =>
                            operation.Index)
                        .First();

                progress?.Report(
                    new MediaSortLiveExecutionProgress(
                        completedOperations,
                        plan.OperationCount,
                        completedGroups,
                        plan.GroupCount,
                        currentOperation.SourcePath,
                        currentOperation.DestinationPath,
                        skippedOperations,
                        skippedGroups,
                        groupResult.Problem));

                continue;
            }

            return Result(
                groupResult.State
                    == GroupExecutionState.Cancelled
                        ? MediaSortLiveExecutionState.Cancelled
                        : groupResult.State
                            == GroupExecutionState.RecoveryRequired
                                ? MediaSortLiveExecutionState.RecoveryRequired
                                : MediaSortLiveExecutionState.Failed,
                plan,
                completedOperations,
                completedGroups,
                errorMessage:
                    groupResult.ErrorMessage,
                skippedOperations: skippedOperations,
                skippedGroups: skippedGroups,
                problems: problems);
        }

        return Result(
            problems.Count > 0
                ? MediaSortLiveExecutionState.CompletedWithIssues
                : MediaSortLiveExecutionState.Completed,
            plan,
            completedOperations,
            completedGroups,
            skippedOperations: skippedOperations,
            skippedGroups: skippedGroups,
            problems: problems);
    }

    private Task<GroupExecutionResult> ExecuteGroupAsync(
        MediaSortExecutionGroup group,
        int completedOperationsBeforeGroup,
        int completedGroupsBeforeGroup,
        int skippedOperationsBeforeGroup,
        int skippedGroupsBeforeGroup,
        int totalOperationCount,
        int totalGroupCount,
        IProgress<MediaSortLiveExecutionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var kinds =
            group.Operations
                .Select(operation =>
                    operation.Kind)
                .Distinct()
                .ToArray();

        if (kinds.Length != 1)
        {
            return Task.FromResult(
                new GroupExecutionResult(
                    GroupExecutionState.Failed,
                    0,
                    "Eine Safe-Sort-Gruppe enthält gemischte Copy-/Move-Operationen. "
                    + "Die Gruppe wird nicht gestartet."));
        }

        return kinds[0] switch
        {
            FileOperationKind.Copy =>
                ExecuteCopyGroupAsync(
                    group,
                    completedOperationsBeforeGroup,
                    completedGroupsBeforeGroup,
                    skippedOperationsBeforeGroup,
                    skippedGroupsBeforeGroup,
                    totalOperationCount,
                    totalGroupCount,
                    progress,
                    cancellationToken),

            FileOperationKind.Move =>
                ExecuteMoveGroupAsync(
                    group,
                    completedOperationsBeforeGroup,
                    completedGroupsBeforeGroup,
                    skippedOperationsBeforeGroup,
                    skippedGroupsBeforeGroup,
                    totalOperationCount,
                    totalGroupCount,
                    progress,
                    cancellationToken),

            _ =>
                Task.FromResult(
                    new GroupExecutionResult(
                        GroupExecutionState.Failed,
                        0,
                        $"Nicht unterstützte Safe-Sort-Operation: {kinds[0]}."))
        };
    }

    private async Task<GroupExecutionResult> ExecuteCopyGroupAsync(
        MediaSortExecutionGroup group,
        int completedOperationsBeforeGroup,
        int completedGroupsBeforeGroup,
        int skippedOperationsBeforeGroup,
        int skippedGroupsBeforeGroup,
        int totalOperationCount,
        int totalGroupCount,
        IProgress<MediaSortLiveExecutionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var records =
            group.Operations
                .OrderBy(operation =>
                    operation.Index)
                .Select(operation =>
                    new GroupOperationRecord(
                        operation,
                        Guid.NewGuid()))
                .ToArray();

        try
        {
            ValidateGroupRuntimeState(
                records);

            foreach (var record in records)
            {
                await AppendJournalAsync(
                    record.TransactionId,
                    OperationJournalState.Prepared,
                    record.Operation,
                    "Safe-Sort-Gruppe vorbereitet. Noch keine Zieldatei committed.",
                    cancellationToken);

                record.Prepared = true;
            }

            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await AppendJournalAsync(
                    record.TransactionId,
                    OperationJournalState.Executing,
                    record.Operation,
                    "Verifizierte temporäre Kopie wird vorbereitet.",
                    cancellationToken);

                record.TemporaryPath =
                    await StageVerifiedCopyAsync(
                        record.Operation,
                        record.TransactionId,
                        cancellationToken);
            }

            ValidateSourcesStillMatchSnapshots(
                records);

            ValidateDestinationsStillFree(
                records);

            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var temporaryPath =
                    record.TemporaryPath;

                if (string.IsNullOrWhiteSpace(
                        temporaryPath))
                {
                    throw new InvalidOperationException(
                        "Die temporäre Kopie der Gruppe fehlt vor dem Commit.");
                }

                File.Move(
                    temporaryPath,
                    record.Operation.DestinationPath);

                record.TemporaryPath = null;
                record.Committed = true;

                await AppendJournalAsync(
                    record.TransactionId,
                    OperationJournalState.Committed,
                    record.Operation,
                    "Verifizierte Kopie wurde auf den endgültigen Zielpfad committed.",
                    CancellationToken.None);

                progress?.Report(
                    new MediaSortLiveExecutionProgress(
                        completedOperationsBeforeGroup
                        + records.Count(value =>
                            value.Committed),
                        totalOperationCount,
                        completedGroupsBeforeGroup,
                        totalGroupCount,
                        record.Operation.SourcePath,
                        record.Operation.DestinationPath,
                        skippedOperationsBeforeGroup,
                        skippedGroupsBeforeGroup));
            }

            foreach (var record in records)
            {
                await AppendJournalAsync(
                    record.TransactionId,
                    OperationJournalState.Completed,
                    record.Operation,
                    group.RequiresAtomicExecution
                        ? "Alle Mitglieder der Safe-Sort-Gruppe wurden committed. Operation abgeschlossen."
                        : "Safe-Sort-Kopie abgeschlossen.",
                    CancellationToken.None);

                record.Completed = true;
            }

            progress?.Report(
                new MediaSortLiveExecutionProgress(
                    completedOperationsBeforeGroup
                    + records.Length,
                    totalOperationCount,
                    completedGroupsBeforeGroup + 1,
                    totalGroupCount,
                    records[^1].Operation.SourcePath,
                    records[^1].Operation.DestinationPath,
                    skippedOperationsBeforeGroup,
                    skippedGroupsBeforeGroup));

            return new GroupExecutionResult(
                GroupExecutionState.Completed,
                records.Length);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            var recoveryRequired =
                records.Any(record =>
                    record.Committed
                    && !record.Completed);

            await FinalizeInterruptedGroupAsync(
                records,
                cancelled: true,
                "Ausführung durch Benutzer abgebrochen.");

            return new GroupExecutionResult(
                recoveryRequired
                    ? GroupExecutionState.RecoveryRequired
                    : GroupExecutionState.Cancelled,
                records.Count(record =>
                    record.Completed),
                recoveryRequired
                    ? "Die Gruppe wurde während der Commit-Phase abgebrochen. "
                      + "Committed-Ziele bleiben erhalten und werden beim Recovery geprüft."
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
                records.Any(record =>
                    record.Committed
                    && !record.Completed);

            await FinalizeInterruptedGroupAsync(
                records,
                cancelled: false,
                exception.Message);

            if (!recoveryRequired)
            {
                var problem =
                    await TryBuildSourceReadProblemAsync(
                        records,
                        exception,
                        CancellationToken.None);

                if (problem is not null)
                {
                    return new GroupExecutionResult(
                        GroupExecutionState.Skipped,
                        0,
                        problem.Message,
                        problem);
                }
            }

            return new GroupExecutionResult(
                recoveryRequired
                    ? GroupExecutionState.RecoveryRequired
                    : GroupExecutionState.Failed,
                records.Count(record =>
                    record.Completed),
                exception.Message);
        }
        finally
        {
            foreach (var record in records)
            {
                TryDeleteTemporaryFile(
                    record.TemporaryPath);
            }
        }
    }

    private async Task<GroupExecutionResult> ExecuteMoveGroupAsync(
        MediaSortExecutionGroup group,
        int completedOperationsBeforeGroup,
        int completedGroupsBeforeGroup,
        int skippedOperationsBeforeGroup,
        int skippedGroupsBeforeGroup,
        int totalOperationCount,
        int totalGroupCount,
        IProgress<MediaSortLiveExecutionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var records =
            group.Operations
                .OrderBy(operation =>
                    operation.Index)
                .Select(operation =>
                    new GroupOperationRecord(
                        operation,
                        Guid.NewGuid()))
                .ToArray();

        try
        {
            ValidateGroupRuntimeState(
                records);

            foreach (var record in records)
            {
                await AppendJournalAsync(
                    record.TransactionId,
                    OperationJournalState.Prepared,
                    record.Operation,
                    "Safe-Move-Gruppe vorbereitet. Quelle, Ziel und Recovery werden vor jeder Löschung vollständig verifiziert.",
                    cancellationToken);

                record.Prepared = true;
            }

            // Phase 1: Jede Quelle persistent sichern und die Recovery-Kopie
            // verifizieren. Erst wenn ALLE Mitglieder gesichert sind, geht es weiter.
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var recovery =
                    await _persistentRecoveryStore.PreserveAsync(
                        record.Operation.SourcePath,
                        cancellationToken);

                record.Recovery =
                    recovery;

                var recoveryValid =
                    await _persistentRecoveryStore.VerifyAsync(
                        recovery,
                        cancellationToken);

                if (!recoveryValid)
                {
                    throw new CryptographicException(
                        $"Die persistente Recovery-Sicherung für '{record.Operation.SourcePath}' "
                        + "hat die Integritätsprüfung nicht bestanden.");
                }

                await AppendJournalAsync(
                    record.TransactionId,
                    OperationJournalState.Prepared,
                    record.Operation,
                    "Persistente Recovery-Sicherung erstellt und verifiziert. "
                    + "Die Quelldatei ist weiterhin unverändert vorhanden.",
                    cancellationToken,
                    recovery);
            }

            // Phase 2: Alle Ziele als .partial schreiben, flushen und per SHA-256
            // gegen die noch vorhandenen Quellen verifizieren.
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await AppendJournalAsync(
                    record.TransactionId,
                    OperationJournalState.Executing,
                    record.Operation,
                    "Verifizierte Zielkopie für Safe-Move wird vorbereitet. "
                    + "Die Quelldatei bleibt weiterhin bestehen.",
                    cancellationToken,
                    record.Recovery);

                record.TemporaryPath =
                    await StageVerifiedCopyAsync(
                        record.Operation,
                        record.TransactionId,
                        cancellationToken);
            }

            ValidateSourcesStillMatchSnapshots(
                records);

            ValidateDestinationsStillFree(
                records);

            // Phase 3: Alle verifizierten .partial-Dateien auf ihre endgültigen
            // Zielnamen committen. Noch immer wird KEINE Quelle gelöscht.
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var temporaryPath =
                    record.TemporaryPath;

                if (string.IsNullOrWhiteSpace(
                        temporaryPath))
                {
                    throw new InvalidOperationException(
                        "Die temporäre Move-Zielkopie fehlt vor dem Commit.");
                }

                File.Move(
                    temporaryPath,
                    record.Operation.DestinationPath);

                record.TemporaryPath = null;
                record.Committed = true;

                await AppendJournalAsync(
                    record.TransactionId,
                    OperationJournalState.Executing,
                    record.Operation,
                    "Verifizierte Zielkopie committed. "
                    + "Die Quelle existiert noch; die destruktive Phase hat noch nicht begonnen.",
                    CancellationToken.None,
                    record.Recovery);
            }

            // Phase 4: Vor der ERSTEN Löschung müssen für ALLE Gruppenmitglieder
            // Recovery, Quelle und endgültiges Ziel nochmals übereinstimmen.
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await VerifyMoveRecordReadyForSourceDeletionAsync(
                    record,
                    cancellationToken);
            }

            // Dies ist der letzte Punkt, an dem ein Benutzerabbruch die Gruppe
            // ohne destruktiven Teilschritt stoppen darf.
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var record in records)
            {
                await AppendJournalAsync(
                    record.TransactionId,
                    OperationJournalState.Executing,
                    record.Operation,
                    "Gesamte Safe-Move-Gruppe ist Recovery-/Quell-/Ziel-verifiziert. "
                    + "Die destruktive Commit-Phase beginnt jetzt und wird für diese Gruppe nicht mehr durch Benutzerabbruch unterbrochen.",
                    CancellationToken.None,
                    record.Recovery);
            }

            // Phase 5: Ab hier bewusst KEIN CancellationToken mehr.
            // Eine atomare Gruppe soll nach Beginn der Löschphase entweder
            // vollständig abgeschlossen werden oder eindeutig RecoveryRequired bleiben.
            foreach (var record in records)
            {
                ValidateSourceSnapshot(
                    record.Operation);

                await VerifyMoveRecordReadyForSourceDeletionAsync(
                    record,
                    CancellationToken.None);

                File.Delete(
                    record.Operation.SourcePath);

                record.SourceDeleted = true;

                if (File.Exists(
                        record.Operation.SourcePath))
                {
                    throw new IOException(
                        $"Die Quelle '{record.Operation.SourcePath}' wurde nach File.Delete weiterhin gefunden. "
                        + "Recovery ist erforderlich.");
                }

                await AppendJournalAsync(
                    record.TransactionId,
                    OperationJournalState.Committed,
                    record.Operation,
                    "Recovery und endgültiges Ziel wurden per SHA-256 verifiziert; "
                    + "die ursprüngliche Quelldatei wurde erst danach entfernt.",
                    CancellationToken.None,
                    record.Recovery);

                progress?.Report(
                    new MediaSortLiveExecutionProgress(
                        completedOperationsBeforeGroup
                        + records.Count(value =>
                            value.SourceDeleted),
                        totalOperationCount,
                        completedGroupsBeforeGroup,
                        totalGroupCount,
                        record.Operation.SourcePath,
                        record.Operation.DestinationPath,
                        skippedOperationsBeforeGroup,
                        skippedGroupsBeforeGroup));
            }

            foreach (var record in records)
            {
                await AppendJournalAsync(
                    record.TransactionId,
                    OperationJournalState.Completed,
                    record.Operation,
                    group.RequiresAtomicExecution
                        ? "Alle Mitglieder der Safe-Move-Gruppe wurden vollständig verifiziert, committed und erst anschließend an der Quelle entfernt."
                        : "Safe-Move vollständig abgeschlossen.",
                    CancellationToken.None,
                    record.Recovery);

                record.Completed = true;
            }

            foreach (var record in records)
            {
                await TryReleaseRecoveryAsync(
                    record.Recovery);
            }

            progress?.Report(
                new MediaSortLiveExecutionProgress(
                    completedOperationsBeforeGroup
                    + records.Length,
                    totalOperationCount,
                    completedGroupsBeforeGroup + 1,
                    totalGroupCount,
                    records[^1].Operation.SourcePath,
                    records[^1].Operation.DestinationPath,
                    skippedOperationsBeforeGroup,
                    skippedGroupsBeforeGroup));

            return new GroupExecutionResult(
                GroupExecutionState.Completed,
                records.Length);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            var recoveryRequired =
                records.Any(record =>
                    record.Committed
                    || record.SourceDeleted);

            await FinalizeInterruptedGroupAsync(
                records,
                cancelled: true,
                "Safe-Move durch Benutzer vor Beginn bzw. während der nicht-destruktiven Vorbereitung abgebrochen.");

            if (!recoveryRequired)
            {
                await ReleaseSafeRecoveriesAsync(
                    records);
            }

            return new GroupExecutionResult(
                recoveryRequired
                    ? GroupExecutionState.RecoveryRequired
                    : GroupExecutionState.Cancelled,
                records.Count(record =>
                    record.Completed),
                recoveryRequired
                    ? "Safe-Move wurde nach einem Ziel-Commit unterbrochen. "
                      + "Recovery-Sicherungen bleiben erhalten; es werden keine automatischen Ziel-Löschungen durchgeführt."
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
                records.Any(record =>
                    record.Committed
                    || record.SourceDeleted);

            await FinalizeInterruptedGroupAsync(
                records,
                cancelled: false,
                exception.Message);

            if (!recoveryRequired)
            {
                await ReleaseSafeRecoveriesAsync(
                    records);

                var problem =
                    await TryBuildSourceReadProblemAsync(
                        records,
                        exception,
                        CancellationToken.None);

                if (problem is not null)
                {
                    return new GroupExecutionResult(
                        GroupExecutionState.Skipped,
                        0,
                        problem.Message,
                        problem);
                }
            }

            return new GroupExecutionResult(
                recoveryRequired
                    ? GroupExecutionState.RecoveryRequired
                    : GroupExecutionState.Failed,
                records.Count(record =>
                    record.Completed),
                exception.Message);
        }
        finally
        {
            foreach (var record in records)
            {
                TryDeleteTemporaryFile(
                    record.TemporaryPath);
            }
        }
    }

    private async Task VerifyMoveRecordReadyForSourceDeletionAsync(
        GroupOperationRecord record,
        CancellationToken cancellationToken)
    {
        ValidateSourceSnapshot(
            record.Operation);

        var recovery =
            record.Recovery
            ?? throw new InvalidOperationException(
                "Vor einer Move-Löschung fehlt die persistente Recovery-Sicherung.");

        var recoveryValid =
            await _persistentRecoveryStore.VerifyAsync(
                recovery,
                cancellationToken);

        if (!recoveryValid)
        {
            throw new CryptographicException(
                $"Die persistente Recovery-Sicherung für '{record.Operation.SourcePath}' "
                + "ist vor der Quelllöschung nicht mehr verifizierbar.");
        }

        if (!File.Exists(
                record.Operation.DestinationPath))
        {
            throw new FileNotFoundException(
                "Das endgültige Move-Ziel fehlt vor der Quelllöschung.",
                record.Operation.DestinationPath);
        }

        var destinationInfo =
            new FileInfo(
                record.Operation.DestinationPath);

        if (destinationInfo.Length
            != record.Operation.SourceSnapshot.Length)
        {
            throw new IOException(
                $"Das endgültige Ziel '{record.Operation.DestinationPath}' "
                + "hat vor der Quelllöschung eine unerwartete Dateigröße.");
        }

        var sourceHash =
            Convert.ToHexString(
                await ComputeSourceSha256Async(
                    record.Operation.SourcePath,
                    cancellationToken));

        var destinationHash =
            Convert.ToHexString(
                await ComputeSha256Async(
                    record.Operation.DestinationPath,
                    cancellationToken));

        if (!string.Equals(
                sourceHash,
                recovery.Sha256,
                StringComparison.Ordinal)
            || !string.Equals(
                destinationHash,
                recovery.Sha256,
                StringComparison.Ordinal))
        {
            throw new CryptographicException(
                "Recovery, Quelle und endgültiges Ziel sind vor der Quelllöschung nicht byteidentisch. "
                + "Die Quelle wird nicht gelöscht.");
        }
    }

    private async Task ReleaseSafeRecoveriesAsync(
        IEnumerable<GroupOperationRecord> records)
    {
        foreach (var record in records)
        {
            await TryReleaseRecoveryAsync(
                record.Recovery);
        }
    }

    private async Task TryReleaseRecoveryAsync(
        RecoveryHandle? recovery)
    {
        if (recovery is null)
        {
            return;
        }

        try
        {
            await _persistentRecoveryStore.ReleaseAsync(
                recovery,
                CancellationToken.None);
        }
        catch
        {
            // Eine übrig gebliebene Recovery-Datei ist sicherer als ein
            // weiterer riskanter Bereinigungsversuch.
        }
    }

    private async Task<MediaSortExecutionProblem?> TryBuildSourceReadProblemAsync(
        IReadOnlyCollection<GroupOperationRecord> records,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var sourceRead =
            SourceReadIOException.Find(
                exception);

        if (sourceRead is null)
        {
            return null;
        }

        var comparison =
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        var operation =
            records
                .Select(record =>
                    record.Operation)
                .FirstOrDefault(candidate =>
                    string.Equals(
                        Path.GetFullPath(candidate.SourcePath),
                        sourceRead.SourcePath,
                        comparison));

        if (operation is null)
        {
            return null;
        }

        StorageHealthSnapshot storageHealth;

        try
        {
            storageHealth =
                await _storageHealthService.InspectFailureAsync(
                    sourceRead.SourcePath,
                    sourceRead,
                    cancellationToken);
        }
        catch
        {
            storageHealth =
                new StorageHealthSnapshot(
                    sourceRead.SourcePath,
                    StorageFailureKind.SourceReadFailure,
                    StorageHealthSeverity.Warning,
                    "Die Quelldatei konnte nicht zuverlässig gelesen werden. "
                    + "Datenträgerdetails konnten zusätzlich nicht ermittelt werden.",
                    sourceRead.GetBaseException().Message);
        }

        var message =
            storageHealth.SuspectsPhysicalDevice
                ? "Möglicher Datenträgerfehler erkannt. Die betroffene Datei bzw. Companion-Gruppe wurde sicher übersprungen; keine Quelle dieser Gruppe wurde gelöscht."
                : "Quelldatei nicht zuverlässig lesbar. Die betroffene Datei bzw. Companion-Gruppe wurde sicher übersprungen; keine Quelle dieser Gruppe wurde gelöscht.";

        return new MediaSortExecutionProblem(
            storageHealth.SuspectsPhysicalDevice
                ? MediaSortExecutionProblemKind.SuspectedStorageFailure
                : MediaSortExecutionProblemKind.UnreadableSource,
            sourceRead.SourcePath,
            operation.DestinationPath,
            records.Count,
            message,
            storageHealth);
    }

    private async Task FinalizeInterruptedGroupAsync(
        IEnumerable<GroupOperationRecord> records,
        bool cancelled,
        string message)
    {
        var recordArray =
            records.ToArray();

        var groupMutationStarted =
            recordArray.Any(record =>
                record.Committed
                || record.SourceDeleted);

        foreach (var record in recordArray)
        {
            if (!record.Prepared
                || record.Completed)
            {
                continue;
            }

            var recoveryRequired =
                groupMutationStarted
                || record.Committed
                || record.SourceDeleted;

            var state =
                recoveryRequired
                    ? OperationJournalState.RecoveryRequired
                    : cancelled
                        ? OperationJournalState.Cancelled
                        : OperationJournalState.Failed;

            var stateMessage =
                recoveryRequired
                    ? "Gruppenausführung unterbrochen, nachdem mindestens ein endgültiges Ziel committed "
                      + "oder eine Quelle entfernt wurde. Recovery muss Quelle, Ziel und ggf. persistente Sicherung prüfen. "
                      + message
                    : "Gruppenausführung vor jeder endgültigen Dateisystemänderung beendet. "
                      + message;

            try
            {
                await AppendJournalAsync(
                    record.TransactionId,
                    state,
                    record.Operation,
                    stateMessage,
                    CancellationToken.None,
                    record.Recovery);
            }
            catch
            {
                // Ein fehlgeschlagener Journalabschluss darf niemals dazu führen,
                // dass wir weitere Dateisystemänderungen vornehmen. Der vorherige
                // nicht-terminale Journalzustand bleibt absichtlich bestehen.
            }
        }
    }

    private async Task<string> StageVerifiedCopyAsync(
        MediaSortExecutionOperation operation,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        ValidateSourceSnapshot(
            operation);

        if (File.Exists(
                operation.DestinationPath)
            || Directory.Exists(
                operation.DestinationPath))
        {
            throw new IOException(
                $"Das Ziel '{operation.DestinationPath}' existiert bereits. "
                + "Es wird nicht überschrieben.");
        }

        var destinationDirectory =
            Path.GetDirectoryName(
                operation.DestinationPath)
            ?? throw new InvalidOperationException(
                "Das Zielverzeichnis konnte nicht bestimmt werden.");

        Directory.CreateDirectory(
            destinationDirectory);

        var temporaryPath =
            Path.Combine(
                destinationDirectory,
                $".{Path.GetFileName(operation.DestinationPath)}.elbwald-sort-{transactionId:N}.partial");

        try
        {
            var copiedSourceHash =
                await CopyAndHashAsync(
                    operation.SourcePath,
                    temporaryPath,
                    cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            ValidateSourceSnapshot(
                operation);

            var currentSourceHash =
                await ComputeSourceSha256Async(
                    operation.SourcePath,
                    cancellationToken);

            var temporaryHash =
                await ComputeSha256Async(
                    temporaryPath,
                    cancellationToken);

            if (!CryptographicOperations.FixedTimeEquals(
                    copiedSourceHash,
                    currentSourceHash)
                || !CryptographicOperations.FixedTimeEquals(
                    copiedSourceHash,
                    temporaryHash))
            {
                throw new CryptographicException(
                    "Die Quell- oder Zielkopie hat sich während der SHA-256-Prüfung verändert.");
            }

            var temporaryInfo =
                new FileInfo(
                    temporaryPath);

            if (temporaryInfo.Length
                != operation.SourceSnapshot.Length)
            {
                throw new IOException(
                    "Die temporäre Kopie hat eine unerwartete Dateigröße.");
            }

            File.SetLastWriteTimeUtc(
                temporaryPath,
                operation.SourceSnapshot.LastWriteTimeUtc);

            if (File.Exists(
                    operation.DestinationPath)
                || Directory.Exists(
                    operation.DestinationPath))
            {
                throw new IOException(
                    $"Das Ziel '{operation.DestinationPath}' wurde während der Vorbereitung erstellt. "
                    + "Die temporäre Datei wird verworfen.");
            }

            return temporaryPath;
        }
        catch
        {
            TryDeleteTemporaryFile(
                temporaryPath);

            throw;
        }
    }

    private static async Task<byte[]> CopyAndHashAsync(
        string sourcePath,
        string temporaryPath,
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
                    CopyBufferSize,
                    FileOptions.Asynchronous
                    | FileOptions.SequentialScan);
        }
        catch (IOException exception)
        {
            throw new SourceReadIOException(
                sourcePath,
                "Öffnen für die Zielkopie",
                exception);
        }

        await using var sourceStream =
            source;

        await using var destination =
            new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.Asynchronous
                | FileOptions.SequentialScan);

        using var hash =
            IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);

            var buffer =
                new byte[CopyBufferSize];

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
                        "Lesen für die Zielkopie",
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

        return hash.GetHashAndReset();
    }

    private static Task<byte[]> ComputeSourceSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        return ComputeSha256Async(
            path,
            cancellationToken,
            sourceRead: true);
    }

    private static Task<byte[]> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        return ComputeSha256Async(
            path,
            cancellationToken,
            sourceRead: false);
    }

    private static async Task<byte[]> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken,
        bool sourceRead)
    {
        FileStream stream;

        try
        {
            stream =
                new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    CopyBufferSize,
                    FileOptions.Asynchronous
                    | FileOptions.SequentialScan);
        }
        catch (IOException exception) when (sourceRead)
        {
            throw new SourceReadIOException(
                path,
                "Öffnen für die SHA-256-Prüfung",
                exception);
        }

        await using var readStream =
            stream;

        using var hash =
            IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);

            var buffer =
                new byte[CopyBufferSize];

            while (true)
            {
                int bytesRead;

                try
                {
                    bytesRead =
                        await readStream.ReadAsync(
                            buffer.AsMemory(
                                0,
                                buffer.Length),
                            cancellationToken);
                }
                catch (IOException exception) when (sourceRead)
                {
                    throw new SourceReadIOException(
                        path,
                        "Lesen für die SHA-256-Prüfung",
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
            }

        return hash.GetHashAndReset();
    }

    private static void ValidateGroupRuntimeState(
        IEnumerable<GroupOperationRecord> records)
    {
        foreach (var record in records)
        {
            ValidateSourceSnapshot(
                record.Operation);

            if (File.Exists(
                    record.Operation.DestinationPath)
                || Directory.Exists(
                    record.Operation.DestinationPath))
            {
                throw new IOException(
                    $"Das Ziel '{record.Operation.DestinationPath}' existiert inzwischen. "
                    + "Die Gruppe wird nicht gestartet.");
            }
        }
    }

    private static void ValidateSourcesStillMatchSnapshots(
        IEnumerable<GroupOperationRecord> records)
    {
        foreach (var record in records)
        {
            ValidateSourceSnapshot(
                record.Operation);
        }
    }

    private static void ValidateDestinationsStillFree(
        IEnumerable<GroupOperationRecord> records)
    {
        foreach (var record in records)
        {
            if (File.Exists(
                    record.Operation.DestinationPath)
                || Directory.Exists(
                    record.Operation.DestinationPath))
            {
                throw new IOException(
                    $"Das Ziel '{record.Operation.DestinationPath}' ist während der Vorbereitung aufgetaucht. "
                    + "Kein Gruppen-Commit wird begonnen.");
            }
        }
    }

    private static void ValidateSourceSnapshot(
        MediaSortExecutionOperation operation)
    {
        if (!File.Exists(
                operation.SourcePath))
        {
            throw new FileNotFoundException(
                "Die Quelldatei existiert nicht mehr.",
                operation.SourcePath);
        }

        var info =
            new FileInfo(
                operation.SourcePath);

        if (info.LinkTarget is not null
            || (info.Attributes
                & FileAttributes.ReparsePoint)
                != 0)
        {
            throw new IOException(
                $"Symbolische Links/Reparse-Points werden im Live-Sortierer nicht verändert: "
                + $"'{operation.SourcePath}'.");
        }

        if (info.Length
            != operation.SourceSnapshot.Length)
        {
            throw new IOException(
                $"Die Größe von '{operation.SourcePath}' hat sich seit dem Dry Run verändert.");
        }

        if (info.LastWriteTimeUtc
            != operation.SourceSnapshot.LastWriteTimeUtc)
        {
            throw new IOException(
                $"Der Schreibzeitpunkt von '{operation.SourcePath}' hat sich seit dem Dry Run verändert.");
        }
    }

    private async Task AppendJournalAsync(
        Guid transactionId,
        OperationJournalState state,
        MediaSortExecutionOperation operation,
        string message,
        CancellationToken cancellationToken,
        RecoveryHandle? recovery = null)
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

    private static bool CanPassPlanningGate(
        MediaSortExecutionPlan plan,
        MediaSortExecutionConfirmation confirmation)
    {
        if (plan.HasBlockingPlanningIssues)
        {
            return false;
        }

        if (plan.HasDateReviewApprovalRequirement
            && !confirmation.ApproveDateReviews)
        {
            return false;
        }

        return plan.ValidationIssues.Count == 0
               && plan.FileOperationPlan.CanExecute
               && plan.FileOperationPreflight.IsSafe;
    }

    private static string? ValidateConfirmation(
        MediaSortExecutionPlan plan,
        MediaSortExecutionConfirmation confirmation)
    {
        if (!string.Equals(
                plan.Fingerprint,
                confirmation.Fingerprint,
                StringComparison.Ordinal))
        {
            return "Die Bestätigung gehört nicht mehr zum aktuellen Safe Execution Plan. "
                   + "Bitte die Sicherheitsprüfung erneut erzeugen.";
        }

        var kinds =
            plan.Groups
                .SelectMany(group =>
                    group.Operations)
                .Select(operation =>
                    operation.Kind)
                .Distinct()
                .ToArray();

        if (kinds.Length != 1)
        {
            return "Der Safe Execution Plan enthält gemischte Copy-/Move-Operationen und kann nicht freigegeben werden.";
        }

        var operationKind =
            kinds[0];

        if (!confirmation.HasRequiredTextFor(
                operationKind))
        {
            var requiredText =
                operationKind == FileOperationKind.Move
                    ? MediaSortExecutionConfirmation.MoveRequiredText
                    : MediaSortExecutionConfirmation.CopyRequiredText;

            return $"Zur Freigabe muss exakt '{requiredText}' eingegeben werden.";
        }

        if (operationKind == FileOperationKind.Move
            && !confirmation.ApproveSourceDeletion)
        {
            return "Für Verschieben muss zusätzlich ausdrücklich bestätigt werden, "
                   + "dass die Quelldateien erst nach verifizierter Recovery- und Zielkopie gelöscht werden dürfen.";
        }

        return null;
    }

    private static void TryDeleteTemporaryFile(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            return;
        }

        try
        {
            if (File.Exists(
                    path))
            {
                File.Delete(
                    path);
            }
        }
        catch
        {
            // Eine übrig gebliebene .partial-Datei ist sicherer als
            // ein weiterer Löschversuch, dessen Zustand unklar ist.
        }
    }

    private static MediaSortLiveExecutionResult Result(
        MediaSortLiveExecutionState state,
        MediaSortExecutionPlan plan,
        int completedOperations,
        int completedGroups,
        IEnumerable<MediaSortExecutionIssue>? issues = null,
        string? errorMessage = null,
        int skippedOperations = 0,
        int skippedGroups = 0,
        IEnumerable<MediaSortExecutionProblem>? problems = null)
    {
        return new MediaSortLiveExecutionResult(
            state,
            completedOperations,
            plan.OperationCount,
            completedGroups,
            plan.GroupCount,
            issues,
            errorMessage,
            skippedOperations,
            skippedGroups,
            problems);
    }

    private enum GroupExecutionState
    {
        Completed,
        Skipped,
        Failed,
        Cancelled,
        RecoveryRequired
    }

    private sealed class GroupOperationRecord
    {
        public GroupOperationRecord(
            MediaSortExecutionOperation operation,
            Guid transactionId)
        {
            Operation = operation;
            TransactionId = transactionId;
        }

        public MediaSortExecutionOperation Operation { get; }

        public Guid TransactionId { get; }

        public bool Prepared { get; set; }

        public bool Committed { get; set; }

        public bool Completed { get; set; }

        public bool SourceDeleted { get; set; }

        public RecoveryHandle? Recovery { get; set; }

        public string? TemporaryPath { get; set; }
    }

    private sealed record GroupExecutionResult(
        GroupExecutionState State,
        int CompletedOperationCount,
        string? ErrorMessage = null,
        MediaSortExecutionProblem? Problem = null);
}
