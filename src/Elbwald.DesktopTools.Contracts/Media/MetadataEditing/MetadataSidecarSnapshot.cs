namespace Elbwald.DesktopTools.Contracts.Media.MetadataEditing;

public sealed record MetadataSidecarSnapshot(
    string SidecarPath,
    bool Exists,
    bool IsElbwaldManaged,
    DateTime? CaptureDate,
    string? Title,
    string? Description,
    double? Latitude,
    double? Longitude,
    bool RemoveGps,
    string? ErrorMessage = null);
