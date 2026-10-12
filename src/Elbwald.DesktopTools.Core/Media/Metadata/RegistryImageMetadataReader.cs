using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Metadata;

namespace Elbwald.DesktopTools.Core.Media.Metadata;

/// <summary>
/// Compatibility facade for existing consumers. Analyzer, media index, rename
/// and metadata UI continue to depend on IImageMetadataReader while provider
/// selection is delegated to the metadata registry.
/// </summary>
public sealed class RegistryImageMetadataReader : IImageMetadataReader
{
    private readonly IImageMetadataRegistry _registry;

    public RegistryImageMetadataReader(IImageMetadataRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public async Task<ImageMetadataReadResult> ReadAsync(
        MediaFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.MediaType != MediaFileType.Image)
        {
            return new ImageMetadataReadResult(
                ImageMetadataReadState.NotImage,
                errorMessage: "Die Datei wurde nicht als Bild erkannt.");
        }

        var providers = _registry.FindReaders(file);

        if (providers.Count == 0)
        {
            return UnsupportedFormat();
        }

        List<string>? providerErrors = null;

        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await provider.ReadAsync(file, cancellationToken);

                if (result.State == ImageMetadataReadState.UnsupportedFormat)
                {
                    continue;
                }

                return result;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                providerErrors ??= [];
                providerErrors.Add($"{provider.ProviderId}: {exception.Message}");
            }
        }

        if (providerErrors is { Count: > 0 })
        {
            return new ImageMetadataReadResult(
                ImageMetadataReadState.Failed,
                errorMessage:
                    "Kein installierter Metadaten-Reader konnte die Datei verarbeiten. "
                    + string.Join(" | ", providerErrors));
        }

        return UnsupportedFormat();
    }

    private static ImageMetadataReadResult UnsupportedFormat()
    {
        return new ImageMetadataReadResult(
            ImageMetadataReadState.UnsupportedFormat,
            errorMessage:
                "Für dieses Bildformat ist kein aktiver Metadaten-Reader installiert.");
    }
}
