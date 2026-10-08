using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.MediaAnalyzer.ViewModels;

public sealed class AnalysisIssueViewModel
{
    public AnalysisIssueViewModel(
        MediaAnalysisIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        Path = issue.Path;
        Message = issue.Message;

        Kind = issue.Kind switch
        {
            MediaAnalysisIssueKind.ScanError =>
                "Dateisystem",

            MediaAnalysisIssueKind.MetadataError =>
                "Metadaten",

            MediaAnalysisIssueKind.MetadataWarning =>
                "Parser",

            _ =>
                issue.Kind.ToString()
        };

        SeverityLabel = issue.Severity switch
        {
            MediaAnalysisSeverity.Problem =>
                "Problem",

            MediaAnalysisSeverity.Warning =>
                "Warnung",

            _ =>
                "Hinweis"
        };

        IsProblem =
            issue.Severity == MediaAnalysisSeverity.Problem;

        IsWarning =
            issue.Severity == MediaAnalysisSeverity.Warning;

        IsInfo =
            issue.Severity == MediaAnalysisSeverity.Info;
    }

    public string SeverityLabel { get; }

    public string Kind { get; }

    public string Path { get; }

    public string Message { get; }

    public bool IsProblem { get; }

    public bool IsWarning { get; }

    public bool IsInfo { get; }
}
