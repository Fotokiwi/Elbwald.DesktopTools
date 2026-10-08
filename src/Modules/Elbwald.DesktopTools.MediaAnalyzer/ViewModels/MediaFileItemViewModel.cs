using System.Globalization;
using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.MediaAnalyzer.ViewModels;

public sealed class MediaFileItemViewModel
{
    public MediaFileItemViewModel(
        MediaAnalyzedFile analyzedFile,
        IReadOnlyList<MediaAnalysisIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(analyzedFile);
        ArgumentNullException.ThrowIfNull(issues);

        AnalyzedFile = analyzedFile;

        var file =
            analyzedFile.File;

        var metadata =
            analyzedFile.ImageMetadata;

        FileName =
            file.FileName;

        FullPath =
            file.FullPath;

        DirectoryPath =
            file.DirectoryPath;

        Extension =
            string.IsNullOrWhiteSpace(file.Extension)
                ? "(ohne Endung)"
                : file.Extension;

        Length =
            file.Length;

        Size =
            FormatBytes(
                file.Length);

        CapturedAt =
            metadata?.CapturedAt;

        CaptureDate =
            metadata?.CapturedAt is { } capturedAt
                ? capturedAt.ToString(
                    "dd.MM.yyyy HH:mm:ss",
                    CultureInfo.CurrentCulture)
                : "nicht vorhanden";

        Camera =
            BuildCameraLabel(
                metadata);

        Resolution =
            metadata?.HasDimensions == true
                ? $"{metadata.PixelWidth:N0} × {metadata.PixelHeight:N0}"
                : "nicht verfügbar";

        Orientation =
            metadata?.DisplayOrientation switch
            {
                ImageDisplayOrientation.Landscape =>
                    "Querformat",

                ImageDisplayOrientation.Portrait =>
                    "Hochformat",

                ImageDisplayOrientation.Square =>
                    "Quadratisch",

                _ =>
                    "Unbekannt"
            };

        Exif =
            metadata?.HasExif == true
                ? "Ja"
                : "Nein";

        Gps =
            metadata?.HasGps == true
                ? "Ja"
                : "Nein";

        ExifGps =
            $"{Exif} / {Gps}";

        IsImage =
            file.MediaType == MediaFileType.Image;

        MissingCaptureDate =
            IsImage
            && metadata?.HasCaptureDate != true;

        MissingDimensions =
            IsImage
            && metadata?.HasDimensions != true;

        IsRaw =
            IsRawExtension(
                file.Extension);

        HasProblem =
            issues.Any(issue =>
                issue.Severity == MediaAnalysisSeverity.Problem);

        HasWarning =
            issues.Any(issue =>
                issue.Severity == MediaAnalysisSeverity.Warning);

        HasInfo =
            !HasProblem
            && !HasWarning
            && issues.Any(issue =>
                issue.Severity == MediaAnalysisSeverity.Info);

        IsOkay =
            !HasProblem
            && !HasWarning
            && !HasInfo;

        StatusLabel =
            HasProblem
                ? "Problem"
                : HasWarning
                    ? "Warnung"
                    : HasInfo
                        ? "Hinweis"
                        : "OK";

        IssueCount =
            issues.Count;
    }

    public MediaAnalyzedFile AnalyzedFile { get; }

    public string FileName { get; }

    public string FullPath { get; }

    public string DirectoryPath { get; }

    public string Extension { get; }

    public long Length { get; }

    public string Size { get; }

    public DateTime? CapturedAt { get; }

    public string CaptureDate { get; }

    public string Camera { get; }

    public string Resolution { get; }

    public string Orientation { get; }

    public string Exif { get; }

    public string Gps { get; }

    public string ExifGps { get; }

    public bool IsImage { get; }

    public bool MissingCaptureDate { get; }

    public bool MissingDimensions { get; }

    public bool IsRaw { get; }

    public bool HasProblem { get; }

    public bool HasWarning { get; }

    public bool HasInfo { get; }

    public bool IsOkay { get; }

    public string StatusLabel { get; }

    public int IssueCount { get; }

    private static string BuildCameraLabel(
        ImageMetadata? metadata)
    {
        if (metadata is null)
        {
            return "nicht verfügbar";
        }

        var make =
            metadata.CameraMake?.Trim();

        var model =
            metadata.CameraModel?.Trim();

        if (string.IsNullOrWhiteSpace(make))
        {
            return string.IsNullOrWhiteSpace(model)
                ? "nicht verfügbar"
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

    private static bool IsRawExtension(
        string extension)
    {
        return extension.Equals(
                   ".arw",
                   StringComparison.OrdinalIgnoreCase)
               || extension.Equals(
                   ".dng",
                   StringComparison.OrdinalIgnoreCase)
               || extension.Equals(
                   ".cr2",
                   StringComparison.OrdinalIgnoreCase)
               || extension.Equals(
                   ".cr3",
                   StringComparison.OrdinalIgnoreCase)
               || extension.Equals(
                   ".nef",
                   StringComparison.OrdinalIgnoreCase)
               || extension.Equals(
                   ".raf",
                   StringComparison.OrdinalIgnoreCase)
               || extension.Equals(
                   ".orf",
                   StringComparison.OrdinalIgnoreCase)
               || extension.Equals(
                   ".rw2",
                   StringComparison.OrdinalIgnoreCase)
               || extension.Equals(
                   ".pef",
                   StringComparison.OrdinalIgnoreCase);
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
