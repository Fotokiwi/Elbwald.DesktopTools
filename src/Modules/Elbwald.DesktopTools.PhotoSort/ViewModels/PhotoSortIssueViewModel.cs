using Elbwald.DesktopTools.Contracts.Media.Sorting;

namespace Elbwald.DesktopTools.PhotoSort.ViewModels;

public sealed class PhotoSortIssueViewModel
{
    public PhotoSortIssueViewModel(
        MediaSortIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        Path =
            issue.Path;

        Message =
            issue.Message;

        Category =
            issue.Kind switch
            {
                MediaSortIssueKind.DateReview =>
                    "Datum prüfen",

                MediaSortIssueKind.MissingCaptureDate =>
                    "Datum fehlt",

                MediaSortIssueKind.FileOperationConflict =>
                    "Dateikonflikt",

                MediaSortIssueKind.DestinationInsideSource =>
                    "Pfad prüfen",

                MediaSortIssueKind.MissingCameraInformation =>
                    "Kamera",

                _ =>
                    "Hinweis"
            };

        Severity =
            issue.Severity switch
            {
                MediaSortIssueSeverity.Problem =>
                    "Problem",

                MediaSortIssueSeverity.Warning =>
                    "Warnung",

                _ =>
                    "Hinweis"
            };

        IsProblem =
            issue.Severity == MediaSortIssueSeverity.Problem;

        IsWarning =
            issue.Severity == MediaSortIssueSeverity.Warning;

        IsInfo =
            issue.Severity == MediaSortIssueSeverity.Info;
    }

    public string Path { get; }

    public string Message { get; }

    public string Category { get; }

    public string Severity { get; }

    public bool IsProblem { get; }

    public bool IsWarning { get; }

    public bool IsInfo { get; }
}
