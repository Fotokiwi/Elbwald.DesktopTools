namespace Elbwald.DesktopTools.Contracts.Media;

public interface IImageMetadataReader
{
    Task<ImageMetadataReadResult> ReadAsync(
        MediaFile file,
        CancellationToken cancellationToken = default);
}
