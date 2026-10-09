using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Media.Dates;
using Elbwald.DesktopTools.Contracts.Media.Companions;

namespace Elbwald.DesktopTools.Contracts.Media.Sorting;

public sealed class MediaSortPlan
{
    public MediaSortPlan(
        string sourceRoot,
        string destinationRoot,
        MediaSortOptions options,
        IEnumerable<MediaSortPlanItem> items,
        IEnumerable<MediaSortIssue> issues,
        IEnumerable<MediaDateContextHint> dateContextHints,
        IEnumerable<MediaCompanionGroupPlan> companionGroups,
        FileOperationPlan operationPlan,
        int ignoredNonImageCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(dateContextHints);
        ArgumentNullException.ThrowIfNull(companionGroups);
        ArgumentNullException.ThrowIfNull(operationPlan);

        SourceRoot = sourceRoot;
        DestinationRoot = destinationRoot;
        Options = options;
        Items = items.ToArray();
        Issues = issues.ToArray();
        DateContextHints = dateContextHints.ToArray();
        CompanionGroups = companionGroups.ToArray();
        OperationPlan = operationPlan;
        IgnoredNonImageCount = ignoredNonImageCount;
    }

    public string SourceRoot { get; }

    public string DestinationRoot { get; }

    public MediaSortOptions Options { get; }

    public IReadOnlyList<MediaSortPlanItem> Items { get; }

    public IReadOnlyList<MediaSortIssue> Issues { get; }

    public IReadOnlyList<MediaDateContextHint> DateContextHints { get; }

    public IReadOnlyList<MediaCompanionGroupPlan> CompanionGroups { get; }

    public FileOperationPlan OperationPlan { get; }

    public int IgnoredNonImageCount { get; }

    public int PlannedFileCount =>
        Items.Count;

    public long PlannedBytes =>
        Items.Sum(item => item.File.Length);

    public int MissingCaptureDateCount =>
        Issues.Count(issue =>
            issue.Kind == MediaSortIssueKind.MissingCaptureDate);

    public int DateReviewCount =>
        Issues
            .Where(issue =>
                issue.Kind is
                    MediaSortIssueKind.MissingCaptureDate
                    or MediaSortIssueKind.DateReview
                    or MediaSortIssueKind.InsufficientDateConfidence
                    or MediaSortIssueKind.DateConflict)
            .Select(issue =>
                issue.Path)
            .Distinct(StringComparer.Ordinal)
            .Count();

    public int ProblemCount =>
        Issues.Count(issue =>
            issue.Severity == MediaSortIssueSeverity.Problem);

    public int WarningCount =>
        Issues.Count(issue =>
            issue.Severity == MediaSortIssueSeverity.Warning);

    public int InfoCount =>
        Issues.Count(issue =>
            issue.Severity == MediaSortIssueSeverity.Info);

    public int ConflictCount =>
        OperationPlan.Conflicts.Count;

    public int DateContextHintCount =>
        DateContextHints.Count;

    public int CompanionGroupCount =>
        CompanionGroups.Count;

    public int CompanionReviewCount =>
        CompanionGroups.Count(group =>
            group.State
            == MediaCompanionGroupState.Review);

    public int CompanionConflictCount =>
        CompanionGroups.Count(group =>
            group.State
            == MediaCompanionGroupState.Conflict);

    public int ProjectedSidecarCount =>
        CompanionGroups.Sum(group =>
            group.ProjectedSidecars.Count);

    public bool CanExecute =>
        ProblemCount == 0
        && CompanionConflictCount == 0
        && OperationPlan.CanExecute;
}
