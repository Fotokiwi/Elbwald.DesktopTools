namespace Elbwald.DesktopTools.Contracts.MediaIndex;

public sealed record MediaIndexSearchResult(
    MediaIndexItem Item,
    string? RepresentativeEndpointId,
    string? RepresentativeRelativePath,
    bool RepresentativeIsPresent,
    int PresentLocationCount,
    int HistoricalLocationCount);
