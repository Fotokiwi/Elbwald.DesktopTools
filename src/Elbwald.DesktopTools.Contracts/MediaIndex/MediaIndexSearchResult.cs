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
    int Rating = 0,
    string? CameraMake = null,
    string? CameraModel = null,
    double? Latitude = null,
    double? Longitude = null);
