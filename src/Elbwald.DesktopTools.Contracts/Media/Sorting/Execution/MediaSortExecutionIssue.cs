namespace Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;

public sealed record MediaSortExecutionIssue(
    MediaSortExecutionIssueKind Kind,
    string Message,
    string? GroupId = null,
    string? SourcePath = null,
    string? DestinationPath = null,
    long? RequiredBytes = null,
    long? AvailableBytes = null);
