namespace Elbwald.DesktopTools.Contracts.Journaling;

public enum OperationJournalState
{
    Prepared,
    Executing,
    Committed,
    Completed,
    Failed,
    RecoveryRequired,
    Recovered,
    Cancelled
}
