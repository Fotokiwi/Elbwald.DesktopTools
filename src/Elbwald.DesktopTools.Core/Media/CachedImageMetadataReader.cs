using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.Core.Media;

public sealed class CachedImageMetadataReader
    : IImageMetadataReader
{
    private readonly MetadataExtractorImageMetadataReader _inner;
    private readonly IMediaAnalysisCache _cache;

    public CachedImageMetadataReader(
        MetadataExtractorImageMetadataReader inner,
        IMediaAnalysisCache cache)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);

        _inner = inner;
        _cache = cache;
    }

    public async Task<ImageMetadataReadResult> ReadAsync(
        MediaFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var cached =
            await _cache.TryGetImageMetadataAsync(
                file,
                cancellationToken);

        if (cached is not null)
        {
            return cached;
        }

        var result =
            await _inner.ReadAsync(
                file,
                cancellationToken);

        if (result.IsSuccessful)
        {
            await _cache.StoreImageMetadataAsync(
                file,
                result,
                cancellationToken);
        }

        return result;
    }
}
