using Elbwald.DesktopTools.Contracts.Media.Decoding;

namespace Elbwald.DesktopTools.Contracts.Media.Editing;

/// <summary>
/// Renders an ordered non-destructive edit stack against an in-memory source image.
/// The renderer never writes back to the source file.
/// </summary>
public interface IImageEditRenderer
{
    ValueTask<DecodedImageFrame> RenderAsync(
        DecodedImageFrame source,
        IReadOnlyList<ImageEditOperation> operations,
        CancellationToken cancellationToken = default);
}
