namespace Elbwald.DesktopTools.Contracts.Media.MetadataEditing;

public sealed record MediaMetadataSnapshot(
    string MediaItemId,
    bool Exists,
    bool CaptureDateOverridden,
    DateTime? CaptureDate,
    bool TitleOverridden,
    string? Title,
    bool DescriptionOverridden,
    string? Description,
    MetadataGpsMode GpsMode,
    double? Latitude,
    double? Longitude,
    DateTimeOffset? UpdatedUtc);
