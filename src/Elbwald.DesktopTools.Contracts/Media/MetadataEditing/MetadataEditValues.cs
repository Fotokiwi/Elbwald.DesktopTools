namespace Elbwald.DesktopTools.Contracts.Media.MetadataEditing;

public sealed record MetadataEditValues(
    DateTime? CaptureDate,
    string? Title,
    string? Description,
    double? Latitude,
    double? Longitude,
    bool RemoveGps);
