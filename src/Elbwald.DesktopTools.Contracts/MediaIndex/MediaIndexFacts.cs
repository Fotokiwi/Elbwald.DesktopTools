namespace Elbwald.DesktopTools.Contracts.MediaIndex;

public sealed record MediaIndexFacts(
    string MediaItemId,
    DateTime? CaptureDate,
    string? CameraMake,
    string? CameraModel,
    double? Latitude,
    double? Longitude,
    int? PixelWidth,
    int? PixelHeight,
    DateTimeOffset SourceLastWriteTimeUtc,
    DateTimeOffset UpdatedUtc);
