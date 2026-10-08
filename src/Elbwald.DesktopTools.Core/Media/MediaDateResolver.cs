using System.Globalization;
using System.Text.RegularExpressions;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Dates;

namespace Elbwald.DesktopTools.Core.Media;

public sealed class MediaDateResolver
    : IMediaDateResolver
{
    private static readonly Regex FileNameDateRegex =
        new(
            @"(?<!\d)(?<year>(?:19|20)\d{2})[-_.]?(?<month>0[1-9]|1[0-2])[-_.]?(?<day>0[1-9]|[12]\d|3[01])(?:(?:[T _.\-]+)?(?<hour>[01]\d|2[0-3])[:._\-]?(?<minute>[0-5]\d)[:._\-]?(?<second>[0-5]\d))?(?!\d)",
            RegexOptions.Compiled
            | RegexOptions.CultureInvariant);

    private static readonly TimeSpan EquivalentTolerance =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan CloseTolerance =
        TimeSpan.FromMinutes(2);

    private static readonly TimeSpan MinorTolerance =
        TimeSpan.FromMinutes(5);

    private static readonly TimeSpan NoticeableTolerance =
        TimeSpan.FromHours(1);

    private static readonly TimeSpan MinimumPlausibleUtcOffset =
        TimeSpan.FromHours(-12);

    private static readonly TimeSpan MaximumPlausibleUtcOffset =
        TimeSpan.FromHours(14);

    public MediaDateResolution Resolve(
        MediaAnalyzedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var candidates =
            BuildCandidates(file);

        var primary =
            SelectPrimaryCandidate(candidates);

        if (primary is null)
        {
            return new MediaDateResolution(
                value: null,
                MediaDateConfidence.Low,
                primarySource: null,
                candidates,
                comparisons:
                    Array.Empty<MediaDateComparison>(),
                BuildUnresolvedExplanation(candidates));
        }

        var comparisons =
            BuildComparisons(
                primary,
                candidates);

        var score =
            CalculateEvidenceScore(
                primary,
                comparisons,
                candidates);

        var confidence =
            ScoreToConfidence(
                score);

        var explanation =
            BuildExplanation(
                primary,
                confidence,
                score,
                comparisons,
                candidates);

        return new MediaDateResolution(
            primary.Value,
            confidence,
            primary.Source,
            candidates,
            comparisons,
            explanation);
    }

    private static IReadOnlyList<MediaDateCandidate> BuildCandidates(
        MediaAnalyzedFile analyzedFile)
    {
        var candidates =
            new List<MediaDateCandidate>();

        var metadata =
            analyzedFile.ImageMetadata;

        if (metadata?.DateTimeOriginal is { } original)
        {
            candidates.Add(
                CaptureCandidate(
                    MediaDateSource.ExifDateTimeOriginal,
                    original,
                    "EXIF DateTimeOriginal"));
        }

        if (metadata?.DateTimeDigitized is { } digitized)
        {
            candidates.Add(
                CaptureCandidate(
                    MediaDateSource.ExifDateTimeDigitized,
                    digitized,
                    "EXIF DateTimeDigitized"));
        }

        if (metadata?.DateTimeModified is { } modified)
        {
            candidates.Add(
                CaptureCandidate(
                    MediaDateSource.ExifDateTimeModified,
                    modified,
                    "EXIF DateTime"));
        }

        if (metadata is not null
            && metadata.DateTimeOriginal is null
            && metadata.DateTimeDigitized is null
            && metadata.DateTimeModified is null
            && metadata.CapturedAt is { } legacyCapturedAt)
        {
            candidates.Add(
                CaptureCandidate(
                    MediaDateSource.ExifLegacyCapturedAt,
                    legacyCapturedAt,
                    "EXIF Aufnahmezeit"));
        }

        if (metadata?.GpsDateTimeUtc is { } gpsUtc)
        {
            candidates.Add(
                new MediaDateCandidate(
                    MediaDateSource.GpsUtc,
                    DateTime.SpecifyKind(
                        gpsUtc,
                        DateTimeKind.Utc),
                    MediaDateTimeBasis.Utc,
                    MediaDatePrecision.Second,
                    "GPS UTC",
                    IsCaptureEvidence: true));
        }

        var fileNameCandidate =
            TryParseFileNameDate(
                analyzedFile.File.FileName);

        if (fileNameCandidate is not null)
        {
            candidates.Add(
                fileNameCandidate);
        }

        candidates.Add(
            new MediaDateCandidate(
                MediaDateSource.FileSystemCreationUtc,
                analyzedFile.File.CreationTimeUtc.UtcDateTime,
                MediaDateTimeBasis.FileSystemUtc,
                MediaDatePrecision.Second,
                "Dateisystem erstellt (UTC)",
                IsCaptureEvidence: false));

        candidates.Add(
            new MediaDateCandidate(
                MediaDateSource.FileSystemLastWriteUtc,
                analyzedFile.File.LastWriteTimeUtc.UtcDateTime,
                MediaDateTimeBasis.FileSystemUtc,
                MediaDatePrecision.Second,
                "Dateisystem zuletzt geschrieben (UTC)",
                IsCaptureEvidence: false));

        return candidates;
    }

    private static MediaDateCandidate CaptureCandidate(
        MediaDateSource source,
        DateTime value,
        string label)
    {
        return new MediaDateCandidate(
            source,
            DateTime.SpecifyKind(
                value,
                DateTimeKind.Unspecified),
            MediaDateTimeBasis.LocalTimeZoneUnknown,
            MediaDatePrecision.Second,
            label,
            IsCaptureEvidence: true);
    }

    private static MediaDateCandidate? SelectPrimaryCandidate(
        IReadOnlyList<MediaDateCandidate> candidates)
    {
        var priority =
            new[]
            {
                MediaDateSource.ExifDateTimeOriginal,
                MediaDateSource.ExifLegacyCapturedAt,
                MediaDateSource.ExifDateTimeDigitized,
                MediaDateSource.FileName,
                MediaDateSource.ExifDateTimeModified
            };

        foreach (var source in priority)
        {
            var candidate =
                candidates.FirstOrDefault(value =>
                    value.Source == source);

            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static IReadOnlyList<MediaDateComparison> BuildComparisons(
        MediaDateCandidate primary,
        IReadOnlyList<MediaDateCandidate> candidates)
    {
        var comparisons =
            new List<MediaDateComparison>();

        foreach (var candidate in candidates)
        {
            if (candidate == primary
                || candidate.Basis
                    != MediaDateTimeBasis.LocalTimeZoneUnknown
                || !IsComparableCaptureEvidence(candidate))
            {
                continue;
            }

            var difference =
                primary.Precision == MediaDatePrecision.DateOnly
                || candidate.Precision == MediaDatePrecision.DateOnly
                    ? candidate.Value.Date
                      - primary.Value.Date
                    : candidate.Value
                      - primary.Value;

            var deviation =
                ClassifyLocalDeviation(
                    primary,
                    candidate,
                    difference);

            var independent =
                AreIndependentEvidence(
                    primary.Source,
                    candidate.Source);

            comparisons.Add(
                new MediaDateComparison(
                    primary.Source,
                    candidate.Source,
                    difference,
                    deviation,
                    independent,
                    InferredUtcOffset: null,
                    BuildLocalComparisonMessage(
                        primary,
                        candidate,
                        difference,
                        deviation,
                        independent)));
        }

        var gps =
            candidates.FirstOrDefault(candidate =>
                candidate.Source
                == MediaDateSource.GpsUtc);

        if (gps is not null)
        {
            var gpsComparison =
                BuildGpsComparison(
                    primary,
                    gps);

            if (gpsComparison is not null)
            {
                comparisons.Add(
                    gpsComparison);
            }
        }

        return comparisons;
    }

    private static bool IsComparableCaptureEvidence(
        MediaDateCandidate candidate)
    {
        return candidate.Source
            is MediaDateSource.ExifDateTimeOriginal
            or MediaDateSource.ExifLegacyCapturedAt
            or MediaDateSource.ExifDateTimeDigitized
            or MediaDateSource.FileName;
    }

    private static bool AreIndependentEvidence(
        MediaDateSource left,
        MediaDateSource right)
    {
        return GetEvidenceFamily(left)
               != GetEvidenceFamily(right);
    }

    private static int GetEvidenceFamily(
        MediaDateSource source)
    {
        return source switch
        {
            MediaDateSource.ExifDateTimeOriginal
                or MediaDateSource.ExifDateTimeDigitized
                or MediaDateSource.ExifLegacyCapturedAt =>
                1,

            MediaDateSource.FileName =>
                2,

            MediaDateSource.GpsUtc =>
                3,

            MediaDateSource.ExifDateTimeModified =>
                4,

            MediaDateSource.FileSystemCreationUtc
                or MediaDateSource.FileSystemLastWriteUtc =>
                5,

            _ =>
                0
        };
    }

    private static MediaDateDeviationLevel ClassifyLocalDeviation(
        MediaDateCandidate primary,
        MediaDateCandidate compared,
        TimeSpan difference)
    {
        if (primary.Precision == MediaDatePrecision.DateOnly
            || compared.Precision == MediaDatePrecision.DateOnly)
        {
            return primary.Value.Date
                   == compared.Value.Date
                ? MediaDateDeviationLevel.Equivalent
                : MediaDateDeviationLevel.Strong;
        }

        return ClassifyTimedDeviation(
            difference.Duration());
    }

    private static MediaDateDeviationLevel ClassifyTimedDeviation(
        TimeSpan difference)
    {
        if (difference <= EquivalentTolerance)
        {
            return MediaDateDeviationLevel.Equivalent;
        }

        if (difference <= CloseTolerance)
        {
            return MediaDateDeviationLevel.Close;
        }

        if (difference <= MinorTolerance)
        {
            return MediaDateDeviationLevel.Minor;
        }

        if (difference <= NoticeableTolerance)
        {
            return MediaDateDeviationLevel.Noticeable;
        }

        return MediaDateDeviationLevel.Strong;
    }

    private static MediaDateComparison? BuildGpsComparison(
        MediaDateCandidate primary,
        MediaDateCandidate gpsCandidate)
    {
        if (primary.Basis
                != MediaDateTimeBasis.LocalTimeZoneUnknown
            || gpsCandidate.Basis
                != MediaDateTimeBasis.Utc
            || primary.Precision
                == MediaDatePrecision.DateOnly)
        {
            return null;
        }

        var rawOffset =
            primary.Value
            - gpsCandidate.Value;

        var quarterHourMinutes =
            Math.Round(
                rawOffset.TotalMinutes / 15d,
                MidpointRounding.AwayFromZero)
            * 15d;

        var inferredOffset =
            TimeSpan.FromMinutes(
                quarterHourMinutes);

        if (inferredOffset < MinimumPlausibleUtcOffset
            || inferredOffset > MaximumPlausibleUtcOffset)
        {
            return new MediaDateComparison(
                primary.Source,
                MediaDateSource.GpsUtc,
                rawOffset,
                MediaDateDeviationLevel.Strong,
                IsIndependentEvidence: true,
                InferredUtcOffset: null,
                $"GPS UTC ({FormatCandidate(gpsCandidate)}) lässt sich nicht mit einem plausiblen "
                + $"Zeitzonenversatz zu {primary.Label} ({FormatCandidate(primary)}) erklären.");
        }

        var normalizedGpsLocal =
            gpsCandidate.Value
            + inferredOffset;

        var residualDifference =
            normalizedGpsLocal
            - primary.Value;

        var deviation =
            ClassifyTimedDeviation(
                residualDifference.Duration());

        return new MediaDateComparison(
            primary.Source,
            MediaDateSource.GpsUtc,
            residualDifference,
            deviation,
            IsIndependentEvidence: true,
            inferredOffset,
            $"GPS UTC passt mit Zeitzonenversatz {FormatOffset(inferredOffset)}; "
            + $"Restabweichung {FormatDifference(residualDifference)} ({FormatDeviation(deviation)}).");
    }

    private static int CalculateEvidenceScore(
        MediaDateCandidate primary,
        IReadOnlyList<MediaDateComparison> comparisons,
        IReadOnlyList<MediaDateCandidate> candidates)
    {
        var score =
            GetBaseScore(
                primary);

        foreach (var comparison in comparisons)
        {
            if (comparison.ComparedSource
                == MediaDateSource.GpsUtc)
            {
                score +=
                    comparison.Deviation switch
                    {
                        MediaDateDeviationLevel.Equivalent => 25,
                        MediaDateDeviationLevel.Close => 20,
                        MediaDateDeviationLevel.Minor => 10,
                        MediaDateDeviationLevel.Noticeable => -20,
                        MediaDateDeviationLevel.Strong => -30,
                        _ => 0
                    };

                continue;
            }

            if (comparison.IsIndependentEvidence)
            {
                var adjustment =
                    comparison.Deviation switch
                    {
                        MediaDateDeviationLevel.Equivalent => 20,
                        MediaDateDeviationLevel.Close => 15,
                        MediaDateDeviationLevel.Minor => 0,
                        MediaDateDeviationLevel.Noticeable => -20,
                        MediaDateDeviationLevel.Strong => -25,
                        _ => 0
                    };

                var comparedCandidate =
                    candidates.FirstOrDefault(candidate =>
                        candidate.Source
                        == comparison.ComparedSource);

                if (adjustment > 0
                    && comparedCandidate?.Precision
                        == MediaDatePrecision.DateOnly)
                {
                    adjustment =
                        Math.Min(
                            adjustment,
                            10);
                }

                score +=
                    adjustment;

                continue;
            }

            score +=
                comparison.Deviation switch
                {
                    MediaDateDeviationLevel.Equivalent => 5,
                    MediaDateDeviationLevel.Close => 3,
                    MediaDateDeviationLevel.Minor => 0,
                    MediaDateDeviationLevel.Noticeable => -10,
                    MediaDateDeviationLevel.Strong => -20,
                    _ => 0
                };
        }

        return Math.Clamp(
            score,
            0,
            100);
    }

    private static int GetBaseScore(
        MediaDateCandidate primary)
    {
        return primary.Source switch
        {
            MediaDateSource.ExifDateTimeOriginal =>
                75,

            MediaDateSource.ExifLegacyCapturedAt =>
                70,

            MediaDateSource.ExifDateTimeDigitized =>
                60,

            MediaDateSource.FileName =>
                primary.Precision == MediaDatePrecision.DateOnly
                    ? 50
                    : 55,

            MediaDateSource.ExifDateTimeModified =>
                30,

            _ =>
                0
        };
    }

    private static MediaDateConfidence ScoreToConfidence(
        int score)
    {
        if (score >= 90)
        {
            return MediaDateConfidence.VeryHigh;
        }

        if (score >= 70)
        {
            return MediaDateConfidence.High;
        }

        if (score >= 50)
        {
            return MediaDateConfidence.Medium;
        }

        return MediaDateConfidence.Low;
    }

    private static string BuildExplanation(
        MediaDateCandidate primary,
        MediaDateConfidence confidence,
        int score,
        IReadOnlyList<MediaDateComparison> comparisons,
        IReadOnlyList<MediaDateCandidate> allCandidates)
    {
        var parts =
            new List<string>
            {
                $"Gewählt: {primary.Label} ({FormatCandidate(primary)}).",
                $"Vertrauen: {FormatConfidence(confidence)} ({score}/100)."
            };

        foreach (var comparison in comparisons)
        {
            parts.Add(
                comparison.Message);
        }

        if (comparisons.Any(comparison =>
                !comparison.IsIndependentEvidence
                && (int)comparison.Deviation
                    <= (int)MediaDateDeviationLevel.Close))
        {
            parts.Add(
                "Übereinstimmende EXIF-Aufnahmefelder stammen typischerweise aus derselben Kamerauhr "
                + "und zählen deshalb nur als schwache Zusatz-Evidenz.");
        }

        if (comparisons.Any(comparison =>
                (int)comparison.Deviation
                >= (int)MediaDateDeviationLevel.Minor))
        {
            parts.Add(
                "Die Abweichung bleibt sichtbar zur Prüfung markiert.");
        }

        if (allCandidates.Any(candidate =>
                candidate.Source
                is MediaDateSource.FileSystemCreationUtc
                or MediaDateSource.FileSystemLastWriteUtc))
        {
            parts.Add(
                "Dateisystemzeiten werden nur angezeigt und nicht allein als Aufnahmezeit verwendet.");
        }

        return string.Join(
            " ",
            parts);
    }

    private static string BuildLocalComparisonMessage(
        MediaDateCandidate primary,
        MediaDateCandidate compared,
        TimeSpan difference,
        MediaDateDeviationLevel deviation,
        bool independent)
    {
        var relationship =
            independent
                ? "unabhängige Quelle"
                : "gleiche EXIF-Kamerauhr";

        return $"{compared.Label} ({FormatCandidate(compared)}): "
               + $"{FormatDeviation(deviation)}, Δ {FormatDifference(difference)} "
               + $"gegen {primary.Label} ({relationship}).";
    }

    private static string BuildUnresolvedExplanation(
        IReadOnlyList<MediaDateCandidate> candidates)
    {
        if (candidates.Any(candidate =>
                candidate.Source == MediaDateSource.GpsUtc))
        {
            return "GPS liefert einen UTC-Zeitpunkt, aber ohne verlässliche lokale Zeitzone wird daraus keine lokale Aufnahmezeit abgeleitet. "
                   + "Dateisystemzeiten werden nicht als automatischer Ersatz verwendet.";
        }

        return "Keine ausreichend belastbare Aufnahmezeit gefunden. "
               + "Dateisystemzeiten werden bewusst nicht als automatischer Ersatz verwendet.";
    }

    private static MediaDateCandidate? TryParseFileNameDate(
        string fileName)
    {
        var stem =
            Path.GetFileNameWithoutExtension(
                fileName);

        var match =
            FileNameDateRegex.Match(
                stem);

        if (!match.Success)
        {
            return null;
        }

        if (!TryGetInt(
                match,
                "year",
                out var year)
            || !TryGetInt(
                match,
                "month",
                out var month)
            || !TryGetInt(
                match,
                "day",
                out var day))
        {
            return null;
        }

        var hasTime =
            match.Groups["hour"].Success
            && match.Groups["minute"].Success
            && match.Groups["second"].Success;

        var hour = 0;
        var minute = 0;
        var second = 0;

        if (hasTime
            && (!TryGetInt(
                    match,
                    "hour",
                    out hour)
                || !TryGetInt(
                    match,
                    "minute",
                    out minute)
                || !TryGetInt(
                    match,
                    "second",
                    out second)))
        {
            return null;
        }

        try
        {
            var value =
                new DateTime(
                    year,
                    month,
                    day,
                    hour,
                    minute,
                    second,
                    DateTimeKind.Unspecified);

            return new MediaDateCandidate(
                MediaDateSource.FileName,
                value,
                MediaDateTimeBasis.LocalTimeZoneUnknown,
                hasTime
                    ? MediaDatePrecision.Second
                    : MediaDatePrecision.DateOnly,
                hasTime
                    ? "Dateiname (Datum und Uhrzeit)"
                    : "Dateiname (nur Datum)",
                IsCaptureEvidence: true);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool TryGetInt(
        Match match,
        string groupName,
        out int value)
    {
        return int.TryParse(
            match.Groups[groupName].Value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static string FormatCandidate(
        MediaDateCandidate candidate)
    {
        return candidate.Precision
            == MediaDatePrecision.DateOnly
                ? candidate.Value.ToString(
                    "dd.MM.yyyy",
                    CultureInfo.InvariantCulture)
                : candidate.Value.ToString(
                    "dd.MM.yyyy HH:mm:ss",
                    CultureInfo.InvariantCulture);
    }

    private static string FormatConfidence(
        MediaDateConfidence confidence)
    {
        return confidence switch
        {
            MediaDateConfidence.VeryHigh =>
                "Sehr hoch",

            MediaDateConfidence.High =>
                "Hoch",

            MediaDateConfidence.Medium =>
                "Mittel",

            MediaDateConfidence.Low =>
                "Niedrig",

            _ =>
                "Ungeklärt"
        };
    }

    private static string FormatDeviation(
        MediaDateDeviationLevel deviation)
    {
        return deviation switch
        {
            MediaDateDeviationLevel.Equivalent =>
                "praktisch identisch",

            MediaDateDeviationLevel.Close =>
                "sehr nahe",

            MediaDateDeviationLevel.Minor =>
                "geringe Abweichung",

            MediaDateDeviationLevel.Noticeable =>
                "auffällige Abweichung",

            MediaDateDeviationLevel.Strong =>
                "starke Abweichung",

            _ =>
                "unbekannte Abweichung"
        };
    }

    private static string FormatOffset(
        TimeSpan offset)
    {
        var sign =
            offset < TimeSpan.Zero
                ? "-"
                : "+";

        var absolute =
            offset.Duration();

        return $"{sign}{(int)absolute.TotalHours:00}:{absolute.Minutes:00}";
    }

    private static string FormatDifference(
        TimeSpan difference)
    {
        var sign =
            difference < TimeSpan.Zero
                ? "-"
                : "+";

        var absolute =
            difference.Duration();

        if (absolute.TotalDays >= 1)
        {
            return $"{sign}{(int)absolute.TotalDays}d "
                   + $"{absolute.Hours:00}:{absolute.Minutes:00}:{absolute.Seconds:00}";
        }

        return $"{sign}{(int)absolute.TotalHours:00}:{absolute.Minutes:00}:{absolute.Seconds:00}";
    }
}
