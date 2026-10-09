using Elbwald.DesktopTools.Contracts.FileOperations;

namespace Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;

public sealed class MediaSortExecutionPlan
{
    public MediaSortExecutionPlan(
        string fingerprint,
        DateTimeOffset createdAtUtc,
        IEnumerable<MediaSortExecutionGroup> groups,
        FileOperationPlan fileOperationPlan,
        FileOperationPreflightResult fileOperationPreflight,
        IEnumerable<MediaSortExecutionIssue> planningIssues,
        IEnumerable<MediaSortExecutionIssue> validationIssues,
        long peakPersistentRecoveryBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(fileOperationPlan);
        ArgumentNullException.ThrowIfNull(fileOperationPreflight);
        ArgumentNullException.ThrowIfNull(planningIssues);
        ArgumentNullException.ThrowIfNull(validationIssues);

        Fingerprint = fingerprint;
        CreatedAtUtc = createdAtUtc;
        Groups = groups.ToArray();
        FileOperationPlan = fileOperationPlan;
        FileOperationPreflight = fileOperationPreflight;
        PlanningIssues = planningIssues.ToArray();
        ValidationIssues = validationIssues.ToArray();
        Issues = PlanningIssues
            .Concat(ValidationIssues)
            .ToArray();
        PeakPersistentRecoveryBytes = peakPersistentRecoveryBytes;
    }

    public string Fingerprint { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public IReadOnlyList<MediaSortExecutionGroup> Groups { get; }

    public FileOperationPlan FileOperationPlan { get; }

    public FileOperationPreflightResult FileOperationPreflight { get; }

    public IReadOnlyList<MediaSortExecutionIssue> PlanningIssues { get; }

    public IReadOnlyList<MediaSortExecutionIssue> ValidationIssues { get; }

    public IReadOnlyList<MediaSortExecutionIssue> Issues { get; }

    public long PeakPersistentRecoveryBytes { get; }

    public int OperationCount =>
        FileOperationPlan.Operations.Count;

    public int GroupCount =>
        Groups.Count;

    public int AtomicGroupCount =>
        Groups.Count(group =>
            group.RequiresAtomicExecution);

    public int SidecarOperationCount =>
        Groups.Sum(group =>
            group.SidecarCount);

    public long PlannedBytes =>
        Groups.Sum(group =>
            group.TotalBytes);

    public bool HasDateReviewApprovalRequirement =>
        PlanningIssues.Any(issue =>
            issue.Kind
            == MediaSortExecutionIssueKind.ReviewRequiresConfirmation);

    public bool HasBlockingPlanningIssues =>
        PlanningIssues.Any(issue =>
            issue.Kind
            != MediaSortExecutionIssueKind.ReviewRequiresConfirmation);

    public bool CanExecute =>
        PlanningIssues.Count == 0
        && ValidationIssues.Count == 0
        && FileOperationPlan.CanExecute
        && FileOperationPreflight.IsSafe;

    public bool CanExecuteAfterDateReviewApproval =>
        !HasBlockingPlanningIssues
        && ValidationIssues.Count == 0
        && FileOperationPlan.CanExecute
        && FileOperationPreflight.IsSafe;
}
