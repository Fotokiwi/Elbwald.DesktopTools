using System.Security.Cryptography;
using System.Text;
using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Companions;
using Elbwald.DesktopTools.Contracts.Media.Sorting;
using Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;
using Elbwald.DesktopTools.Contracts.Recovery;

namespace Elbwald.DesktopTools.Core.Media;

public sealed class MediaSortExecutionPlanner
    : IMediaSortExecutionPlanner
{
    private const string FingerprintVersion =
        "ELBWALD-SORT-EXECUTION-V1";

    private readonly IFileOperationPlanner _fileOperationPlanner;
    private readonly IFileOperationSafetyChecker _safetyChecker;
    private readonly IStartupRecoveryService _startupRecoveryService;
    private readonly RecoveryOptions _recoveryOptions;

    public MediaSortExecutionPlanner(
        IFileOperationPlanner fileOperationPlanner,
        IFileOperationSafetyChecker safetyChecker,
        IStartupRecoveryService startupRecoveryService,
        RecoveryOptions recoveryOptions)
    {
        ArgumentNullException.ThrowIfNull(fileOperationPlanner);
        ArgumentNullException.ThrowIfNull(safetyChecker);
        ArgumentNullException.ThrowIfNull(startupRecoveryService);
        ArgumentNullException.ThrowIfNull(recoveryOptions);

        recoveryOptions.Validate();

        _fileOperationPlanner =
            fileOperationPlanner;

        _safetyChecker =
            safetyChecker;

        _startupRecoveryService =
            startupRecoveryService;

        _recoveryOptions =
            recoveryOptions;
    }

    public async Task<MediaSortExecutionPlan> CreateAsync(
        MediaSortPlan sortPlan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sortPlan);

        cancellationToken.ThrowIfCancellationRequested();

        var planningIssues =
            BuildPlanningIssues(
                sortPlan);

        var buildResult =
            BuildExpandedOperations(
                sortPlan,
                planningIssues,
                cancellationToken);

        var fileOperationPlan =
            _fileOperationPlanner.CreatePlan(
                buildResult.Requests);

        if (!fileOperationPlan.CanExecute)
        {
            planningIssues.Add(
                new MediaSortExecutionIssue(
                    MediaSortExecutionIssueKind.ExpandedFilePlanConflict,
                    "Der erweiterte Dateioperationsplan enthält Konflikte. "
                    + "Die spätere Ausführung bleibt gesperrt."));
        }

        var groups =
            MaterializeGroups(
                buildResult.PendingGroups,
                fileOperationPlan);

        var fingerprint =
            ComputeFingerprint(
                groups);

        var preflight =
            CheckExecutionSafety(
                fileOperationPlan);

        var peakRecoveryBytes =
            CalculatePeakPersistentRecoveryBytes(
                groups);

        var dynamicIssues =
            new List<MediaSortExecutionIssue>();

        AddFileOperationSafetyIssues(
            preflight,
            dynamicIssues);

        await AddRecoveryReadinessIssuesAsync(
            dynamicIssues,
            cancellationToken);

        AddPersistentRecoveryCapacityIssues(
            peakRecoveryBytes,
            dynamicIssues);

        AddRuntimeStateIssues(
            groups,
            dynamicIssues,
            cancellationToken);

        return new MediaSortExecutionPlan(
            fingerprint,
            DateTimeOffset.UtcNow,
            groups,
            fileOperationPlan,
            preflight,
            planningIssues,
            dynamicIssues,
            peakRecoveryBytes);
    }

    public async Task<MediaSortExecutionValidationResult> ValidateCurrentStateAsync(
        MediaSortExecutionPlan executionPlan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionPlan);

        cancellationToken.ThrowIfCancellationRequested();

        var issues =
            new List<MediaSortExecutionIssue>();

        var safetyResult =
            CheckExecutionSafety(
                executionPlan.FileOperationPlan);

        AddFileOperationSafetyIssues(
            safetyResult,
            issues);

        await AddRecoveryReadinessIssuesAsync(
            issues,
            cancellationToken);

        AddPersistentRecoveryCapacityIssues(
            executionPlan.PeakPersistentRecoveryBytes,
            issues);

        AddRuntimeStateIssues(
            executionPlan.Groups,
            issues,
            cancellationToken);

        return new MediaSortExecutionValidationResult(
            issues);
    }

    private static List<MediaSortExecutionIssue> BuildPlanningIssues(
        MediaSortPlan sortPlan)
    {
        var issues =
            new List<MediaSortExecutionIssue>();

        if (!sortPlan.CanExecute)
        {
            issues.Add(
                new MediaSortExecutionIssue(
                    MediaSortExecutionIssueKind.SortPlanNotExecutable,
                    "Der zugrunde liegende Sortierplan ist nicht ausführungsbereit."));
        }

        foreach (var issue in sortPlan.Issues)
        {
            if (issue.Kind
                == MediaSortIssueKind.CompanionGroup)
            {
                continue;
            }

            if (issue.Severity
                    == MediaSortIssueSeverity.Problem
                || issue.Kind
                    == MediaSortIssueKind.DestinationInsideSource)
            {
                issues.Add(
                    new MediaSortExecutionIssue(
                        MediaSortExecutionIssueKind.SortPlanNotExecutable,
                        issue.Kind
                            == MediaSortIssueKind.DestinationInsideSource
                                ? "Für eine echte Ausführung darf das Ziel nicht innerhalb der Quelle liegen. "
                                  + issue.Message
                                : issue.Message,
                        SourcePath:
                            issue.Path));

                continue;
            }

            if (issue.Kind
                is MediaSortIssueKind.MissingCaptureDate
                or MediaSortIssueKind.DateReview
                or MediaSortIssueKind.InsufficientDateConfidence
                or MediaSortIssueKind.DateConflict)
            {
                issues.Add(
                    new MediaSortExecutionIssue(
                        MediaSortExecutionIssueKind.ReviewRequiresConfirmation,
                        "Vor einer echten Ausführung muss dieser Datums-Prüffall ausdrücklich bestätigt werden. "
                        + issue.Message,
                        SourcePath:
                            issue.Path));
            }
        }

        foreach (var group in sortPlan.CompanionGroups)
        {
            switch (group.State)
            {
                case MediaCompanionGroupState.Review:
                    issues.Add(
                        new MediaSortExecutionIssue(
                            MediaSortExecutionIssueKind.CompanionReviewRequiresConfirmation,
                            "Die Companion-Gruppe benötigt vor einer späteren Ausführung eine ausdrückliche Bestätigung. "
                            + group.Message,
                            GroupId:
                                CreateCompanionGroupId(
                                    group),
                            SourcePath:
                                group.DirectoryPath));
                    break;

                case MediaCompanionGroupState.Conflict:
                    issues.Add(
                        new MediaSortExecutionIssue(
                            MediaSortExecutionIssueKind.SortPlanNotExecutable,
                            "Die Companion-Gruppe enthält einen blockierenden Konflikt. "
                            + group.Message,
                            GroupId:
                                CreateCompanionGroupId(
                                    group),
                            SourcePath:
                                group.DirectoryPath));
                    break;
            }
        }

        return issues;
    }

    private static ExpandedBuildResult BuildExpandedOperations(
        MediaSortPlan sortPlan,
        ICollection<MediaSortExecutionIssue> planningIssues,
        CancellationToken cancellationToken)
    {
        var pathComparer =
            GetPathComparer();

        var mediaByPath =
            new Dictionary<string, MediaFile>(
                pathComparer);

        foreach (var item in sortPlan.Items)
        {
            mediaByPath[
                NormalizePath(
                    item.File.FullPath)] =
                item.File;
        }

        foreach (var group in sortPlan.CompanionGroups)
        {
            foreach (var member in group.Members)
            {
                mediaByPath[
                    NormalizePath(
                        member.File.File.FullPath)] =
                    member.File.File;
            }
        }

        var originalOperationsBySource =
            new Dictionary<string, FileOperationPlanItem>(
                pathComparer);

        foreach (var operation in sortPlan.OperationPlan.Operations)
        {
            var normalizedSource =
                NormalizePath(
                    operation.SourcePath);

            if (!originalOperationsBySource.TryAdd(
                    normalizedSource,
                    operation))
            {
                planningIssues.Add(
                    new MediaSortExecutionIssue(
                        MediaSortExecutionIssueKind.ExpandedFilePlanConflict,
                        "Dieselbe Quelle ist mehrfach im zugrunde liegenden Sortierplan enthalten. "
                        + "Die sichere Ausführungsplanung bleibt gesperrt.",
                        SourcePath:
                            normalizedSource));
            }
        }

        var consumedSources =
            new HashSet<string>(
                pathComparer);

        var pendingGroups =
            new List<PendingGroup>();

        foreach (var companionGroup in sortPlan.CompanionGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var operations =
                new List<PendingOperation>();

            foreach (var member in companionGroup.Members.Where(member =>
                         member.Kind
                         is MediaCompanionKind.RawImage
                         or MediaCompanionKind.JpegImage))
            {
                var normalizedSource =
                    NormalizePath(
                        member.File.File.FullPath);

                if (!originalOperationsBySource.TryGetValue(
                        normalizedSource,
                        out var originalOperation))
                {
                    continue;
                }

                operations.Add(
                    CreatePendingOperation(
                        originalOperation.Kind,
                        originalOperation.SourcePath,
                        originalOperation.DestinationPath,
                        isSidecar: false,
                        mediaByPath,
                        planningIssues));

                consumedSources.Add(
                    normalizedSource);
            }

            foreach (var projection in companionGroup.ProjectedSidecars)
            {
                var sourcePath =
                    NormalizePath(
                        projection.SourcePath);

                operations.Add(
                    CreatePendingOperation(
                        sortPlan.Options.OperationKind,
                        projection.SourcePath,
                        projection.ProjectedDestinationPath,
                        isSidecar: true,
                        mediaByPath,
                        planningIssues));

                consumedSources.Add(
                    sourcePath);
            }

            if (operations.Count == 0)
            {
                continue;
            }

            pendingGroups.Add(
                new PendingGroup(
                    CreateCompanionGroupId(
                        companionGroup),
                    companionGroup.CompanionStem,
                    IsCompanionGroup: true,
                    RequiresAtomicExecution:
                        operations.Count > 1,
                    operations));
        }

        foreach (var operation in sortPlan.OperationPlan.Operations
                     .OrderBy(operation =>
                         operation.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedSource =
                NormalizePath(
                    operation.SourcePath);

            if (consumedSources.Contains(
                    normalizedSource))
            {
                continue;
            }

            pendingGroups.Add(
                new PendingGroup(
                    CreateSingleGroupId(
                        operation.SourcePath),
                    Path.GetFileName(
                        operation.SourcePath),
                    IsCompanionGroup: false,
                    RequiresAtomicExecution: false,
                    new[]
                    {
                        CreatePendingOperation(
                            operation.Kind,
                            operation.SourcePath,
                            operation.DestinationPath,
                            isSidecar: false,
                            mediaByPath,
                            planningIssues)
                    }));
        }

        var requests =
            pendingGroups
                .SelectMany(group =>
                    group.Operations)
                .Select(operation =>
                    new FileOperationRequest(
                        operation.Kind,
                        operation.SourcePath,
                        operation.DestinationPath))
                .ToArray();

        return new ExpandedBuildResult(
            pendingGroups,
            requests);
    }

    private static PendingOperation CreatePendingOperation(
        FileOperationKind kind,
        string sourcePath,
        string destinationPath,
        bool isSidecar,
        IReadOnlyDictionary<string, MediaFile> mediaByPath,
        ICollection<MediaSortExecutionIssue> planningIssues)
    {
        var normalizedSource =
            NormalizePath(
                sourcePath);

        if (!mediaByPath.TryGetValue(
                normalizedSource,
                out var mediaFile))
        {
            planningIssues.Add(
                new MediaSortExecutionIssue(
                    MediaSortExecutionIssueKind.SourceSnapshotUnavailable,
                    "Für eine geplante Quelldatei fehlt der analysierte Snapshot. "
                    + "Die Ausführung bleibt gesperrt.",
                    SourcePath:
                        normalizedSource,
                    DestinationPath:
                        NormalizePath(
                            destinationPath)));

            return new PendingOperation(
                kind,
                normalizedSource,
                NormalizePath(
                    destinationPath),
                isSidecar,
                new MediaSortSourceSnapshot(
                    normalizedSource,
                    Length: -1,
                    LastWriteTimeUtc:
                        DateTime.MinValue));
        }

        return new PendingOperation(
            kind,
            normalizedSource,
            NormalizePath(
                destinationPath),
            isSidecar,
            new MediaSortSourceSnapshot(
                normalizedSource,
                mediaFile.Length,
                mediaFile.LastWriteTimeUtc.UtcDateTime));
    }

    private static IReadOnlyList<MediaSortExecutionGroup> MaterializeGroups(
        IReadOnlyList<PendingGroup> pendingGroups,
        FileOperationPlan fileOperationPlan)
    {
        var planOperations =
            fileOperationPlan.Operations
                .OrderBy(operation =>
                    operation.Index)
                .ToArray();

        var operationCursor = 0;

        var groups =
            new List<MediaSortExecutionGroup>(
                pendingGroups.Count);

        foreach (var pendingGroup in pendingGroups)
        {
            var operations =
                new List<MediaSortExecutionOperation>(
                    pendingGroup.Operations.Count);

            foreach (var pendingOperation in pendingGroup.Operations)
            {
                if (operationCursor
                    >= planOperations.Length)
                {
                    throw new InvalidOperationException(
                        "Der erweiterte Dateioperationsplan ist intern inkonsistent.");
                }

                var planOperation =
                    planOperations[
                        operationCursor++];

                operations.Add(
                    new MediaSortExecutionOperation(
                        planOperation.Index,
                        pendingGroup.Id,
                        planOperation.Kind,
                        planOperation.SourcePath,
                        planOperation.DestinationPath,
                        pendingOperation.IsSidecar,
                        pendingOperation.SourceSnapshot));
            }

            groups.Add(
                new MediaSortExecutionGroup(
                    pendingGroup.Id,
                    pendingGroup.DisplayName,
                    pendingGroup.IsCompanionGroup,
                    pendingGroup.RequiresAtomicExecution,
                    operations));
        }

        if (operationCursor
            != planOperations.Length)
        {
            throw new InvalidOperationException(
                "Der erweiterte Dateioperationsplan enthält unerwartete Operationen.");
        }

        return groups;
    }

    private async Task AddRecoveryReadinessIssuesAsync(
        ICollection<MediaSortExecutionIssue> issues,
        CancellationToken cancellationToken)
    {
        var snapshot =
            await _startupRecoveryService.ScanAsync(
                cancellationToken);

        if (snapshot.CanStartFileOperations)
        {
            return;
        }

        var message =
            snapshot.ErrorMessage;

        if (string.IsNullOrWhiteSpace(
                message))
        {
            message =
                snapshot.HasRecoveryCandidates
                    ? "Es existieren noch offene Recovery-Vorgänge."
                    : snapshot.HasJournalCorruption
                        ? "Das Operationsjournal enthält beschädigte Einträge."
                        : $"Recovery-Status: {snapshot.State}.";
        }

        issues.Add(
            new MediaSortExecutionIssue(
                MediaSortExecutionIssueKind.RecoveryNotReady,
                "Die Recovery-Sicherheitskette ist nicht bereit. "
                + message));
    }

    private FileOperationPreflightResult CheckExecutionSafety(
        FileOperationPlan fileOperationPlan)
    {
        var safetyPlan =
            BuildSafetyPreflightPlan(
                fileOperationPlan);

        return _safetyChecker.Check(
            safetyPlan);
    }

    private static FileOperationPlan BuildSafetyPreflightPlan(
        FileOperationPlan fileOperationPlan)
    {
        if (!fileOperationPlan.Operations.Any(operation =>
                operation.Kind == FileOperationKind.Move))
        {
            return fileOperationPlan;
        }

        // Safe-Sort-Move ist technisch kein direktes File.Move:
        // Ziel vollständig kopieren + verifizieren, erst danach Quelle löschen.
        // Deshalb muss die Zielkapazität genauso streng wie bei Copy geprüft werden.
        var safetyOperations =
            fileOperationPlan.Operations
                .Select(operation =>
                    operation.Kind == FileOperationKind.Move
                        ? new FileOperationPlanItem(
                            operation.Index,
                            FileOperationKind.Copy,
                            operation.SourcePath,
                            operation.DestinationPath)
                        : operation)
                .ToArray();

        return new FileOperationPlan(
            safetyOperations,
            fileOperationPlan.Conflicts);
    }

    private static void AddFileOperationSafetyIssues(
        FileOperationPreflightResult preflight,
        ICollection<MediaSortExecutionIssue> issues)
    {
        foreach (var safetyIssue in preflight.Issues)
        {
            issues.Add(
                new MediaSortExecutionIssue(
                    MediaSortExecutionIssueKind.FileOperationSafety,
                    safetyIssue.Message,
                    RequiredBytes:
                        safetyIssue.RequiredBytes,
                    AvailableBytes:
                        safetyIssue.AvailableBytes));
        }
    }

    private void AddPersistentRecoveryCapacityIssues(
        long peakRecoveryBytes,
        ICollection<MediaSortExecutionIssue> issues)
    {
        if (peakRecoveryBytes <= 0)
        {
            return;
        }

        DriveInfo? volume;

        try
        {
            volume =
                TryResolveVolume(
                    _recoveryOptions.PersistentDirectory);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            issues.Add(
                new MediaSortExecutionIssue(
                    MediaSortExecutionIssueKind.RecoveryStorageUnavailable,
                    "Der persistente Recovery-Datenträger konnte nicht zuverlässig bestimmt werden: "
                    + exception.Message,
                    RequiredBytes:
                        peakRecoveryBytes));
            return;
        }

        if (volume is null)
        {
            issues.Add(
                new MediaSortExecutionIssue(
                    MediaSortExecutionIssueKind.RecoveryStorageUnavailable,
                    "Für das persistente Recovery-Verzeichnis wurde kein Datenträger gefunden.",
                    RequiredBytes:
                        peakRecoveryBytes));
            return;
        }

        long totalSize;
        long available;

        try
        {
            totalSize =
                volume.TotalSize;

            available =
                volume.AvailableFreeSpace;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException)
        {
            issues.Add(
                new MediaSortExecutionIssue(
                    MediaSortExecutionIssueKind.RecoveryStorageUnavailable,
                    $"Der freie Speicher auf dem Recovery-Datenträger '{volume.Name}' "
                    + $"konnte nicht zuverlässig bestimmt werden: {exception.Message}",
                    RequiredBytes:
                        peakRecoveryBytes));
            return;
        }

        var reserve =
            _recoveryOptions.CalculatePersistentReserveBytes(
                totalSize);

        long requiredWithReserve;

        try
        {
            requiredWithReserve =
                checked(
                    peakRecoveryBytes
                    + reserve);
        }
        catch (OverflowException)
        {
            requiredWithReserve =
                long.MaxValue;
        }

        if (available
            >= requiredWithReserve)
        {
            return;
        }

        issues.Add(
            new MediaSortExecutionIssue(
                MediaSortExecutionIssueKind.RecoveryStorageInsufficientSpace,
                $"Nicht genügend sicherer freier Speicher für die persistente Recovery-Sicherung. "
                + $"Benötigt für die größte atomare Move-Gruppe einschließlich Reserve: "
                + $"{FormatBytes(requiredWithReserve)}, verfügbar: {FormatBytes(available)}.",
                RequiredBytes:
                    requiredWithReserve,
                AvailableBytes:
                    available));
    }

    private static void AddRuntimeStateIssues(
        IEnumerable<MediaSortExecutionGroup> groups,
        ICollection<MediaSortExecutionIssue> issues,
        CancellationToken cancellationToken)
    {
        foreach (var group in groups)
        {
            foreach (var operation in group.Operations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(
                        operation.SourcePath))
                {
                    issues.Add(
                        new MediaSortExecutionIssue(
                            MediaSortExecutionIssueKind.SourceMissing,
                            "Die Quelldatei existiert nicht mehr. "
                            + "Der Dry Run muss neu erzeugt werden.",
                            group.Id,
                            operation.SourcePath,
                            operation.DestinationPath));

                    continue;
                }

                FileInfo info;

                try
                {
                    info =
                        new FileInfo(
                            operation.SourcePath);

                    _ =
                        info.Length;

                    _ =
                        info.LastWriteTimeUtc;
                }
                catch (Exception exception) when (
                    exception is IOException
                        or UnauthorizedAccessException)
                {
                    issues.Add(
                        new MediaSortExecutionIssue(
                            MediaSortExecutionIssueKind.SourceSnapshotUnavailable,
                            "Der aktuelle Zustand der Quelldatei konnte nicht zuverlässig gelesen werden: "
                            + exception.Message,
                            group.Id,
                            operation.SourcePath,
                            operation.DestinationPath));
                    continue;
                }

                if (info.Length
                    != operation.SourceSnapshot.Length)
                {
                    issues.Add(
                        new MediaSortExecutionIssue(
                            MediaSortExecutionIssueKind.SourceLengthChanged,
                            $"Die Quelldatei hat sich seit dem Dry Run in der Größe verändert "
                            + $"({operation.SourceSnapshot.Length:N0} → {info.Length:N0} Bytes). "
                            + "Vor einer Ausführung ist ein neuer Dry Run erforderlich.",
                            group.Id,
                            operation.SourcePath,
                            operation.DestinationPath));
                }

                if (info.LastWriteTimeUtc
                    != operation.SourceSnapshot.LastWriteTimeUtc)
                {
                    issues.Add(
                        new MediaSortExecutionIssue(
                            MediaSortExecutionIssueKind.SourceLastWriteTimeChanged,
                            "Der Schreibzeitpunkt der Quelldatei hat sich seit dem Dry Run verändert. "
                            + "Vor einer Ausführung ist ein neuer Dry Run erforderlich.",
                            group.Id,
                            operation.SourcePath,
                            operation.DestinationPath));
                }

                if (File.Exists(
                        operation.DestinationPath)
                    || Directory.Exists(
                        operation.DestinationPath))
                {
                    issues.Add(
                        new MediaSortExecutionIssue(
                            MediaSortExecutionIssueKind.DestinationAppeared,
                            "Das Ziel existiert inzwischen. Es wird niemals überschrieben; "
                            + "der Dry Run muss neu bewertet werden.",
                            group.Id,
                            operation.SourcePath,
                            operation.DestinationPath));
                }
            }
        }
    }

    private static long CalculatePeakPersistentRecoveryBytes(
        IEnumerable<MediaSortExecutionGroup> groups)
    {
        long peak = 0;

        foreach (var group in groups)
        {
            long groupBytes = 0;

            foreach (var operation in group.Operations)
            {
                if (operation.Kind
                    != FileOperationKind.Move)
                {
                    continue;
                }

                try
                {
                    groupBytes =
                        checked(
                            groupBytes
                            + Math.Max(
                                0,
                                operation.SourceSnapshot.Length));
                }
                catch (OverflowException)
                {
                    groupBytes =
                        long.MaxValue;
                    break;
                }
            }

            peak =
                Math.Max(
                    peak,
                    groupBytes);
        }

        return peak;
    }

    private static string ComputeFingerprint(
        IEnumerable<MediaSortExecutionGroup> groups)
    {
        using var stream =
            new MemoryStream();

        using (var writer =
               new BinaryWriter(
                   stream,
                   Encoding.UTF8,
                   leaveOpen: true))
        {
            writer.Write(
                FingerprintVersion);

            foreach (var group in groups
                         .OrderBy(group =>
                             group.Id,
                             StringComparer.Ordinal))
            {
                writer.Write(
                    group.Id);

                writer.Write(
                    group.IsCompanionGroup);

                writer.Write(
                    group.RequiresAtomicExecution);

                foreach (var operation in group.Operations
                             .OrderBy(operation =>
                                 operation.Index))
                {
                    writer.Write(
                        (int)operation.Kind);

                    writer.Write(
                        NormalizeForFingerprint(
                            operation.SourcePath));

                    writer.Write(
                        NormalizeForFingerprint(
                            operation.DestinationPath));

                    writer.Write(
                        operation.IsSidecar);

                    writer.Write(
                        operation.SourceSnapshot.Length);

                    writer.Write(
                        operation.SourceSnapshot.LastWriteTimeUtc.Ticks);
                }
            }
        }

        var hash =
            SHA256.HashData(
                stream.ToArray());

        return Convert.ToHexString(
            hash);
    }

    private static string CreateCompanionGroupId(
        MediaCompanionGroupPlan group)
    {
        return "cmp-"
               + ShortHash(
                   NormalizeForFingerprint(
                       group.DirectoryPath)
                   + "\n"
                   + group.CompanionStem.ToUpperInvariant());
    }

    private static string CreateSingleGroupId(
        string sourcePath)
    {
        return "single-"
               + ShortHash(
                   NormalizeForFingerprint(
                       sourcePath));
    }

    private static string ShortHash(
        string value)
    {
        var hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    value));

        return Convert.ToHexString(
                hash.AsSpan(
                    0,
                    8));
    }

    private static string NormalizeForFingerprint(
        string path)
    {
        var fullPath =
            NormalizePath(
                path);

        return OperatingSystem.IsWindows()
            ? fullPath.ToUpperInvariant()
            : fullPath;
    }

    private static string NormalizePath(
        string path)
    {
        return Path.GetFullPath(
            path);
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    private static DriveInfo? TryResolveVolume(
        string path)
    {
        var fullPath =
            Path.GetFullPath(
                path);

        var comparison =
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        DriveInfo? bestMatch = null;
        var bestLength = -1;

        foreach (var drive in DriveInfo.GetDrives())
        {
            string root;

            try
            {
                root =
                    Path.GetFullPath(
                        drive.RootDirectory.FullName);
            }
            catch
            {
                continue;
            }

            var normalizedRoot =
                root.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            if (string.IsNullOrEmpty(
                    normalizedRoot))
            {
                normalizedRoot =
                    Path.DirectorySeparatorChar.ToString();
            }

            var rootWithSeparator =
                normalizedRoot
                == Path.DirectorySeparatorChar.ToString()
                    ? normalizedRoot
                    : normalizedRoot
                      + Path.DirectorySeparatorChar;

            var matches =
                string.Equals(
                    fullPath,
                    normalizedRoot,
                    comparison)
                || fullPath.StartsWith(
                    rootWithSeparator,
                    comparison);

            if (!matches
                || normalizedRoot.Length
                    <= bestLength)
            {
                continue;
            }

            bestMatch =
                drive;

            bestLength =
                normalizedRoot.Length;
        }

        return bestMatch;
    }

    private static string FormatBytes(
        long bytes)
    {
        string[] units =
        {
            "B",
            "KB",
            "MB",
            "GB",
            "TB"
        };

        var value =
            Math.Max(
                0,
                (double)bytes);

        var unitIndex = 0;

        while (value >= 1024
               && unitIndex
                   < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value:N0} {units[unitIndex]}"
            : $"{value:N1} {units[unitIndex]}";
    }

    private sealed record PendingOperation(
        FileOperationKind Kind,
        string SourcePath,
        string DestinationPath,
        bool IsSidecar,
        MediaSortSourceSnapshot SourceSnapshot);

    private sealed record PendingGroup(
        string Id,
        string DisplayName,
        bool IsCompanionGroup,
        bool RequiresAtomicExecution,
        IReadOnlyList<PendingOperation> Operations);

    private sealed record ExpandedBuildResult(
        IReadOnlyList<PendingGroup> PendingGroups,
        IReadOnlyList<FileOperationRequest> Requests);
}
