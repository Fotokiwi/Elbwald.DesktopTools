using Avalonia.Media.Imaging;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Thumbnailing;

namespace Elbwald.DesktopTools.App.Services;

/// <summary>
/// Built-in thumbnail provider for Elbwald's base image formats JPEG and PNG.
/// </summary>
public sealed class AvaloniaBaseImageThumbnailProvider : IImageThumbnailProvider
{
    private static readonly HashSet<string> SupportedExtensions = new(
        [".jpg", ".jpeg", ".png"],
        StringComparer.OrdinalIgnoreCase);

    public string ProviderId => "core.avalonia.jpeg-png";

    public int Priority => 0;

    public bool CanCreate(MediaFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return file.MediaType == MediaFileType.Image
               && SupportedExtensions.Contains(file.Extension);
    }

    public Task<MediaThumbnailResult> CreateAsync(
        MediaFile file,
        int targetWidth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!CanCreate(file))
        {
            return Task.FromResult(
                new MediaThumbnailResult(MediaThumbnailState.UnsupportedFormat));
        }

        if (targetWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetWidth));
        }

        return Task.Run(
            () => CreateCore(file, targetWidth, cancellationToken),
            cancellationToken);
    }

    private static MediaThumbnailResult CreateCore(
        MediaFile file,
        int targetWidth,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var source = new FileStream(
                file.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);

            using var bitmap = Bitmap.DecodeToWidth(
                source,
                targetWidth,
                BitmapInterpolationMode.HighQuality);

            cancellationToken.ThrowIfCancellationRequested();

            using var encoded = new MemoryStream();
            bitmap.Save(encoded, PngBitmapEncoderOptions.Default);

            return new MediaThumbnailResult(
                MediaThumbnailState.Success,
                encoded.ToArray(),
                "image/png");
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
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or NotSupportedException)
        {
            return Failure(MediaThumbnailState.UnsupportedFormat, exception);
        }
    }

    private static MediaThumbnailResult Failure(
        MediaThumbnailState state,
        Exception exception) =>
        new(state, errorMessage: exception.Message);
}
