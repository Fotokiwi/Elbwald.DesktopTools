using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Dates;

namespace Elbwald.DesktopTools.Core.Media;

public sealed class MediaDateContextAnalyzer
    : IMediaDateContextAnalyzer
{
    private const int MinimumComparableFiles = 5;
    private const int HighStrengthMinimumFiles = 10;

    private const double MinimumCoverageRatio = 0.70;
    private const double HighStrengthCoverageRatio = 0.85;

    private static readonly TimeSpan MinimumReportedOffset =
        TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ClusterTolerance =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan OutlierTolerance =
        TimeSpan.FromMinutes(5);

    public IReadOnlyList<MediaDateContextHint> Analyze(
        IEnumerable<MediaDateContextEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var samples =
            new List<ContextSample>();

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sample =
                TryCreateSample(
                    entry);

            if (sample is not null)
            {
                samples.Add(
                    sample);
            }
        }

        var hints =
            new List<MediaDateContextHint>();

        foreach (var group in samples.GroupBy(sample =>
                     sample.GroupKey))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (group.Count()
                < MinimumComparableFiles)
            {
                continue;
            }

            var groupSamples =
                group.ToArray();

            var dominantCluster =
                BuildClusters(
                    groupSamples)
                    .OrderByDescending(cluster =>
                        cluster.Count)
                    .ThenBy(cluster =>
                        cluster
                            .Select(sample =>
                                sample.Difference.Duration())
                            .Min())
                    .First();

            var matchingCount =
                dominantCluster.Count;

            var comparableCount =
                groupSamples.Length;

            var coverage =
                matchingCount
                / (double)comparableCount;

            if (matchingCount
                    < MinimumComparableFiles
                || coverage
                    < MinimumCoverageRatio)
            {
                continue;
            }

            var dominantDifference =
                MedianDifference(
                    dominantCluster
                        .Select(sample =>
                            sample.Difference)
                        .ToArray());

            if (dominantDifference.Duration()
                < MinimumReportedOffset)
            {
                continue;
            }

            var outlierCount =
                groupSamples.Count(sample =>
                    (sample.Difference
                     - dominantDifference).Duration()
                    > OutlierTolerance);

            var strength =
                matchingCount
                    >= HighStrengthMinimumFiles
                && coverage
                    >= HighStrengthCoverageRatio
                    ? MediaDateContextStrength.High
                    : MediaDateContextStrength.Medium;

            var first =
                groupSamples[0];

            hints.Add(
                new MediaDateContextHint(
                    MediaDateContextHintKind.RepeatedFileNameOffset,
                    strength,
                    first.DirectoryPath,
                    first.CameraLabel,
                    first.ExifCandidate.Source,
                    MediaDateSource.FileName,
                    dominantDifference,
                    matchingCount,
                    comparableCount,
                    outlierCount,
                    coverage,
                    BuildMessage(
                        first,
                        dominantDifference,
                        matchingCount,
                        comparableCount,
                        outlierCount,
                        coverage,
                        strength)));
        }

        return hints
            .OrderByDescending(hint =>
                hint.Strength)
            .ThenByDescending(hint =>
                hint.MatchingFileCount)
            .ThenBy(hint =>
                hint.DirectoryPath,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static ContextSample? TryCreateSample(
        MediaDateContextEntry entry)
    {
        var resolution =
            entry.Resolution;

        var exif =
            resolution.Candidates.FirstOrDefault(candidate =>
                candidate.Source
                == MediaDateSource.ExifDateTimeOriginal)
            ?? resolution.Candidates.FirstOrDefault(candidate =>
                candidate.Source
                == MediaDateSource.ExifLegacyCapturedAt)
            ?? resolution.Candidates.FirstOrDefault(candidate =>
                candidate.Source
                == MediaDateSource.ExifDateTimeDigitized);

        var fileName =
            resolution.Candidates.FirstOrDefault(candidate =>
                candidate.Source
                    == MediaDateSource.FileName
                && candidate.Precision
                    == MediaDatePrecision.Second);

        if (exif is null
            || fileName is null
            || exif.Basis
                != MediaDateTimeBasis.LocalTimeZoneUnknown
            || fileName.Basis
                != MediaDateTimeBasis.LocalTimeZoneUnknown)
        {
            return null;
        }

        var directoryPath =
            entry.File.File.DirectoryPath;

        if (string.IsNullOrWhiteSpace(
                directoryPath))
        {
            return null;
        }

        var cameraLabel =
            BuildCameraLabel(
                entry.File.ImageMetadata);

        if (string.IsNullOrWhiteSpace(
                cameraLabel))
        {
            return null;
        }

        var groupKey =
            new ContextGroupKey(
                NormalizeGroupText(
                    directoryPath),
                NormalizeGroupText(
                    cameraLabel),
                exif.Source);

        return new ContextSample(
            groupKey,
            directoryPath,
            cameraLabel,
            exif,
            fileName,
            fileName.Value
            - exif.Value);
    }

    private static string? BuildCameraLabel(
        ImageMetadata? metadata)
    {
        var make =
            metadata?.CameraMake?.Trim();

        var model =
            metadata?.CameraModel?.Trim();

        if (string.IsNullOrWhiteSpace(make))
        {
            return string.IsNullOrWhiteSpace(model)
                ? null
                : model;
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            return make;
        }

        if (model.StartsWith(
                make,
                StringComparison.OrdinalIgnoreCase))
        {
            return model;
        }

        return $"{make} {model}";
    }

    private static string NormalizeGroupText(
        string value)
    {
        var normalized =
            value.Trim();

        return OperatingSystem.IsWindows()
            ? normalized.ToUpperInvariant()
            : normalized;
    }

    private static IReadOnlyList<List<ContextSample>> BuildClusters(
        IReadOnlyList<ContextSample> samples)
    {
        var clusters =
            new List<List<ContextSample>>();

        foreach (var sample in samples
                     .OrderBy(value =>
                         value.Difference))
        {
            if (clusters.Count == 0)
            {
                clusters.Add(
                    new List<ContextSample>
                    {
                        sample
                    });

                continue;
            }

            var currentCluster =
                clusters[^1];

            var currentMedian =
                MedianOfSortedCluster(
                    currentCluster);

            if ((sample.Difference
                 - currentMedian).Duration()
                <= ClusterTolerance)
            {
                currentCluster.Add(
                    sample);

                continue;
            }

            clusters.Add(
                new List<ContextSample>
                {
                    sample
                });
        }

        return clusters;
    }

    private static TimeSpan MedianOfSortedCluster(
        IReadOnlyList<ContextSample> cluster)
    {
        var middle =
            cluster.Count / 2;

        if (cluster.Count % 2 == 1)
        {
            return cluster[middle].Difference;
        }

        var lower =
            cluster[middle - 1].Difference.Ticks;

        var upper =
            cluster[middle].Difference.Ticks;

        return TimeSpan.FromTicks(
            lower
            + (upper - lower) / 2);
    }

    private static TimeSpan MedianDifference(
        IReadOnlyList<TimeSpan> values)
    {
        var ordered =
            values
                .Select(value =>
                    value.Ticks)
                .OrderBy(value =>
                    value)
                .ToArray();

        if (ordered.Length == 0)
        {
            return TimeSpan.Zero;
        }

        var middle =
            ordered.Length / 2;

        if (ordered.Length % 2 == 1)
        {
            return TimeSpan.FromTicks(
                ordered[middle]);
        }

        var lower =
            ordered[middle - 1];

        var upper =
            ordered[middle];

        return TimeSpan.FromTicks(
            lower
            + (upper - lower) / 2);
    }

    private static string BuildMessage(
        ContextSample sample,
        TimeSpan dominantDifference,
        int matchingCount,
        int comparableCount,
        int outlierCount,
        double coverage,
        MediaDateContextStrength strength)
    {
        var strengthText =
            strength == MediaDateContextStrength.High
                ? "starker"
                : "plausibler";

        var message =
            $"{strengthText} Serienhinweis: "
            + $"{matchingCount:N0} von {comparableCount:N0} vergleichbaren Dateien "
            + $"({coverage:P0}) zeigen denselben systematischen Versatz "
            + $"Dateiname − {FormatSource(sample.ExifCandidate.Source)} "
            + $"≈ {FormatDifference(dominantDifference)}. "
            + $"Kamera: {sample.CameraLabel}. "
            + "Das kann auf eine verstellte Kamerauhr, einen Export-/Umbenennungsprozess "
            + "oder eine andere systematische Zeitquelle hindeuten. "
            + "Elbwald Digital korrigiert deshalb nichts automatisch.";

        if (outlierCount > 0)
        {
            message +=
                $" Zusätzlich liegen {outlierCount:N0} Datei(en) mehr als "
                + $"{FormatDifference(OutlierTolerance)} vom dominanten Versatz entfernt.";
        }

        return message;
    }

    private static string FormatSource(
        MediaDateSource source)
    {
        return source switch
        {
            MediaDateSource.ExifDateTimeOriginal =>
                "EXIF Original",

            MediaDateSource.ExifLegacyCapturedAt =>
                "EXIF Aufnahmezeit",

            MediaDateSource.ExifDateTimeDigitized =>
                "EXIF Digitalisiert",

            _ =>
                source.ToString()
        };
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

    private sealed record ContextGroupKey(
        string DirectoryPath,
        string CameraLabel,
        MediaDateSource ExifSource);

    private sealed record ContextSample(
        ContextGroupKey GroupKey,
        string DirectoryPath,
        string CameraLabel,
        MediaDateCandidate ExifCandidate,
        MediaDateCandidate FileNameCandidate,
        TimeSpan Difference);
}
