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
        SeverityValue = issue.Severity;
        KindValue = issue.Kind;
        Severity = issue.Severity switch
        {
            LibraryHealthSeverity.Problem => "Problem",
            LibraryHealthSeverity.Warning => "Warnung",
            _ => "Info"
        };
        Kind = issue.Kind switch
        {
            LibraryHealthIssueKind.VolumeUnavailable => "Speicherort offline",
            LibraryHealthIssueKind.PathMissing => "Bibliotheksordner fehlt",
            LibraryHealthIssueKind.AmbiguousVolume => "Mehrdeutiges Volume",
            LibraryHealthIssueKind.InvalidConfiguration => "Ungültige Konfiguration",
            LibraryHealthIssueKind.ScanError => "Scan-Fehler",
            LibraryHealthIssueKind.MetadataProblem => "Metadatenproblem",
            LibraryHealthIssueKind.MissingCaptureDate => "Aufnahmedatum fehlt",
            LibraryHealthIssueKind.IndexedMediaUnavailable => "Index ohne erreichbaren Ort",
            LibraryHealthIssueKind.IndexedMediaOffline => "Medien derzeit offline",
            LibraryHealthIssueKind.IndexedMediaMissing => "Medien fehlen",
            LibraryHealthIssueKind.IndexedMediaUnconfigured => "Nicht mehr konfigurierter Speicherort",
            LibraryHealthIssueKind.StaleIndexLocation => "Index sollte aktualisiert werden",
            LibraryHealthIssueKind.ConflictingCaptureDate => "Datumsquellen widersprechen sich",
            LibraryHealthIssueKind.SuspiciousCaptureDate => "Aufnahmedatum unplausibel",
            LibraryHealthIssueKind.MissingDimensions => "Bildabmessungen fehlen",
            _ => "Hinweis"
        };
        Area = GetArea(issue.Kind);
        Message = issue.Message;
        Path = issue.Path ?? string.Empty;
        HasPath = !string.IsNullOrWhiteSpace(issue.Path);
    }

    public string EndpointName { get; }

    public LibraryHealthSeverity SeverityValue { get; }

    public LibraryHealthIssueKind KindValue { get; }

    public string Severity { get; }

    public string Kind { get; }

    public string Area { get; }

    public string Message { get; }

    public string Path { get; }

    public bool HasPath { get; }

    public bool MatchesSearch(string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        var search = searchText.Trim();
        return EndpointName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || Severity.Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || Kind.Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || Area.Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || Message.Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || Path.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private static string GetArea(LibraryHealthIssueKind kind) =>
        kind switch
        {
            LibraryHealthIssueKind.VolumeUnavailable
                or LibraryHealthIssueKind.PathMissing
                or LibraryHealthIssueKind.AmbiguousVolume
                or LibraryHealthIssueKind.InvalidConfiguration => "Speicherorte",

            LibraryHealthIssueKind.IndexedMediaUnavailable
                or LibraryHealthIssueKind.IndexedMediaOffline
                or LibraryHealthIssueKind.IndexedMediaMissing
                or LibraryHealthIssueKind.IndexedMediaUnconfigured
                or LibraryHealthIssueKind.StaleIndexLocation => "Medienindex",

            LibraryHealthIssueKind.MetadataProblem
                or LibraryHealthIssueKind.MissingCaptureDate
                or LibraryHealthIssueKind.ConflictingCaptureDate
                or LibraryHealthIssueKind.SuspiciousCaptureDate
                or LibraryHealthIssueKind.MissingDimensions => "Metadaten",

            _ => "Allgemein"
        };
}
