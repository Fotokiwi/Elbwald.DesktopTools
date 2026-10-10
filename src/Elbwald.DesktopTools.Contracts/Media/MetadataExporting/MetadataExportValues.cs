using Elbwald.DesktopTools.Contracts.Media.MetadataEditing;

namespace Elbwald.DesktopTools.Contracts.Media.MetadataExporting;

public sealed record MetadataExportValues(
    bool CaptureDateOverridden,
    DateTime? CaptureDate,
    bool TitleOverridden,
    string? Title,
    bool DescriptionOverridden,
    string? Description,
    MetadataGpsMode GpsMode,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<string>? Persons = null,
    IReadOnlyList<string>? Places = null,
    IReadOnlyList<string>? Events = null,
    int Rating = 0,
    string? CameraMake = null,
    string? CameraModel = null)
{
    public bool HasAnyOverride =>
        CaptureDateOverridden
        || TitleOverridden
        || DescriptionOverridden
        || GpsMode != MetadataGpsMode.Inherit;

    public bool HasAnyExportData =>
        HasAnyOverride
        || Rating > 0
        || HasValues(Tags)
        || HasValues(Persons)
        || HasValues(Places)
        || HasValues(Events)
        || !string.IsNullOrWhiteSpace(CameraMake)
        || !string.IsNullOrWhiteSpace(CameraModel);

    private static bool HasValues(IReadOnlyList<string>? values) =>
        values is { Count: > 0 } && values.Any(value => !string.IsNullOrWhiteSpace(value));
}
