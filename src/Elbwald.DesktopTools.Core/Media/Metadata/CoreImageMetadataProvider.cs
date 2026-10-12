using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Formats;
using Elbwald.DesktopTools.Contracts.Media.Metadata;
using Elbwald.DesktopTools.Core.Media.Formats;

namespace Elbwald.DesktopTools.Core.Media.Metadata;

/// <summary>
/// Host-owned compatibility metadata provider for the image formats that still
/// belong to <see cref="CoreImageFormatProvider"/>. As formats move to dedicated
/// extensions they automatically leave this provider's responsibility too.
/// </summary>
public sealed class CoreImageMetadataProvider : IImageMetadataProvider
{
    private readonly IImageMetadataReader _reader;
    private readonly HashSet<string> _extensions;

    public CoreImageMetadataProvider(
        IImageMetadataReader reader,
        CoreImageFormatProvider formatProvider)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(formatProvider);

        _reader = reader;
        _extensions = formatProvider.FileExtensions
            .Select(NormalizeExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public string ProviderId => "core.metadata-extractor";

    public int Priority => 0;

    public bool CanRead(MediaFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return file.MediaType == MediaFileType.Image
            && _extensions.Contains(NormalizeExtension(file.Extension));
    }

    public Task<ImageMetadataReadResult> ReadAsync(
        MediaFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        return _reader.ReadAsync(file, cancellationToken);
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var normalized = extension.Trim();

        if (!normalized.StartsWith(".", StringComparison.Ordinal))
        {
            normalized = "." + normalized;
        }

        return normalized.ToLowerInvariant();
    }
}
