using Elbwald.DesktopTools.Contracts.Media.Sorting;
using Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;

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

                MediaSortIssueKind.CompanionGroup =>
                    "Dateigruppe",

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

    public PhotoSortIssueViewModel(
        MediaSortExecutionProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        Path =
            problem.SourcePath;

        Message =
            problem.Message
            + " "
            + problem.StorageHealth.Summary;

        Category =
            problem.SuspectsPhysicalDevice
                ? "Datenträger"
                : "Lesefehler";

        Severity =
            problem.SuspectsPhysicalDevice
                ? "Problem"
                : "Warnung";

        IsProblem =
            problem.SuspectsPhysicalDevice;

        IsWarning =
            !problem.SuspectsPhysicalDevice;

        IsInfo =
            false;
    }

    public string Path { get; }

    public string Message { get; }

    public string Category { get; }

    public string Severity { get; }

    public bool IsProblem { get; }

    public bool IsWarning { get; }

    public bool IsInfo { get; }
}
