namespace Elbwald.DesktopTools.Contracts.Media.Editing;

/// <summary>
/// Builds non-destructive editor previews from an indexed media item. Implementations
/// resolve a readable source, decode a bounded raster and apply the requested edit stack.
/// Source files must never be modified.
/// </summary>
public interface IImageEditPreviewService
{
    Task<ImageEditPreviewResult> RenderAsync(
        ImageEditPreviewRequest request,
        CancellationToken cancellationToken = default);
}
