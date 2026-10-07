namespace Elbwald.DesktopTools.Contracts.Journaling;

public sealed record OperationJournalRepairResult(
    OperationJournalRepairOutcome Outcome,
    string Message,
    string? BackupPath = null)
{
    public bool WasRepaired =>
        Outcome == OperationJournalRepairOutcome.Repaired;
}
