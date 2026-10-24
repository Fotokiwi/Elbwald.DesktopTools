using Elbwald.DesktopTools.Contracts.DuplicateFinding;
using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.OperationHistory;

namespace Elbwald.DesktopTools.Core.DuplicateFinding;

public sealed class DuplicateCleanupExecutor : IDuplicateCleanupExecutor
{
    private readonly IDuplicateCleanupPlanner _cleanupPlanner;
    private readonly IFileTrashService _trashService;
    private readonly IMediaIndexService _mediaIndexService;
    private readonly IOperationHistoryStore _historyStore;

    public DuplicateCleanupExecutor(
        IDuplicateCleanupPlanner cleanupPlanner,
        IFileTrashService trashService,
        IMediaIndexService mediaIndexService,
        IOperationHistoryStore historyStore)
    {
        ArgumentNullException.ThrowIfNull(cleanupPlanner);
        ArgumentNullException.ThrowIfNull(trashService);
        ArgumentNullException.ThrowIfNull(mediaIndexService);
        ArgumentNullException.ThrowIfNull(historyStore);

        _cleanupPlanner = cleanupPlanner;
        _trashService = trashService;
        _mediaIndexService = mediaIndexService;
        _historyStore = historyStore;
    }

    public async Task<DuplicateCleanupExecutionReport> ExecuteAsync(
        DuplicateCleanupPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        var startedAtUtc = DateTimeOffset.UtcNow;
        var items = new List<DuplicateCleanupExecutionItem>();
        var globalIssues = new List<string>();
        var succeededLocationIds = new List<long>();
        var historyEntries = new List<OperationHistoryEntry>();
        var historyBatchId = Guid.NewGuid().ToString("D");

        var initialPreflight = await _cleanupPlanner.RunPreflightAsync(
            plan,
            cancellationToken);

        if (!initialPreflight.IsReady)
        {
            globalIssues.Add(
                initialPreflight.GlobalIssues.FirstOrDefault()
                ?? initialPreflight.Items.FirstOrDefault(item => !item.IsReady)?.Message
                ?? "Der unmittelbar vor der Ausführung wiederholte Preflight hat den Bereinigungsplan blockiert.");

            foreach (var removalLocationId in plan.RemovalLocationIds.Distinct())
            {
                var preflightItem = initialPreflight.Items.FirstOrDefault(
                    item => item.LocationId == removalLocationId
                        && item.Action == DuplicateCleanupAction.Remove);

                items.Add(new DuplicateCleanupExecutionItem(
                    removalLocationId,
                    preflightItem?.EndpointId ?? string.Empty,
                    preflightItem?.RelativePath ?? string.Empty,
                    preflightItem?.ResolvedPath,
                    null,
                    DuplicateCleanupExecutionStatus.BlockedByPreflight,
                    initialPreflight.GlobalIssues.FirstOrDefault()
                        ?? preflightItem?.Message
                        ?? "Der Gesamtplan wurde vor der Ausführung durch den Preflight blockiert.",
                    0));
            }

            return Complete(
                startedAtUtc,
                plan,
                items,
                globalIssues,
                indexUpdated: true,
                historyRecorded: true,
                historyBatchId: null);
        }

        foreach (var removalLocationId in plan.RemovalLocationIds.Distinct())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                globalIssues.Add("Die Bereinigung wurde abgebrochen. Bereits in den Papierkorb verschobene Dateien werden dennoch im Index und in der Historie nachgeführt.");
                break;
            }

            var singlePlan = new DuplicateCleanupPlan(
                plan.MediaItemId,
                plan.ExpectedSha256,
                plan.ExpectedLength,
                plan.KeeperLocationId,
                [removalLocationId]);

