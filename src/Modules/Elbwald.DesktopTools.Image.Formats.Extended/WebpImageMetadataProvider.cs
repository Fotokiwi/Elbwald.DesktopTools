using System.Globalization;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Metadata;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.WebP;

namespace Elbwald.DesktopTools.Image.Formats.Extended;

/// <summary>
/// Reads WebP dimensions and embedded EXIF/GPS metadata without depending on
/// the host Core assembly. The provider is discovered through Module API v2.
/// </summary>
public sealed class WebpImageMetadataProvider : IImageMetadataProvider
{
    private const int ReadBufferSize = 64 * 1024;

    public string ProviderId => "webp.metadata-extractor";

    public int Priority => 100;

    public bool CanRead(MediaFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return file.MediaType == MediaFileType.Image
               && string.Equals(
                   file.Extension,
                   ".webp",
                   StringComparison.OrdinalIgnoreCase);
    }

    public Task<ImageMetadataReadResult> ReadAsync(
        MediaFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.MediaType != MediaFileType.Image)
        {
            return Task.FromResult(
                new ImageMetadataReadResult(
                    ImageMetadataReadState.NotImage,
                    errorMessage: "Die Datei wurde nicht als Bild erkannt."));
        }

        if (!CanRead(file))
        {
            return Task.FromResult(
                new ImageMetadataReadResult(
                    ImageMetadataReadState.UnsupportedFormat,
                    errorMessage: "Der WebP-Metadatenreader unterstützt nur .webp-Dateien."));
        }

