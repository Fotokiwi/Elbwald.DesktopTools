namespace Elbwald.DesktopTools.Contracts.Media.Naming;

public sealed record MediaNamingContext(
    DateTime CaptureDate,
    string OriginalStem,
    string Extension,
    string MediaType,
    string FolderName,
    string? CameraMake,
    string? CameraModel,
    string? Title,
    string? Description,
    int? Width,
    int? Height,
    int Sequence);
