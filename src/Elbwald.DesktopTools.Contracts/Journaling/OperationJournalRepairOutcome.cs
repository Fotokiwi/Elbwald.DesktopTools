namespace Elbwald.DesktopTools.Contracts.Journaling;

public enum OperationJournalRepairOutcome
{
    NotNeeded,
    Repaired,
    UnsafeCorruption,
    Failed
}
