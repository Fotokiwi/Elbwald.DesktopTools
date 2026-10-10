namespace Elbwald.DesktopTools.Contracts.Media.MetadataEditing;

public sealed record MediaMetadataUpdate(
    string MediaItemId,
    bool CaptureDateOverridden,
    DateTime? CaptureDate,
    bool TitleOverridden,
    string? Title,
    bool DescriptionOverridden,
    string? Description,
    MetadataGpsMode GpsMode,
    double? Latitude,
    double? Longitude);
