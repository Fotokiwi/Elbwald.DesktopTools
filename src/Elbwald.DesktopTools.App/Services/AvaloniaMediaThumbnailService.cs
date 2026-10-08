using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.App.Services;

public sealed class AvaloniaMediaThumbnailService
    : IMediaThumbnailService
{
    private const int ThumbnailCacheVersion = 2;

    private readonly MediaThumbnailOptions _options;
    private readonly IRawPreviewExtractor _rawPreviewExtractor;

    public AvaloniaMediaThumbnailService(
        MediaThumbnailOptions options,
        IRawPreviewExtractor rawPreviewExtractor)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(rawPreviewExtractor);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CacheDirectory);

        _options = options with
        {
            CacheDirectory = Path.GetFullPath(options.CacheDirectory)
        };

        _rawPreviewExtractor = rawPreviewExtractor;
    }

    public async Task<MediaThumbnailResult> GetThumbnailAsync(
        MediaFile file,
        int? width = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.MediaType != MediaFileType.Image)
        {
            return new MediaThumbnailResult(
                MediaThumbnailState.NotImage);
        }

        var targetWidth =
            Math.Clamp(
                width ?? _options.DefaultWidth,
                64,
                _options.MaximumWidth);

        var cachePath =
            GetCachePath(
                file,
                targetWidth);

        var cached =
            await Task.Run(
                () => TryReadCache(
                    cachePath,
                    cancellationToken),
                cancellationToken);

        if (cached is not null)
        {
            return cached;
        }

        if (IsRawExtension(
                file.Extension))
        {
            var rawPreview =
                await _rawPreviewExtractor.ExtractAsync(
                    file,
                    cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (rawPreview.IsSuccessful
                && rawPreview.EncodedImage is not null)
            {
                var embeddedResult =
                    await Task.Run(
                        () => CreateThumbnailFromEncodedImage(
                            rawPreview.EncodedImage,
                            cachePath,
                            targetWidth,
                            cancellationToken),
                        cancellationToken);

                if (embeddedResult.IsSuccessful)
                {
                    return embeddedResult;
                }
            }
        }

        return await Task.Run(
            () => CreateThumbnailFromFile(
                file,
                cachePath,
                targetWidth,
                cancellationToken),
            cancellationToken);
    }

    public Task<MediaCacheStatistics> GetCacheStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (!Directory.Exists(
                            _options.CacheDirectory))
                    {
                        return new MediaCacheStatistics(
                            _options.CacheDirectory,
                            EntryCount: 0,
                            SizeBytes: 0);
                    }

                    long entryCount = 0;
                    long sizeBytes = 0;

                    foreach (var path in Directory.EnumerateFiles(
                                 _options.CacheDirectory,
                                 "*",
                                 SearchOption.TopDirectoryOnly))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!path.EndsWith(
                                ".png",
                                StringComparison.OrdinalIgnoreCase)
                            && !path.EndsWith(
                                ".partial",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var info =
                            new FileInfo(path);

                        sizeBytes +=
                            info.Exists
                                ? info.Length
                                : 0;

                        if (path.EndsWith(
                                ".png",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            entryCount++;
                        }
                    }

                    return new MediaCacheStatistics(
                        _options.CacheDirectory,
                        entryCount,
                        sizeBytes);
                }
                catch (Exception exception) when (
                    exception is IOException
                        or UnauthorizedAccessException
                        or NotSupportedException)
                {
                    return new MediaCacheStatistics(
                        _options.CacheDirectory,
                        EntryCount: 0,
                        SizeBytes: 0,
                        IsAvailable: false,
                        Message:
                            exception.Message);
                }
            },
            cancellationToken);
    }

    public Task ClearCacheAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(
                        _options.CacheDirectory))
                {
                    return;
                }

                foreach (var path in Directory.EnumerateFiles(
                             _options.CacheDirectory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!path.EndsWith(
                            ".png",
                            StringComparison.OrdinalIgnoreCase)
                        && !path.EndsWith(
                            ".partial",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        File.Delete(path);
                    }
                    catch (FileNotFoundException)
                    {
                    }
                    catch (DirectoryNotFoundException)
                    {
                    }
                }
            },
            cancellationToken);
    }

    private static MediaThumbnailResult? TryReadCache(
        string cachePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!File.Exists(cachePath))
            {
                return null;
            }

            var cached =
                File.ReadAllBytes(cachePath);

            return cached.Length > 0
                ? new MediaThumbnailResult(
                    MediaThumbnailState.Success,
                    cached,
                    "image/png",
                    isFromCache: true)
                : null;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static MediaThumbnailResult CreateThumbnailFromFile(
        MediaFile file,
        string cachePath,
        int targetWidth,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var stream =
                new FileStream(
                    file.FullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.SequentialScan);

            return DecodeAndStore(
                stream,
                cachePath,
                targetWidth,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FileNotFoundException exception)
        {
            return Failure(
                MediaThumbnailState.FileNotFound,
                exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            return Failure(
                MediaThumbnailState.FileNotFound,
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure(
                MediaThumbnailState.AccessDenied,
                exception);
        }
        catch (IOException exception)
        {
            return Failure(
                MediaThumbnailState.IoError,
                exception);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or NotSupportedException)
        {
            return Failure(
                MediaThumbnailState.UnsupportedFormat,
                exception);
        }
    }

    private static MediaThumbnailResult CreateThumbnailFromEncodedImage(
        byte[] encodedImage,
        string cachePath,
        int targetWidth,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var stream =
                new MemoryStream(
                    encodedImage,
                    writable: false);

            return DecodeAndStore(
                stream,
                cachePath,
                targetWidth,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or InvalidOperationException
                or NotSupportedException)
        {
            return Failure(
                MediaThumbnailState.UnsupportedFormat,
                exception);
        }
    }

    private static MediaThumbnailResult DecodeAndStore(
        Stream source,
        string cachePath,
        int targetWidth,
        CancellationToken cancellationToken)
    {
        using var bitmap =
            Bitmap.DecodeToWidth(
                source,
                targetWidth,
                BitmapInterpolationMode.HighQuality);

        cancellationToken.ThrowIfCancellationRequested();

        using var encoded =
            new MemoryStream();

        bitmap.Save(
            encoded,
            PngBitmapEncoderOptions.Default);

        var bytes =
            encoded.ToArray();

        TryStoreCache(
            cachePath,
            bytes,
            cancellationToken);

        return new MediaThumbnailResult(
            MediaThumbnailState.Success,
            bytes,
            "image/png");
    }

    private string GetCachePath(
        MediaFile file,
        int targetWidth)
    {
        var fingerprint =
            string.Join(
                "|",
                ThumbnailCacheVersion,
                Path.GetFullPath(file.FullPath),
                file.Length,
                file.LastWriteTimeUtc.UtcDateTime.Ticks,
                targetWidth);

        var hash =
            Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        fingerprint)));

        return Path.Combine(
            _options.CacheDirectory,
            hash + ".png");
    }

    private static bool IsRawExtension(
        string extension)
    {
        return extension.Equals(".arw", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".dng", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".cr2", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".cr3", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".nef", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".raf", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".orf", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".rw2", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".pef", StringComparison.OrdinalIgnoreCase);
    }

    private static MediaThumbnailResult Failure(
        MediaThumbnailState state,
        Exception exception)
    {
        return new MediaThumbnailResult(
            state,
            errorMessage:
                exception.Message);
    }

    private static void TryStoreCache(
        string cachePath,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        string? temporaryPath = null;

        try
        {
            var directory =
                Path.GetDirectoryName(cachePath)
                ?? throw new InvalidOperationException(
                    "Der Thumbnail-Cache hat kein gültiges Verzeichnis.");

            Directory.CreateDirectory(directory);

            temporaryPath =
                cachePath
                + "."
                + Guid.NewGuid().ToString("N")
                + ".partial";

            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       64 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();

            File.Move(
                temporaryPath,
                cachePath,
                overwrite: true);

            temporaryPath = null;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            // A cache write must never break a successful in-memory preview.
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Expendable cache partial; conservative cleanup later.
                }
            }
        }
    }
}
