namespace Elbwald.DesktopTools.Contracts.MediaIndex;

public sealed record MediaIndexSearchResult(
    MediaIndexItem Item,
    string? RepresentativeEndpointId,
    string? RepresentativeRelativePath,
    bool RepresentativeIsPresent,
    int PresentLocationCount,
    int HistoricalLocationCount,
    string? Title = null,
    DateTime? CaptureDate = null,
    string? Description = null,
    int Rating = 0);
