namespace Elbwald.DesktopTools.Contracts.Media.Sorting;

public sealed record MediaSortIssue(
    string Path,
    MediaSortIssueKind Kind,
    MediaSortIssueSeverity Severity,
    string Message,
    int? OperationIndex = null);
