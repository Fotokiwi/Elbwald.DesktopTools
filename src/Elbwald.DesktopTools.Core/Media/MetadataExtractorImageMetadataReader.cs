using System.Globalization;
using Elbwald.DesktopTools.Contracts.Media;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.WebP;

namespace Elbwald.DesktopTools.Core.Media;

public sealed class MetadataExtractorImageMetadataReader
    : IImageMetadataReader
{
    private const int ReadBufferSize = 64 * 1024;
    private const int TagSonyRawImageSize = 0x7038;
    private const int TagSonyCropSize = 0x74C8;

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
                    errorMessage:
                        "Die Datei wurde nicht als Bild erkannt."));
        }

        return Task.Run(
            () => ReadCore(
                file,
                cancellationToken),
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

            var directories =
                MetadataExtractor.ImageMetadataReader
                    .ReadMetadata(stream)
                    .ToArray();

            cancellationToken.ThrowIfCancellationRequested();

            var warnings = directories
                .Where(directory => directory.HasError)
                .SelectMany(
                    directory => directory.Errors.Select(
                        error => $"{directory.Name}: {error}"))
                .ToArray();

            var ifd0 =
                directories
                    .OfType<ExifIfd0Directory>()
                    .FirstOrDefault();

            var subIfd =
                directories
                    .OfType<ExifSubIfdDirectory>()
                    .FirstOrDefault();

            var gps =
                directories
                    .OfType<GpsDirectory>()
                    .FirstOrDefault();

            var sonyRawDimensions =
                ReadSonyRawDimensions(
                    directories);

            var pixelWidth =
                sonyRawDimensions.Width
                ?? ReadPixelWidth(
                    directories,
                    ifd0,
                    subIfd);

            var pixelHeight =
                sonyRawDimensions.Height
                ?? ReadPixelHeight(
                    directories,
                    ifd0,
                    subIfd);

            var dateTimeOriginal =
                ReadDateTime(
                    subIfd,
                    ExifDirectoryBase.TagDateTimeOriginal);

            var dateTimeDigitized =
                ReadDateTime(
                    subIfd,
                    ExifDirectoryBase.TagDateTimeDigitized);

            var dateTimeModified =
                ReadDateTime(
                    ifd0,
                    ExifDirectoryBase.TagDateTime);

            var capturedAt =
                dateTimeOriginal
                ?? dateTimeDigitized
                ?? dateTimeModified;

            var gpsDateTimeUtc =
                ReadGpsDateTimeUtc(
                    gps);

            var cameraMake =
                ReadText(
                    ifd0,
                    ExifDirectoryBase.TagMake);

            var cameraModel =
                ReadText(
                    ifd0,
                    ExifDirectoryBase.TagModel);

            var lensModel =
                ReadText(
                    subIfd,
                    ExifDirectoryBase.TagLensModel);

            var orientation =
                ReadOrientation(ifd0);

            var hasExif =
                directories.Any(
                    directory =>
                        directory is ExifDirectoryBase);

            var hasGps =
                gpsDateTimeUtc.HasValue
                || (gps is not null
                    && (gps.ContainsTag(GpsDirectory.TagLatitude)
                        || gps.ContainsTag(GpsDirectory.TagLongitude)));

            double? latitude = null;
            double? longitude = null;

            if (gps?.TryGetGeoLocation(
                    out var location)
                == true)
            {
                latitude = location.Latitude;
                longitude = location.Longitude;
                hasGps = true;
            }

            var metadata =
                new ImageMetadata(
                    pixelWidth,
                    pixelHeight,
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
                    DateTimeOriginal =
                        dateTimeOriginal,
                    DateTimeDigitized =
                        dateTimeDigitized,
                    DateTimeModified =
                        dateTimeModified,
                    GpsDateTimeUtc =
                        gpsDateTimeUtc
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
            return Failure(
                ImageMetadataReadState.FileNotFound,
                exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            return Failure(
                ImageMetadataReadState.FileNotFound,
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure(
                ImageMetadataReadState.AccessDenied,
                exception);
        }
        catch (MetadataExtractor.ImageProcessingException exception)
        {
            return Failure(
                ImageMetadataReadState.UnsupportedFormat,
                exception);
        }
        catch (IOException exception)
        {
            return Failure(
                ImageMetadataReadState.IoError,
                exception);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or InvalidOperationException)
        {
            return Failure(
                ImageMetadataReadState.Failed,
                exception);
        }
    }

    private static (int? Width, int? Height) ReadSonyRawDimensions(
        IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        foreach (var directory in directories.OfType<ExifSubIfdDirectory>())
        {
            if (TryReadDimensionPair(
                    directory,
                    TagSonyRawImageSize,
                    out var width,
                    out var height))
            {
                return (width, height);
            }
        }

        foreach (var directory in directories.OfType<ExifSubIfdDirectory>())
        {
            if (TryReadDimensionPair(
                    directory,
                    TagSonyCropSize,
                    out var width,
                    out var height))
            {
                return (width, height);
            }
        }

        return (null, null);
    }

    private static bool TryReadDimensionPair(
        MetadataExtractor.Directory directory,
        int tag,
        out int width,
        out int height)
    {
        width = 0;
        height = 0;

        if (!directory.ContainsTag(tag))
        {
            return false;
        }

        var value =
            directory.GetObject(tag);

        if (value is Array values
            && values.Length >= 2
            && TryConvertPositiveInt(
                values.GetValue(0),
                out width)
            && TryConvertPositiveInt(
                values.GetValue(1),
                out height))
        {
            return true;
        }

        var text =
            NormalizeText(
                directory.GetDescription(tag))
            ?? NormalizeText(
                value?.ToString());

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts =
            text.Split(
                new[]
                {
                    ' ',
                    'x',
                    'X',
                    ',',
                    ';',
                    '[',
                    ']',
                    '(',
                    ')'
                },
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);

        var numbers =
            parts
                .Select(part =>
                    int.TryParse(
                        part,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsed)
                        ? parsed
                        : 0)
                .Where(number => number > 0)
                .Take(2)
                .ToArray();

        if (numbers.Length < 2)
        {
            return false;
        }

        width = numbers[0];
        height = numbers[1];
        return true;
    }

    private static bool TryConvertPositiveInt(
        object? value,
        out int result)
    {
        result = 0;

        if (value is null)
        {
            return false;
        }

        try
        {
            if (value is IConvertible convertible)
            {
                var converted =
                    convertible.ToInt32(
                        CultureInfo.InvariantCulture);

                if (converted > 0)
                {
                    result = converted;
                    return true;
                }
            }
        }
        catch (Exception exception) when (
            exception is FormatException
                or InvalidCastException
                or OverflowException)
        {
        }

        return int.TryParse(
                   value.ToString(),
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out var parsed)
               && parsed > 0
               && Assign(
                   parsed,
                   out result);
    }

    private static bool Assign(
        int value,
        out int result)
    {
        result = value;
        return true;
    }

    private static int? ReadPixelWidth(
        IReadOnlyList<MetadataExtractor.Directory> directories,
        ExifIfd0Directory? ifd0,
        ExifSubIfdDirectory? subIfd)
    {
        var jpeg =
            directories
                .OfType<JpegDirectory>()
                .FirstOrDefault();

        var png =
            directories
                .OfType<PngDirectory>()
                .FirstOrDefault(
                    directory =>
                        directory.ContainsTag(
                            PngDirectory.TagImageWidth));

        var webP =
            directories
                .OfType<WebPDirectory>()
                .FirstOrDefault();

        return ReadPositiveInt(
                   jpeg,
                   JpegDirectory.TagImageWidth)
               ?? ReadPositiveInt(
                   png,
                   PngDirectory.TagImageWidth)
               ?? ReadPositiveInt(
                   webP,
                   WebPDirectory.TagImageWidth)
               ?? ReadPositiveInt(
                   subIfd,
                   ExifDirectoryBase.TagExifImageWidth)
               ?? ReadPositiveInt(
                   ifd0,
                   ExifDirectoryBase.TagImageWidth);
    }

    private static int? ReadPixelHeight(
        IReadOnlyList<MetadataExtractor.Directory> directories,
        ExifIfd0Directory? ifd0,
        ExifSubIfdDirectory? subIfd)
    {
        var jpeg =
            directories
                .OfType<JpegDirectory>()
                .FirstOrDefault();

        var png =
            directories
                .OfType<PngDirectory>()
                .FirstOrDefault(
                    directory =>
                        directory.ContainsTag(
                            PngDirectory.TagImageHeight));

        var webP =
            directories
                .OfType<WebPDirectory>()
                .FirstOrDefault();

        return ReadPositiveInt(
                   jpeg,
                   JpegDirectory.TagImageHeight)
               ?? ReadPositiveInt(
                   png,
                   PngDirectory.TagImageHeight)
               ?? ReadPositiveInt(
                   webP,
                   WebPDirectory.TagImageHeight)
               ?? ReadPositiveInt(
                   subIfd,
                   ExifDirectoryBase.TagExifImageHeight)
               ?? ReadPositiveInt(
                   ifd0,
                   ExifDirectoryBase.TagImageHeight);
    }

    private static DateTime? ReadGpsDateTimeUtc(
        GpsDirectory? gps)
    {
        const int TagGpsTimeStamp = 0x0007;
        const int TagGpsDateStamp = 0x001D;

        if (gps is null
            || !gps.ContainsTag(TagGpsTimeStamp)
            || !gps.ContainsTag(TagGpsDateStamp))
        {
            return null;
        }

        var dateText =
            NormalizeText(
                gps.GetObject(TagGpsDateStamp)?.ToString())
            ?? NormalizeText(
                gps.GetDescription(TagGpsDateStamp));

        var timeText =
            NormalizeText(
                gps.GetDescription(TagGpsTimeStamp))
            ?? NormalizeText(
                gps.GetObject(TagGpsTimeStamp)?.ToString());

        if (!TryParseGpsDate(
                dateText,
                out var date)
            || !TryParseGpsTime(
                timeText,
                out var time))
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

        var formats =
            new[]
            {
                "yyyy:MM:dd",
                "yyyy-MM-dd",
                "yyyy/MM/dd"
            };

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

        var numbers =
            System.Text.RegularExpressions.Regex
                .Matches(
                    value,
                    @"\d+(?:\.\d+)?",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant)
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(match =>
                    match.Value)
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

        var roundedSecond =
            (int)Math.Round(
                second,
                MidpointRounding.AwayFromZero);

        if (hour is < 0 or >= 24
            || minute is < 0 or >= 60
            || roundedSecond is < 0 or >= 60)
        {
            return false;
        }

        time =
            (
                (int)hour,
                (int)minute,
                roundedSecond
            );

        return true;
    }

    private static int? ReadOrientation(
        ExifIfd0Directory? ifd0)
    {
        var orientation =
            ReadInt32(
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
        var value =
            ReadInt32(
                directory,
                tag);

        return value is > 0
            ? value
            : null;
    }

    private static int? ReadInt32(
        MetadataExtractor.Directory? directory,
        int tag)
    {
        if (directory is null
            || !directory.ContainsTag(tag))
        {
            return null;
        }

        var value =
            directory.GetObject(tag);

        if (value is null)
        {
            return null;
        }

        try
        {
            if (value is IConvertible convertible)
            {
                return convertible.ToInt32(
                    CultureInfo.InvariantCulture);
            }
        }
        catch (Exception exception) when (
            exception is FormatException
                or InvalidCastException
                or OverflowException)
        {
            // Fall through to the textual representation.
        }

        var text =
            NormalizeText(
                value.ToString())
            ?? NormalizeText(
                directory.GetDescription(tag));

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
        if (directory is null
            || !directory.ContainsTag(tag))
        {
            return null;
        }

        var value =
            directory.GetObject(tag);

        if (value is DateTime dateTime)
        {
            return DateTime.SpecifyKind(
                dateTime,
                DateTimeKind.Unspecified);
        }

        var rawText =
            NormalizeText(
                value?.ToString());

        if (TryParseExifDateTime(
                rawText,
                out var parsed))
        {
            return parsed;
        }

        var description =
            NormalizeText(
                directory.GetDescription(tag));

        return TryParseExifDateTime(
            description,
            out parsed)
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

        var formats =
            new[]
            {
                "yyyy:MM:dd HH:mm:ss",
                "yyyy:MM:dd HH:mm:ss.F",
                "yyyy:MM:dd HH:mm:ss.FF",
                "yyyy:MM:dd HH:mm:ss.FFF",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss"
            };

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
        if (directory is null
            || !directory.ContainsTag(tag))
        {
            return null;
        }

        var raw =
            NormalizeText(
                directory.GetObject(tag)?.ToString());

        if (!string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        return NormalizeText(
            directory.GetDescription(tag));
    }

    private static string? NormalizeText(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value
            .Trim()
            .TrimEnd('\0');
    }

    private static ImageMetadataReadResult Failure(
        ImageMetadataReadState state,
        Exception exception)
    {
        return new ImageMetadataReadResult(
            state,
            errorMessage:
                exception.Message);
    }
}
