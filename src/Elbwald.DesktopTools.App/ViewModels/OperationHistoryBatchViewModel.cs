using Elbwald.DesktopTools.Contracts.OperationHistory;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class OperationHistoryBatchViewModel
{
    public OperationHistoryBatchViewModel(
        string batchId,
        IReadOnlyList<OperationHistoryEntry> entries,
        bool isReverted)
    {
        BatchId = batchId;
        Entries = entries.Select(x => new OperationHistoryEntryViewModel(x)).ToArray();
        RawEntries = entries;
        IsReverted = isReverted;
        OperationType = entries.FirstOrDefault()?.OperationType ?? "Unbekannt";
        CompletedUtc = entries.Max(x => x.CompletedUtc);
    }

    public string BatchId { get; }
    public string OperationType { get; }
    public DateTimeOffset CompletedUtc { get; }
    public string CompletedText => CompletedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
    public IReadOnlyList<OperationHistoryEntryViewModel> Entries { get; }
    public IReadOnlyList<OperationHistoryEntry> RawEntries { get; }
    public int FileCount => Entries.Count;
    public bool IsReverted { get; }
    public bool IsUndoOperation => RawEntries.Any(x => !string.IsNullOrWhiteSpace(x.ReversalOfBatchId));
    public bool IsRename => string.Equals(OperationType, "Rename", StringComparison.OrdinalIgnoreCase);
    public bool CanOfferUndo => IsRename && !IsReverted && !IsUndoOperation;
    public string OperationLabel => IsUndoOperation
        ? "Rückgängig"
        : IsRename
            ? "Umbenennung"
            : string.Equals(OperationType, "ExportCopy", StringComparison.OrdinalIgnoreCase)
                ? "Export"
                : string.Equals(OperationType, "DuplicateTrash", StringComparison.OrdinalIgnoreCase)
                    ? "Dublettenbereinigung"
                    : OperationType;
    public string StateLabel => IsReverted ? "Zurückgesetzt" : IsUndoOperation ? "Undo ausgeführt" : "Abgeschlossen";
    public string Summary => $"{FileCount:N0} Dateioperation(en) · {StateLabel}";
    public string ShortBatchId => BatchId.Length > 12 ? BatchId[..12] + "…" : BatchId;
}
