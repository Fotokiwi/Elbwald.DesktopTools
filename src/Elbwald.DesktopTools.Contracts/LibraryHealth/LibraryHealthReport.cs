namespace Elbwald.DesktopTools.Contracts.LibraryHealth;

public sealed record LibraryHealthReport(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<LibraryHealthEndpointResult> Endpoints,
    IReadOnlyList<LibraryHealthIssue> Issues)
{
    public int ProblemCount => Issues.Count(issue => issue.Severity == LibraryHealthSeverity.Problem);
    public int WarningCount => Issues.Count(issue => issue.Severity == LibraryHealthSeverity.Warning);
    public bool HasProblems => ProblemCount > 0;
}
