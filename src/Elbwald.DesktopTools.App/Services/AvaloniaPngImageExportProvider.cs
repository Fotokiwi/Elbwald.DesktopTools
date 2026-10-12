using Avalonia;
using Avalonia.Media.Imaging;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Exporting;

namespace Elbwald.DesktopTools.App.Services;

/// <summary>
/// Host-owned PNG encoder. JPEG and PNG are the two intentional base image
/// formats and therefore do not depend on an optional format extension.
/// Final-path commit and overwrite protection remain outside the provider.
/// </summary>
public sealed class AvaloniaPngImageExportProvider : IImageExportProvider
{
    public const string PngFormatId = "image/png";

    public string ProviderId => "elbwald.core.image-export.png";

    public string FormatId => PngFormatId;

    public string DisplayName => "PNG";

    public string FileExtension => ".png";

    public string MimeType => "image/png";

    public int Priority => 0;

    public bool SupportsResize => true;

    public bool SupportsQuality => false;

    public ImageExportSupport CheckSupport(ImageExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MediaType != MediaFileType.Image)
        {
            return ImageExportSupport.Unsupported(
                "PNG-Ausgabe ist nur für Bilder möglich.");
        }

        if (IsRawExtension(Path.GetExtension(request.SourcePath)))
        {
            return ImageExportSupport.Unsupported(
                "RAW-Konvertierung ist noch nicht freigegeben. Originalformat verwenden.");
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
                support.Reason ?? "PNG-Export wird für diese Quelle nicht unterstützt.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using var source = new FileStream(
            request.SourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.SequentialScan | FileOptions.Asynchronous);

        var maxEdge = Math.Max(0, request.MaxEdge ?? 0);
        Bitmap bitmap;

        if (maxEdge > 0
            && request.PixelWidth is > 0
            && request.PixelHeight is > 0
            && Math.Max(request.PixelWidth.Value, request.PixelHeight.Value) > maxEdge)
        {
            bitmap = request.PixelWidth.Value >= request.PixelHeight.Value
                ? Bitmap.DecodeToWidth(source, maxEdge, BitmapInterpolationMode.HighQuality)
                : Bitmap.DecodeToHeight(source, maxEdge, BitmapInterpolationMode.HighQuality);
        }
        else
        {
            bitmap = new Bitmap(source);

            if (maxEdge > 0
                && Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height) > maxEdge)
            {
                var scale = maxEdge
                    / (double)Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
                var size = new PixelSize(
                    Math.Max(1, (int)Math.Round(bitmap.PixelSize.Width * scale)),
                    Math.Max(1, (int)Math.Round(bitmap.PixelSize.Height * scale)));
                var scaled = bitmap.CreateScaledBitmap(
                    size,
                    BitmapInterpolationMode.HighQuality);
                bitmap.Dispose();
                bitmap = scaled;
            }
        }

        using (bitmap)
        {
            bitmap.Save(destination, new PngBitmapEncoderOptions());

            await destination.FlushAsync(cancellationToken);

            if (destination is FileStream fileStream)
            {
                fileStream.Flush(flushToDisk: true);
            }
        }
    }

    private static bool IsRawExtension(string extension)
    {
        return extension.Equals(".cr2", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cr3", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".nef", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".arw", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".dng", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".orf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".rw2", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".raf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pef", StringComparison.OrdinalIgnoreCase);
    }
}
