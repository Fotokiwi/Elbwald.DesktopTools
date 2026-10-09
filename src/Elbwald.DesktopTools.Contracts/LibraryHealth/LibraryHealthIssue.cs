namespace Elbwald.DesktopTools.Contracts.LibraryHealth;

public sealed record LibraryHealthIssue(
    string EndpointId,
    LibraryHealthIssueKind Kind,
    LibraryHealthSeverity Severity,
    string Message,
    string? Path = null);
