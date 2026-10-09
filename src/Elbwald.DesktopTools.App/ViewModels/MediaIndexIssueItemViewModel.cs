using Elbwald.DesktopTools.Contracts.MediaIndex;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class MediaIndexIssueItemViewModel
{
    public MediaIndexIssueItemViewModel(MediaIndexIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        EndpointId = issue.EndpointId;
        Kind = issue.Kind switch
        {
            MediaIndexIssueKind.EndpointUnavailable => "Endpunkt nicht verfügbar",
            MediaIndexIssueKind.ScanError => "Scan-Fehler",
            MediaIndexIssueKind.FileReadError => "Datei nicht lesbar",
            MediaIndexIssueKind.InvalidRelativePath => "Ungültiger relativer Pfad",
            MediaIndexIssueKind.IndexStoreError => "Index-Datenbank",
            MediaIndexIssueKind.SymbolicLinkSkipped => "Symbolischer Link übersprungen",
            _ => "Hinweis"
        };
        Message = issue.Message;
        Path = issue.Path ?? string.Empty;
        HasPath = !string.IsNullOrWhiteSpace(issue.Path);
    }

    public string EndpointId { get; }

    public string Kind { get; }

    public string Message { get; }

    public string Path { get; }

    public bool HasPath { get; }
}
