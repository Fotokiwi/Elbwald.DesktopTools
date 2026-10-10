namespace Elbwald.DesktopTools.Contracts.Media.Importing;

public sealed record MediaImportCleanupPlan(
    string SourceRoot,
    string DestinationRoot,
    string SourceVolumeId,
    string DestinationVolumeId,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<MediaImportCleanupPlanItem> Items,
    IReadOnlyList<MediaImportPlanIssue> Issues,
    bool IsComplete)
{
    public int FileCount => Items.Count;
    public long TotalBytes => Items.Sum(item => item.Length);
    public bool CanExecute => IsComplete && Items.Count > 0;
}
