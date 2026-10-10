using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.OperationHistory;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class OperationHistoryViewModel : ObservableObject
{
    public const string UndoConfirmationPhrase = "RÜCKGÄNGIG";

    private readonly IOperationHistoryStore _historyStore;
    private readonly IFileOperationPlanner _fileOperationPlanner;
    private readonly IFileOperationExecutor _fileOperationExecutor;
    private readonly IMediaIndexService _mediaIndex;

    [ObservableProperty]
    private IReadOnlyList<OperationHistoryBatchViewModel> batches = Array.Empty<OperationHistoryBatchViewModel>();

    [ObservableProperty]
    private OperationHistoryBatchViewModel? selectedBatch;

    [ObservableProperty]
    private string statusMessage = "Vorgangsverlauf wird geladen …";

    [ObservableProperty]
    private string undoCheckMessage = "Wähle einen Vorgang aus, um Details und Undo-Status zu sehen.";

    [ObservableProperty]
    private string confirmationText = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isUndoReady;

    [ObservableProperty]
    private int progressPercent;

    public OperationHistoryViewModel(
        IOperationHistoryStore historyStore,
        IFileOperationPlanner fileOperationPlanner,
        IFileOperationExecutor fileOperationExecutor,
        IMediaIndexService mediaIndex)
    {
        _historyStore = historyStore;
        _fileOperationPlanner = fileOperationPlanner;
        _fileOperationExecutor = fileOperationExecutor;
        _mediaIndex = mediaIndex;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        CheckUndoCommand = new AsyncRelayCommand(CheckUndoAsync, CanCheckUndo);
        ExecuteUndoCommand = new AsyncRelayCommand(ExecuteUndoAsync, CanExecuteUndo);

        _ = RefreshAsync();
    }

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand CheckUndoCommand { get; }
    public IAsyncRelayCommand ExecuteUndoCommand { get; }

    public bool HasBatches => Batches.Count > 0;
    public bool HasSelection => SelectedBatch is not null;
    public bool HasNoBatches => !IsBusy && Batches.Count == 0;
    public string ConfirmationHint => $"Zur zweiten Bestätigung exakt {UndoConfirmationPhrase} eingeben.";

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        CheckUndoCommand.NotifyCanExecuteChanged();
        ExecuteUndoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasNoBatches));
    }

    partial void OnBatchesChanged(IReadOnlyList<OperationHistoryBatchViewModel> value)
    {
        OnPropertyChanged(nameof(HasBatches));
        OnPropertyChanged(nameof(HasNoBatches));
    }

    partial void OnSelectedBatchChanged(OperationHistoryBatchViewModel? value)
    {
        ResetUndoState();
        OnPropertyChanged(nameof(HasSelection));
        CheckUndoCommand.NotifyCanExecuteChanged();
        ExecuteUndoCommand.NotifyCanExecuteChanged();
    }

    partial void OnConfirmationTextChanged(string value)
    {
        ExecuteUndoCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsUndoReadyChanged(bool value)
    {
        ExecuteUndoCommand.NotifyCanExecuteChanged();
    }

    private bool CanCheckUndo() => !IsBusy && SelectedBatch?.CanOfferUndo == true;

    private bool CanExecuteUndo() =>
        !IsBusy
        && IsUndoReady
        && SelectedBatch?.CanOfferUndo == true
        && string.Equals(ConfirmationText, UndoConfirmationPhrase, StringComparison.Ordinal);

    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var entries = await _historyStore.GetRecentAsync(5000);
            var revertedIds = entries
                .Where(x => !string.IsNullOrWhiteSpace(x.ReversalOfBatchId))
                .Select(x => x.ReversalOfBatchId!)
                .ToHashSet(StringComparer.Ordinal);

            Batches = entries
                .GroupBy(x => x.BatchId, StringComparer.Ordinal)
                .Select(group => new OperationHistoryBatchViewModel(
                    group.Key,
                    group.OrderBy(x => x.CompletedUtc).ToArray(),
                    revertedIds.Contains(group.Key)))
                .OrderByDescending(x => x.CompletedUtc)
                .ToArray();

            SelectedBatch = Batches.FirstOrDefault();
            StatusMessage = Batches.Count == 0
                ? "Noch keine protokollierten Benutzer-Dateioperationen vorhanden."
                : $"{Batches.Count:N0} Vorgang/Vorgänge geladen · neueste zuerst.";
        }
        catch (Exception ex)
        {
            Batches = Array.Empty<OperationHistoryBatchViewModel>();
            SelectedBatch = null;
            StatusMessage = "Vorgangsverlauf konnte nicht geladen werden: " + ex.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CheckUndoAsync()
    {
        var batch = SelectedBatch;
        if (batch is null || !batch.CanOfferUndo) return;

        IsBusy = true;
        ProgressPercent = 0;
        ConfirmationText = string.Empty;
        IsUndoReady = false;
        UndoCheckMessage = "Undo-Sicherheitsprüfung läuft …";

        try
        {
            var validation = await ValidateUndoBatchAsync(batch.BatchId);
            if (!validation.IsSafe)
            {
                UndoCheckMessage = "Undo blockiert: " + validation.Message;
                return;
            }

            IsUndoReady = true;
            UndoCheckMessage = $"Undo ist aktuell sicher möglich: {validation.Entries.Count:N0} Dateioperation(en) wurden vollständig geprüft. Vor der Ausführung wird alles erneut verifiziert.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException)
        {
            UndoCheckMessage = "Undo-Prüfung fehlgeschlagen: " + ex.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteUndoAsync()
    {
        var selected = SelectedBatch;
        if (selected is null || !CanExecuteUndo()) return;

        IsBusy = true;
        ProgressPercent = 0;
        UndoCheckMessage = "Unmittelbare Sicherheitsprüfung vor Undo …";

        try
        {
            var validation = await ValidateUndoBatchAsync(selected.BatchId);
            if (!validation.IsSafe)
            {
                IsUndoReady = false;
                ConfirmationText = string.Empty;
                UndoCheckMessage = "Undo blockiert: " + validation.Message;
                return;
            }

            var requests = validation.Entries
                .Select(x => FileOperationRequest.Move(x.DestinationPath, x.SourcePath))
                .ToArray();

            var plan = _fileOperationPlanner.CreatePlan(requests);
            if (!plan.CanExecute)
            {
                IsUndoReady = false;
                ConfirmationText = string.Empty;
                UndoCheckMessage = "Undo blockiert: Der Reverse-Dateioperationsplan ist nicht konfliktfrei.";
                return;
            }

            var progress = new Progress<FileOperationProgress>(p =>
            {
                ProgressPercent = p.Percentage;
                UndoCheckMessage = $"Rückgängig: {p.CompletedCount:N0} von {p.TotalCount:N0} …";
            });

            // Nach der vollständigen Vorprüfung gibt es bewusst keinen Benutzerabbruch mehr.
            var result = await _fileOperationExecutor.ExecuteAsync(plan, progress, CancellationToken.None);
            if (!result.IsSuccessful)
            {
                UndoCheckMessage = result.RequiresRecovery
                    ? "Undo nicht vollständig abgeschlossen. Recovery ist erforderlich."
                    : result.WasBlockedByRecoveryState
                        ? "Undo blockiert: Zuerst offenen Recovery-Zustand klären."
                        : result.WasBlockedByProcessLock
                            ? "Undo blockiert: Eine andere Dateioperation ist aktiv."
                            : "Undo nicht vollständig ausgeführt: " + (result.ErrorMessage ?? result.State.ToString());
                IsUndoReady = false;
                ConfirmationText = string.Empty;
                return;
            }

            var reversalBatchId = result.TransactionId?.ToString("D") ?? Guid.NewGuid().ToString("D");
            var completedUtc = DateTimeOffset.UtcNow;
            var reversalEntries = validation.Entries.Select(entry =>
                new OperationHistoryEntry(
                    reversalBatchId,
                    "UndoRename",
                    entry.DestinationPath,
                    entry.SourcePath,
                    entry.Sha256,
                    entry.Length,
                    entry.MediaItemId,
                    completedUtc,
                    selected.BatchId)).ToArray();

            await _historyStore.AppendBatchAsync(reversalEntries);

            ProgressPercent = 100;
            IsUndoReady = false;
            ConfirmationText = string.Empty;
            await RefreshAfterUndoAsync(selected.BatchId);
            UndoCheckMessage = $"Rückgängig abgeschlossen: {reversalEntries.Length:N0} Dateioperation(en). Der Undo-Vorgang wurde ebenfalls protokolliert.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException or ArgumentException)
        {
            UndoCheckMessage = "Undo blockiert: " + ex.GetBaseException().Message;
            IsUndoReady = false;
            ConfirmationText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshAfterUndoAsync(string originalBatchId)
    {
        var entries = await _historyStore.GetRecentAsync(5000);
        var revertedIds = entries
            .Where(x => !string.IsNullOrWhiteSpace(x.ReversalOfBatchId))
            .Select(x => x.ReversalOfBatchId!)
            .ToHashSet(StringComparer.Ordinal);

        Batches = entries
            .GroupBy(x => x.BatchId, StringComparer.Ordinal)
            .Select(group => new OperationHistoryBatchViewModel(
                group.Key,
                group.OrderBy(x => x.CompletedUtc).ToArray(),
                revertedIds.Contains(group.Key)))
            .OrderByDescending(x => x.CompletedUtc)
            .ToArray();

        SelectedBatch = Batches.FirstOrDefault(x => string.Equals(x.BatchId, originalBatchId, StringComparison.Ordinal))
            ?? Batches.FirstOrDefault();
        StatusMessage = $"{Batches.Count:N0} Vorgang/Vorgänge geladen · neueste zuerst.";
    }

    private async Task<UndoValidationResult> ValidateUndoBatchAsync(string batchId)
    {
        if (await _historyStore.HasReversalAsync(batchId))
        {
            return UndoValidationResult.Blocked("Dieser Vorgang wurde bereits zurückgesetzt.");
        }

        var entries = await _historyStore.GetBatchAsync(batchId);
        if (entries.Count == 0)
        {
            return UndoValidationResult.Blocked("Der Vorgang ist nicht mehr in der Historie vorhanden.");
        }

        if (entries.Any(x => !string.Equals(x.OperationType, "Rename", StringComparison.OrdinalIgnoreCase)))
        {
            return UndoValidationResult.Blocked("0055 unterstützt Undo ausschließlich für Rename-Vorgänge.");
        }

        var sourcePaths = new HashSet<string>(GetPathComparer());
        var destinationPaths = new HashSet<string>(GetPathComparer());
        foreach (var entry in entries)
        {
            var source = Path.GetFullPath(entry.SourcePath);
            var destination = Path.GetFullPath(entry.DestinationPath);

            if (!sourcePaths.Add(source) || !destinationPaths.Add(destination))
            {
                return UndoValidationResult.Blocked("Der Vorgang enthält doppelte Quell- oder Zielpfade.");
            }

            if (File.Exists(source) || Directory.Exists(source))
            {
                return UndoValidationResult.Blocked($"Alter Name ist inzwischen belegt: {source}");
            }

            if (!File.Exists(destination))
            {
                return UndoValidationResult.Blocked($"Aktuelle Datei fehlt: {destination}");
            }

            var info = new FileInfo(destination);
            if (info.Length != entry.Length)
            {
                return UndoValidationResult.Blocked($"Dateigröße hat sich verändert: {destination}");
            }

            var currentHash = await ComputeSha256Async(destination);
            if (!string.Equals(currentHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return UndoValidationResult.Blocked($"Dateiinhalt hat sich verändert: {destination}");
            }

            if (!string.IsNullOrWhiteSpace(entry.MediaItemId))
            {
                // Die Historie enthält mit SHA-256 + Länge bereits einen selbständigen,
                // pfadunabhängigen Identitäts-Snapshot. Ein Rename darf deshalb nicht
                // nur deshalb un-undo-bar werden, weil der Media Index den neuen Pfad
                // noch nicht eingelesen hat.
                //
                // Ist die gespeicherte Media-ID weiterhin im Index vorhanden, prüfen
                // wir sie zusätzlich gegen genau diesen Snapshot. Fehlt sie aktuell im
                // Index, bleibt der kryptografisch verifizierte History-Snapshot die
                // maßgebliche Identität für das Undo.
                var indexedById = await _mediaIndex.GetItemAsync(entry.MediaItemId);
                if (indexedById is not null
                    && (!string.Equals(indexedById.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase)
                        || indexedById.Length != entry.Length))
                {
                    return UndoValidationResult.Blocked($"Die gespeicherte Media-ID verweist inzwischen auf einen anderen Inhalt: {destination}");
                }
            }
        }

        var reversePlan = _fileOperationPlanner.CreatePlan(
            entries.Select(x => FileOperationRequest.Move(x.DestinationPath, x.SourcePath)));
        if (!reversePlan.CanExecute)
        {
            return UndoValidationResult.Blocked("Der Reverse-Plan enthält Dateisystemkonflikte.");
        }

        return UndoValidationResult.Safe(entries);
    }

    private void ResetUndoState()
    {
        IsUndoReady = false;
        ConfirmationText = string.Empty;
        ProgressPercent = 0;
        UndoCheckMessage = SelectedBatch switch
        {
            null => "Wähle einen Vorgang aus, um Details und Undo-Status zu sehen.",
            { IsReverted: true } => "Dieser Vorgang wurde bereits zurückgesetzt.",
            { IsUndoOperation: true } => "Dies ist selbst ein protokollierter Undo-Vorgang.",
            { CanOfferUndo: true } => "Undo ist noch nicht geprüft. Erst Sicherheitsprüfung ausführen.",
            _ => "Für diesen Vorgangstyp ist in 0055 kein Undo verfügbar."
        };
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record UndoValidationResult(
        bool IsSafe,
        string Message,
        IReadOnlyList<OperationHistoryEntry> Entries)
    {
        public static UndoValidationResult Safe(IReadOnlyList<OperationHistoryEntry> entries) =>
            new(true, string.Empty, entries);

        public static UndoValidationResult Blocked(string message) =>
            new(false, message, Array.Empty<OperationHistoryEntry>());
    }
}
