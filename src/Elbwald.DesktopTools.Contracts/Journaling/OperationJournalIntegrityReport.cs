namespace Elbwald.DesktopTools.Contracts.Journaling;

public sealed record OperationJournalIntegrityReport(
    OperationJournalIntegrityState State,
    int CorruptLineCount,
    long JournalLengthBytes,
    long? RepairablePrefixLengthBytes = null)
{
    public bool IsClean =>
        State == OperationJournalIntegrityState.Clean;

    public bool CanRepairAutomatically =>
        State == OperationJournalIntegrityState.RepairableTrailingRecord;
}