            DuplicateCleanupPreflightReport immediatePreflight;
            try
            {
                immediatePreflight = await _cleanupPlanner.RunPreflightAsync(
                    singlePlan,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                globalIssues.Add("Die Bereinigung wurde vor dem nächsten Kandidaten abgebrochen. Bereits ausgeführte Aktionen werden nachgeführt.");
                break;
            }

            var candidate = immediatePreflight.Items.FirstOrDefault(
                item => item.LocationId == removalLocationId
                    && item.Action == DuplicateCleanupAction.Remove);

            if (!immediatePreflight.IsReady
                || candidate is null
                || !candidate.IsReady
                || string.IsNullOrWhiteSpace(candidate.ResolvedPath))
            {
                items.Add(new DuplicateCleanupExecutionItem(
                    removalLocationId,
                    candidate?.EndpointId ?? string.Empty,
                    candidate?.RelativePath ?? string.Empty,
                    candidate?.ResolvedPath,
                    null,
                    DuplicateCleanupExecutionStatus.BlockedByPreflight,
                    immediatePreflight.GlobalIssues.FirstOrDefault()
                        ?? candidate?.Message
                        ?? "Der Kandidat wurde unmittelbar vor der Papierkorb-Aktion durch den Preflight blockiert.",
                    0));
                continue;
            }

            FileTrashResult trashResult;
            try
            {
                trashResult = await _trashService.MoveToTrashAsync(
                    candidate.ResolvedPath,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                globalIssues.Add("Die Bereinigung wurde vor der Papierkorb-Aktion abgebrochen. Bereits ausgeführte Aktionen werden nachgeführt.");
                break;
            }

            if (!trashResult.Succeeded)
            {
                items.Add(new DuplicateCleanupExecutionItem(
                    removalLocationId,
                    candidate.EndpointId,
                    candidate.RelativePath,
                    candidate.ResolvedPath,
                    trashResult.TrashPath,
                    DuplicateCleanupExecutionStatus.TrashFailed,
                    trashResult.Message,
                    0));
                continue;
            }

            succeededLocationIds.Add(removalLocationId);
            items.Add(new DuplicateCleanupExecutionItem(
                removalLocationId,
                candidate.EndpointId,
                candidate.RelativePath,
                candidate.ResolvedPath,
                trashResult.TrashPath,
                DuplicateCleanupExecutionStatus.MovedToTrash,
                trashResult.Message,
                plan.ExpectedLength));

            historyEntries.Add(new OperationHistoryEntry(
                historyBatchId,
                "DuplicateTrash",
                candidate.ResolvedPath,
                trashResult.TrashPath ?? "Systempapierkorb",
                plan.ExpectedSha256,
                plan.ExpectedLength,
                plan.MediaItemId,
                DateTimeOffset.UtcNow));
        }

        var indexUpdated = succeededLocationIds.Count == 0;
        if (succeededLocationIds.Count > 0)
        {
            try
            {
                var updated = await _mediaIndexService.MarkLocationsMissingAfterControlledRemovalAsync(
                    plan.MediaItemId,
                    succeededLocationIds,
                    CancellationToken.None);
                indexUpdated = updated == succeededLocationIds.Distinct().Count();

                if (!indexUpdated)
                {
                    globalIssues.Add(
                        $"Der Medienindex konnte nur {updated:N0} von {succeededLocationIds.Distinct().Count():N0} verschobenen Fundorten gezielt nachführen. Bitte den Medienindex aktualisieren.");
                }
            }
            catch (Exception exception)
            {
                globalIssues.Add(
                    "Dateien wurden in den Papierkorb verschoben, aber der Medienindex konnte nicht vollständig aktualisiert werden: "
                    + exception.GetBaseException().Message);
            }
        }

        var historyRecorded = historyEntries.Count == 0;
        if (historyEntries.Count > 0)
        {
            try
            {
                await _historyStore.AppendBatchAsync(
                    historyEntries,
                    CancellationToken.None);
                historyRecorded = true;
            }
            catch (Exception exception)
            {
                globalIssues.Add(
                    "Die Papierkorb-Aktion wurde ausgeführt, konnte aber nicht vollständig in der Vorgangshistorie protokolliert werden: "
                    + exception.GetBaseException().Message);
            }
        }

        return Complete(
            startedAtUtc,
            plan,
            items,
            globalIssues,
            indexUpdated,
            historyRecorded,
            historyEntries.Count == 0 ? null : historyBatchId);
    }

    private static DuplicateCleanupExecutionReport Complete(
        DateTimeOffset startedAtUtc,
        DuplicateCleanupPlan plan,
        IReadOnlyList<DuplicateCleanupExecutionItem> items,
        IReadOnlyList<string> globalIssues,
        bool indexUpdated,
        bool historyRecorded,
        string? historyBatchId) =>
        new(
            startedAtUtc,
            DateTimeOffset.UtcNow,
            plan,
            items,
            globalIssues,
            indexUpdated,
            historyRecorded,
            historyBatchId);
}
