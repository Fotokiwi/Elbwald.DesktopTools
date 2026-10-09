namespace Elbwald.DesktopTools.Contracts.MediaIndex;

public sealed record MediaIndexIssue(
    string EndpointId,
    MediaIndexIssueKind Kind,
    string Message,
    string? Path = null);
