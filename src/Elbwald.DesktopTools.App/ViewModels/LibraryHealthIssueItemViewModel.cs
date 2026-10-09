using Elbwald.DesktopTools.Contracts.LibraryHealth;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class LibraryHealthIssueItemViewModel
{
    public LibraryHealthIssueItemViewModel(
        string endpointName,
        LibraryHealthIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        EndpointName = string.IsNullOrWhiteSpace(endpointName)
            ? issue.EndpointId
            : endpointName;
        Severity = issue.Severity switch
        {
            LibraryHealthSeverity.Problem => "Problem",
            LibraryHealthSeverity.Warning => "Warnung",
            _ => "Info"
        };
        Kind = issue.Kind switch
        {
            LibraryHealthIssueKind.VolumeUnavailable => "Volume nicht verfügbar",
            LibraryHealthIssueKind.PathMissing => "Pfad fehlt",
            LibraryHealthIssueKind.AmbiguousVolume => "Mehrdeutiges Volume",
            LibraryHealthIssueKind.InvalidConfiguration => "Ungültige Konfiguration",
            LibraryHealthIssueKind.ScanError => "Scan-Fehler",
            LibraryHealthIssueKind.MetadataProblem => "Metadatenproblem",
            LibraryHealthIssueKind.MissingCaptureDate => "Aufnahmedatum fehlt",
            _ => "Hinweis"
        };
        Message = issue.Message;
        Path = issue.Path ?? string.Empty;
        HasPath = !string.IsNullOrWhiteSpace(issue.Path);
    }

    public string EndpointName { get; }

    public string Severity { get; }

    public string Kind { get; }

    public string Message { get; }

    public string Path { get; }

    public bool HasPath { get; }
}
