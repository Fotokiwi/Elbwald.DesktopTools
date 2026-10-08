using System.Globalization;
using Elbwald.DesktopTools.Contracts.Media.Dates;
using Elbwald.DesktopTools.Contracts.Media.Sorting;

namespace Elbwald.DesktopTools.PhotoSort.ViewModels;

public sealed class PhotoSortPlanItemViewModel
{
    public PhotoSortPlanItemViewModel(
        MediaSortPlanItem item,
        bool hasConflict)
    {
        ArgumentNullException.ThrowIfNull(item);

        FileName =
            item.File.FileName;

        SourcePath =
            item.File.FullPath;

        DestinationPath =
            item.DestinationPath;

        RelativeDirectory =
            item.RelativeDirectory;

        CaptureDate =
            item.CapturedAt.ToString(
                "dd.MM.yyyy HH:mm:ss",
                CultureInfo.CurrentCulture);

        DateConfidence =
            FormatConfidence(
                item.DateResolution.Confidence);

        DateSource =
            FormatSource(
                item.DateResolution.PrimarySource);

        DateExplanation =
            item.DateResolution.Explanation;

        DateEvidenceSummary =
            BuildEvidenceSummary(
                item.DateResolution);

        DateDeviationSummary =
            BuildDeviationSummary(
                item.DateResolution);

        Size =
            FormatBytes(
                item.File.Length);

        HasConflict =
            hasConflict;

        NeedsDateReview =
            !hasConflict
            && item.DateResolution.NeedsReview;

        IsReady =
            !hasConflict
            && !NeedsDateReview;

        Status =
            hasConflict
                ? "Konflikt"
                : NeedsDateReview
                    ? "Datum prüfen"
                    : "Bereit";
    }

    public string FileName { get; }

    public string SourcePath { get; }

    public string DestinationPath { get; }

    public string RelativeDirectory { get; }

    public string CaptureDate { get; }

    public string DateConfidence { get; }

    public string DateSource { get; }

    public string DateExplanation { get; }

    public string DateEvidenceSummary { get; }

    public string DateDeviationSummary { get; }

    public string Size { get; }

    public bool HasConflict { get; }

    public bool NeedsDateReview { get; }

    public bool IsReady { get; }

    public string Status { get; }

    private static string BuildEvidenceSummary(
        MediaDateResolution resolution)
    {
        return string.Join(
            " · ",
            resolution.Candidates.Select(
                candidate =>
                    $"{FormatSource(candidate.Source)} "
                    + $"{FormatCandidateValue(candidate)}"));
    }

    private static string BuildDeviationSummary(
        MediaDateResolution resolution)
    {
        var reviewComparisons =
            resolution.Comparisons
                .Where(comparison =>
                    (int)comparison.Deviation
                    >= (int)MediaDateDeviationLevel.Minor)
                .ToArray();

        if (reviewComparisons.Length == 0)
        {
            return "Keine relevante Zeitabweichung";
        }

        return string.Join(
            " · ",
            reviewComparisons.Select(
                comparison =>
                    $"{FormatSource(comparison.ComparedSource)} "
                    + $"Δ {FormatDifference(comparison.Difference)} "
                    + $"({FormatDeviation(comparison.Deviation)})"));
    }

    private static string FormatCandidateValue(
        MediaDateCandidate candidate)
    {
        var value =
            candidate.Precision == MediaDatePrecision.DateOnly
                ? candidate.Value.ToString(
                    "dd.MM.yyyy",
                    CultureInfo.CurrentCulture)
                : candidate.Value.ToString(
                    "dd.MM.yyyy HH:mm:ss",
                    CultureInfo.CurrentCulture);

        return candidate.Basis
            is MediaDateTimeBasis.Utc
            or MediaDateTimeBasis.FileSystemUtc
                ? value + " UTC"
                : value;
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

    private static string FormatSource(
        MediaDateSource? source)
    {
        return source switch
        {
            MediaDateSource.ExifDateTimeOriginal =>
                "EXIF Original",

            MediaDateSource.ExifLegacyCapturedAt =>
                "EXIF Aufnahmezeit",

            MediaDateSource.ExifDateTimeDigitized =>
                "EXIF Digitalisiert",

            MediaDateSource.ExifDateTimeModified =>
                "EXIF geändert",

            MediaDateSource.FileName =>
                "Dateiname",

            MediaDateSource.GpsUtc =>
                "GPS",

            MediaDateSource.FileSystemCreationUtc =>
                "Datei erstellt",

            MediaDateSource.FileSystemLastWriteUtc =>
                "Datei geändert",

            _ =>
                "Unbekannt"
        };
    }

    private static string FormatDeviation(
        MediaDateDeviationLevel deviation)
    {
        return deviation switch
        {
            MediaDateDeviationLevel.Minor =>
                "gering",

            MediaDateDeviationLevel.Noticeable =>
                "auffällig",

            MediaDateDeviationLevel.Strong =>
                "stark",

            MediaDateDeviationLevel.Close =>
                "sehr nahe",

            _ =>
                "identisch"
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

    private static string FormatBytes(
        long bytes)
    {
        string[] units =
        {
            "B",
            "KB",
            "MB",
            "GB",
            "TB"
        };

        var value =
            Math.Max(
                0,
                (double)bytes);

        var unitIndex = 0;

        while (value >= 1024
               && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value:N0} {units[unitIndex]}"
            : $"{value:N1} {units[unitIndex]}";
    }
}