        return Task.Run(
            () => ReadCore(file, cancellationToken),
            cancellationToken);
    }

    private static ImageMetadataReadResult ReadCore(
        MediaFile file,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var stream = new FileStream(
                file.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                ReadBufferSize,
                FileOptions.SequentialScan);

            cancellationToken.ThrowIfCancellationRequested();

            var directories = MetadataExtractor.ImageMetadataReader
                .ReadMetadata(stream)
                .ToArray();

            cancellationToken.ThrowIfCancellationRequested();

            var warnings = directories
                .Where(directory => directory.HasError)
                .SelectMany(
                    directory => directory.Errors.Select(
                        error => $"{directory.Name}: {error}"))
                .ToArray();

            var webp = directories
                .OfType<WebPDirectory>()
                .FirstOrDefault();

            if (webp is null)
            {
                return new ImageMetadataReadResult(
                    ImageMetadataReadState.UnsupportedFormat,
                    errorMessage: "Die Datei enthält keine lesbaren WebP-Metadaten.");
            }

            var ifd0 = directories
                .OfType<ExifIfd0Directory>()
                .FirstOrDefault();

            var subIfd = directories
                .OfType<ExifSubIfdDirectory>()
                .FirstOrDefault();

            var gps = directories
                .OfType<GpsDirectory>()
                .FirstOrDefault();

            var width = ReadPositiveInt(
                webp,
                WebPDirectory.TagImageWidth);

            var height = ReadPositiveInt(
                webp,
                WebPDirectory.TagImageHeight);

            var dateTimeOriginal = ReadDateTime(
                subIfd,
                ExifDirectoryBase.TagDateTimeOriginal);

            var dateTimeDigitized = ReadDateTime(
                subIfd,
                ExifDirectoryBase.TagDateTimeDigitized);

            var dateTimeModified = ReadDateTime(
                ifd0,
                ExifDirectoryBase.TagDateTime);

            var capturedAt = dateTimeOriginal
                             ?? dateTimeDigitized
                             ?? dateTimeModified;

            var gpsDateTimeUtc = ReadGpsDateTimeUtc(gps);

            var cameraMake = ReadText(
                ifd0,
                ExifDirectoryBase.TagMake);

            var cameraModel = ReadText(
                ifd0,
                ExifDirectoryBase.TagModel);

            var lensModel = ReadText(
                subIfd,
                ExifDirectoryBase.TagLensModel);

            var orientation = ReadOrientation(ifd0);

            var hasExif = directories.Any(
                directory => directory is ExifDirectoryBase);

            var hasGps = gpsDateTimeUtc.HasValue
                         || (gps is not null
                             && (gps.ContainsTag(GpsDirectory.TagLatitude)
                                 || gps.ContainsTag(GpsDirectory.TagLongitude)));

            double? latitude = null;
            double? longitude = null;

            if (gps?.TryGetGeoLocation(out var location) == true)
            {
                latitude = location.Latitude;
                longitude = location.Longitude;
                hasGps = true;
            }

            var metadata = new ImageMetadata(
                width,
                height,
                capturedAt,
                cameraMake,
                cameraModel,
                lensModel,
                orientation,
                hasExif,
                hasGps,
                latitude,
                longitude)
            {
                DateTimeOriginal = dateTimeOriginal,
                DateTimeDigitized = dateTimeDigitized,
                DateTimeModified = dateTimeModified,
                GpsDateTimeUtc = gpsDateTimeUtc
            };

            return new ImageMetadataReadResult(
                ImageMetadataReadState.Success,
                metadata,
                warnings);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FileNotFoundException exception)
        {
            return Failure(ImageMetadataReadState.FileNotFound, exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            return Failure(ImageMetadataReadState.FileNotFound, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure(ImageMetadataReadState.AccessDenied, exception);
        }
        catch (MetadataExtractor.ImageProcessingException exception)
        {
            return Failure(ImageMetadataReadState.UnsupportedFormat, exception);
        }
        catch (IOException exception)
        {
            return Failure(ImageMetadataReadState.IoError, exception);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or InvalidOperationException)
        {
            return Failure(ImageMetadataReadState.Failed, exception);
        }
    }

    private static int? ReadOrientation(ExifIfd0Directory? ifd0)
    {
        var orientation = ReadInt32(
            ifd0,
            ExifDirectoryBase.TagOrientation);

        return orientation is >= 1 and <= 8
            ? orientation
            : null;
    }

    private static int? ReadPositiveInt(
        MetadataExtractor.Directory? directory,
        int tag)
    {
        var value = ReadInt32(directory, tag);

        return value is > 0
            ? value
            : null;
    }

    private static int? ReadInt32(
        MetadataExtractor.Directory? directory,
        int tag)
    {
        if (directory is null || !directory.ContainsTag(tag))
        {
            return null;
        }

        var value = directory.GetObject(tag);

        if (value is null)
        {
            return null;
        }

        try
        {
            if (value is IConvertible convertible)
            {
                return convertible.ToInt32(CultureInfo.InvariantCulture);
            }
        }
        catch (Exception exception) when (
            exception is FormatException
                or InvalidCastException
                or OverflowException)
        {
            // Fall through to textual parsing.
        }

        var text = NormalizeText(value.ToString())
                   ?? NormalizeText(directory.GetDescription(tag));

        return int.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    private static DateTime? ReadDateTime(
        MetadataExtractor.Directory? directory,
        int tag)
    {
        if (directory is null || !directory.ContainsTag(tag))
        {
            return null;
        }

        var value = directory.GetObject(tag);

        if (value is DateTime dateTime)
        {
            return DateTime.SpecifyKind(
                dateTime,
                DateTimeKind.Unspecified);
        }

        var rawText = NormalizeText(value?.ToString());

        if (TryParseExifDateTime(rawText, out var parsed))
        {
            return parsed;
        }

        var description = NormalizeText(directory.GetDescription(tag));

        return TryParseExifDateTime(description, out parsed)
            ? parsed
            : null;
    }

    private static bool TryParseExifDateTime(
        string? value,
        out DateTime parsed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = default;
            return false;
        }

        string[] formats =
        [
            "yyyy:MM:dd HH:mm:ss",
            "yyyy:MM:dd HH:mm:ss.F",
            "yyyy:MM:dd HH:mm:ss.FF",
            "yyyy:MM:dd HH:mm:ss.FFF",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss"
        ];

        return DateTime.TryParseExact(
            value,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out parsed);
    }

    private static string? ReadText(
        MetadataExtractor.Directory? directory,
        int tag)
    {
        if (directory is null || !directory.ContainsTag(tag))
        {
            return null;
        }

        var raw = NormalizeText(directory.GetObject(tag)?.ToString());

        return !string.IsNullOrWhiteSpace(raw)
            ? raw
            : NormalizeText(directory.GetDescription(tag));
    }

    private static DateTime? ReadGpsDateTimeUtc(GpsDirectory? gps)
    {
        const int tagGpsTimeStamp = 0x0007;
        const int tagGpsDateStamp = 0x001D;

        if (gps is null
            || !gps.ContainsTag(tagGpsTimeStamp)
            || !gps.ContainsTag(tagGpsDateStamp))
        {
            return null;
        }

        var dateText = NormalizeText(gps.GetObject(tagGpsDateStamp)?.ToString())
                       ?? NormalizeText(gps.GetDescription(tagGpsDateStamp));

        var timeText = NormalizeText(gps.GetDescription(tagGpsTimeStamp))
                       ?? NormalizeText(gps.GetObject(tagGpsTimeStamp)?.ToString());

        if (!TryParseGpsDate(dateText, out var date)
            || !TryParseGpsTime(timeText, out var time))
        {
            return null;
        }

        try
        {
            return new DateTime(
                date.Year,
                date.Month,
                date.Day,
                time.Hour,
                time.Minute,
                time.Second,
                DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool TryParseGpsDate(
        string? value,
        out DateTime date)
    {
        date = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] formats =
        [
            "yyyy:MM:dd",
            "yyyy-MM-dd",
            "yyyy/MM/dd"
        ];

        return DateTime.TryParseExact(
            value.Trim(),
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out date);
    }

    private static bool TryParseGpsTime(
        string? value,
        out (int Hour, int Minute, int Second) time)
    {
        time = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var numbers = System.Text.RegularExpressions.Regex
            .Matches(
                value,
                @"\d+(?:\.\d+)?",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            .Cast<System.Text.RegularExpressions.Match>()
            .Select(match => match.Value)
            .ToArray();

        if (numbers.Length < 3
            || !double.TryParse(
                numbers[0],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var hour)
            || !double.TryParse(
                numbers[1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var minute)
            || !double.TryParse(
                numbers[2],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var second))
        {
            return false;
        }

        var roundedSecond = (int)Math.Round(
            second,
            MidpointRounding.AwayFromZero);

        if (hour is < 0 or >= 24
            || minute is < 0 or >= 60
            || roundedSecond is < 0 or >= 60)
        {
            return false;
        }

        time = ((int)hour, (int)minute, roundedSecond);
        return true;
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().TrimEnd('\0');
    }

    private static ImageMetadataReadResult Failure(
        ImageMetadataReadState state,
        Exception exception)
    {
        return new ImageMetadataReadResult(
            state,
            errorMessage: exception.Message);
    }
}
