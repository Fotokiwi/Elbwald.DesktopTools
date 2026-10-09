namespace Elbwald.DesktopTools.Contracts.Media.Importing;

public sealed record MediaImportPlan(
    string SourceRoot,
    string DestinationRoot,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<MediaImportPlanItem> Items,
    IReadOnlyList<MediaImportPlanIssue> Issues,
    bool IsComplete)
{
    public int FileCount => Items.Count;
    public int CopyCount => Items.Count(item => item.State == MediaImportPlanItemState.ReadyToCopy);
    public int AlreadyImportedCount => Items.Count(item => item.State == MediaImportPlanItemState.AlreadyImported);
    public int ConflictCount => Items.Count(item => item.State == MediaImportPlanItemState.Conflict);
    public long TotalBytes => Items.Sum(item => item.Length);
    public long CopyBytes => Items.Where(item => item.State == MediaImportPlanItemState.ReadyToCopy).Sum(item => item.Length);
    public bool CanExecute => IsComplete && CopyCount > 0;
}
