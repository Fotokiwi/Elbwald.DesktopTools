using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Exporting;
using ImageMagick;

namespace Elbwald.DesktopTools.Image.Formats.Extended;

/// <summary>
/// Encodes supported raster images as TIFF through Magick.NET/ImageMagick.
/// The provider only writes to the host-owned destination stream; final-path
/// commit and overwrite protection remain under host control.
/// </summary>
public sealed class TiffImageExportProvider : IImageExportProvider
{
    private static readonly HashSet<string> SupportedSourceExtensions = new(
        [".jpg", ".jpeg", ".png", ".webp", ".tif", ".tiff"],
        StringComparer.OrdinalIgnoreCase);

    public string ProviderId => "tiff.magicknet";

    public string FormatId => "image/tiff";

    public string DisplayName => "TIFF";

    public string FileExtension => ".tiff";

    public string MimeType => "image/tiff";

    public int Priority => 100;

    public bool SupportsResize => true;

    public bool SupportsQuality => false;

    public ImageExportSupport CheckSupport(ImageExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MediaType != MediaFileType.Image)
        {
            return ImageExportSupport.Unsupported(
                "TIFF-Ausgabe ist nur für Bilder möglich.");
        }

        var extension = Path.GetExtension(request.SourcePath);
        if (!SupportedSourceExtensions.Contains(extension))
        {
            return ImageExportSupport.Unsupported(
                "TIFF-Export unterstützt aktuell JPEG, PNG, WebP und TIFF als Quelle.");
        }

        return ImageExportSupport.Supported;
    }

    public async Task ExportAsync(
        ImageExportRequest request,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(destination);

        var support = CheckSupport(request);
        if (!support.IsSupported)
        {
            throw new NotSupportedException(
                support.Reason ?? "TIFF-Export wird für diese Quelle nicht unterstützt.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var image = new MagickImage(request.SourcePath);

        cancellationToken.ThrowIfCancellationRequested();

        var maxEdge = Math.Max(0, request.MaxEdge ?? 0);
        var longestEdge = Math.Max(image.Width, image.Height);

        if (maxEdge > 0 && longestEdge > maxEdge)
        {
            var scale = maxEdge / (double)longestEdge;
            var width = Math.Max(1u, (uint)Math.Round(image.Width * scale));
            var height = Math.Max(1u, (uint)Math.Round(image.Height * scale));

            image.Resize(width, height);
        }

        cancellationToken.ThrowIfCancellationRequested();

        image.Write(destination, MagickFormat.Tiff);

        await destination.FlushAsync(cancellationToken);

        if (destination is FileStream fileStream)
        {
            fileStream.Flush(flushToDisk: true);
        }
    }
}
