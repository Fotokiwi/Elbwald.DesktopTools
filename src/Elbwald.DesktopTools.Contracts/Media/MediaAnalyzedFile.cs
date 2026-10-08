namespace Elbwald.DesktopTools.Contracts.Media;

public sealed record MediaAnalyzedFile(
    MediaFile File,
    ImageMetadata? ImageMetadata);
