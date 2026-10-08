namespace Elbwald.DesktopTools.Contracts.Media;

public sealed record MediaAnalysisIssue(
    string Path,
    MediaAnalysisIssueKind Kind,
    string Message,
    MediaAnalysisSeverity Severity);
