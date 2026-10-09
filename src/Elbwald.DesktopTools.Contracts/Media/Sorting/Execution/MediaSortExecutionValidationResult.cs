namespace Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;

public sealed class MediaSortExecutionValidationResult
{
    public MediaSortExecutionValidationResult(
        IEnumerable<MediaSortExecutionIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = issues.ToArray();
    }

    public IReadOnlyList<MediaSortExecutionIssue> Issues { get; }

    public bool IsValid =>
        Issues.Count == 0;

    public static MediaSortExecutionValidationResult Valid { get; } =
        new(Array.Empty<MediaSortExecutionIssue>());
}
