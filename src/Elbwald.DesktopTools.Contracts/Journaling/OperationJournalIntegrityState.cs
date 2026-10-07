namespace Elbwald.DesktopTools.Contracts.Journaling;

public enum OperationJournalIntegrityState
{
    Clean,
    RepairableTrailingRecord,
    UnsafeCorruption
}
