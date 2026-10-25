namespace Elbwald.DesktopTools.Contracts.Media.Editing;

/// <summary>
/// Persists only non-destructive edit recipes. Implementations must not modify the
/// referenced media file. A save with a stale Revision must fail.
/// </summary>
public interface IImageEditDocumentStore
{
    Task<ImageEditDocument?> GetAsync(
        string mediaItemId,
        CancellationToken cancellationToken = default);

    Task<ImageEditDocument> SaveAsync(
        ImageEditDocument document,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string mediaItemId,
        CancellationToken cancellationToken = default);
}
