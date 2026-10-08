namespace Elbwald.DesktopTools.Contracts.Media;

public interface IRawPreviewExtractor
{
    Task<RawPreviewResult> ExtractAsync(
        MediaFile file,
        CancellationToken cancellationToken = default);
}
