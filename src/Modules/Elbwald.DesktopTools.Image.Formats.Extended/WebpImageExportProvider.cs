using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Exporting;
using ImageMagick;

namespace Elbwald.DesktopTools.Image.Formats.Extended;

/// <summary>
/// Encodes supported raster images as WebP through Magick.NET/ImageMagick.
/// The provider only writes to the host-owned destination stream; final-path
/// commit and overwrite protection remain under host control.
/// </summary>
public sealed class WebpImageExportProvider : IImageExportProvider
{
    private static readonly HashSet<string> SupportedSourceExtensions = new(
        [".jpg", ".jpeg", ".png", ".webp"],
        StringComparer.OrdinalIgnoreCase);

    public string ProviderId => "webp.magicknet";

    public string FormatId => "image/webp";

    public string DisplayName => "WebP";

    public string FileExtension => ".webp";

    public string MimeType => "image/webp";

    public int Priority => 100;

    public bool SupportsResize => true;

    public bool SupportsQuality => true;

    public ImageExportSupport CheckSupport(ImageExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MediaType != MediaFileType.Image)
        {
            return ImageExportSupport.Unsupported(
                "WebP-Ausgabe ist nur für Bilder möglich.");
        }

        var extension = Path.GetExtension(request.SourcePath);
        if (!SupportedSourceExtensions.Contains(extension))
        {
            return ImageExportSupport.Unsupported(
                "WebP-Export unterstützt aktuell JPEG, PNG und WebP als Quelle.");
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
                support.Reason ?? "WebP-Export wird für diese Quelle nicht unterstützt.");
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

        image.Quality = (uint)Math.Clamp(request.Quality ?? 90, 1, 100);
        image.Write(destination, MagickFormat.WebP);

        await destination.FlushAsync(cancellationToken);

        if (destination is FileStream fileStream)
        {
            fileStream.Flush(flushToDisk: true);
        }
    }
}
