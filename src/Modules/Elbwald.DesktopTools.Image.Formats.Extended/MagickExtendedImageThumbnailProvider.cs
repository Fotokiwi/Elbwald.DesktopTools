using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Thumbnailing;
using ImageMagick;

namespace Elbwald.DesktopTools.Image.Formats.Extended;

/// <summary>
/// Creates PNG thumbnails for the vertically supported extended formats WebP
/// and TIFF. HEIF/AVIF/JPEG 2000 remain detection-only until explicitly promoted.
/// </summary>
public sealed class MagickExtendedImageThumbnailProvider : IImageThumbnailProvider
{
    private static readonly HashSet<string> SupportedExtensions = new(
        [".webp", ".tif", ".tiff"],
        StringComparer.OrdinalIgnoreCase);

    public string ProviderId => "extended.magicknet.webp-tiff";

    public int Priority => 100;

    public bool CanCreate(MediaFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return file.MediaType == MediaFileType.Image
               && SupportedExtensions.Contains(file.Extension);
    }

    public async Task<MediaThumbnailResult> CreateAsync(
        MediaFile file,
        int targetWidth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!CanCreate(file))
        {
            return new MediaThumbnailResult(MediaThumbnailState.UnsupportedFormat);
        }

        if (targetWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetWidth));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var image = new MagickImage(file.FullPath);

            cancellationToken.ThrowIfCancellationRequested();

            if (image.Width > (uint)targetWidth)
            {
                var scale = targetWidth / (double)image.Width;
                var width = Math.Max(1u, (uint)targetWidth);
                var height = Math.Max(1u, (uint)Math.Round(image.Height * scale));

                image.Resize(width, height);
            }

            cancellationToken.ThrowIfCancellationRequested();

            await using var encoded = new MemoryStream();
            image.Write(encoded, MagickFormat.Png);
            await encoded.FlushAsync(cancellationToken);

            var bytes = encoded.ToArray();

            return bytes.Length > 0
                ? new MediaThumbnailResult(
                    MediaThumbnailState.Success,
                    bytes,
                    "image/png")
                : new MediaThumbnailResult(
                    MediaThumbnailState.Failed,
                    errorMessage: "Magick.NET hat keine Thumbnail-Daten erzeugt.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FileNotFoundException exception)
        {
            return Failure(MediaThumbnailState.FileNotFound, exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            return Failure(MediaThumbnailState.FileNotFound, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure(MediaThumbnailState.AccessDenied, exception);
        }
        catch (IOException exception)
        {
            return Failure(MediaThumbnailState.IoError, exception);
        }
        catch (Exception exception)
        {
            return Failure(MediaThumbnailState.Failed, exception);
        }
    }

    private static MediaThumbnailResult Failure(
        MediaThumbnailState state,
        Exception exception) =>
        new(state, errorMessage: exception.Message);
}
