using Elbwald.DesktopTools.Contracts.FileOperations;

namespace Elbwald.DesktopTools.Contracts.Media.Sorting;

public sealed class MediaSortPlan
{
    public MediaSortPlan(
        string sourceRoot,
        string destinationRoot,
        MediaSortOptions options,
        IEnumerable<MediaSortPlanItem> items,
        IEnumerable<MediaSortIssue> issues,
        FileOperationPlan operationPlan,
        int ignoredNonImageCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(operationPlan);

        SourceRoot = sourceRoot;
        DestinationRoot = destinationRoot;
        Options = options;
        Items = items.ToArray();
        Issues = issues.ToArray();
        OperationPlan = operationPlan;
        IgnoredNonImageCount = ignoredNonImageCount;
    }

    public string SourceRoot { get; }

    public string DestinationRoot { get; }

    public MediaSortOptions Options { get; }

    public IReadOnlyList<MediaSortPlanItem> Items { get; }

    public IReadOnlyList<MediaSortIssue> Issues { get; }

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

    public bool CanExecute =>
        ProblemCount == 0
        && OperationPlan.CanExecute;
}
