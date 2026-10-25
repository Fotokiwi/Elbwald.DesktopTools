namespace Elbwald.DesktopTools.Contracts.Consolidation;

public sealed record MediaConsolidationPlan(
    string TargetPath,
    MediaConsolidationStructureMode StructureMode,
    int IndexedSourceLocationCount,
    int UniqueContentCount,
    int DuplicateSourceLocationCount,
    int CopyCount,
    int AlreadyInTargetCount,
    int ConflictCount,
    int IssueCount,
    long PlannedCopyBytes,
    IReadOnlyList<MediaConsolidationPlanItem> Items,
    IReadOnlyList<string> Issues)
{
    public bool CanExecuteLater => CopyCount > 0 && ConflictCount == 0 && IssueCount == 0;
}
