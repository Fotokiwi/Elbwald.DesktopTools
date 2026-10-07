namespace Elbwald.DesktopTools.Contracts.Journaling;

public interface IOperationJournalMaintenance
{
    Task<OperationJournalIntegrityReport> AnalyzeIntegrityAsync(
        CancellationToken cancellationToken = default);

    Task<OperationJournalRepairResult> RepairTrailingRecordAsync(
        CancellationToken cancellationToken = default);
}
