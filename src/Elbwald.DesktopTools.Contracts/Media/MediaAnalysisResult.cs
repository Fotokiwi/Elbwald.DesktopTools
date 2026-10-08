namespace Elbwald.DesktopTools.Contracts.Media;

public sealed class MediaAnalysisResult
{
    private const int LargestFileLimit = 10;

    public MediaAnalysisResult(
        string rootPath,
        IEnumerable<MediaAnalyzedFile> files,
        IEnumerable<MediaAnalysisIssue> issues,
        int directoriesVisited,
        int filesDiscovered,
        int skippedSymbolicLinkDirectories,
        int metadataCacheHits = 0,
        int metadataCacheMisses = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(issues);

        RootPath = rootPath;
        Files = files.ToArray();
        Issues = issues.ToArray();
        DirectoriesVisited = directoriesVisited;
        FilesDiscovered = filesDiscovered;
        SkippedSymbolicLinkDirectories =
            skippedSymbolicLinkDirectories;
        MetadataCacheHits = metadataCacheHits;
        MetadataCacheMisses = metadataCacheMisses;

        FormatCounts =
            BuildFormatCounts(Files);

        CameraCounts =
            BuildCameraCounts(Files);

        LargestFiles =
            Files
                .OrderByDescending(file => file.File.Length)
                .ThenBy(
                    file => file.File.FileName,
                    StringComparer.OrdinalIgnoreCase)
                .Take(LargestFileLimit)
                .ToArray();
    }

    public string RootPath { get; }

    public IReadOnlyList<MediaAnalyzedFile> Files { get; }

    public IReadOnlyList<MediaAnalysisIssue> Issues { get; }

    public int DirectoriesVisited { get; }

    public int FilesDiscovered { get; }

    public int SkippedSymbolicLinkDirectories { get; }

    public int MetadataCacheHits { get; }

    public int MetadataCacheMisses { get; }

    public int TotalFiles =>
        Files.Count;

    public long TotalBytes =>
        Files.Sum(file => file.File.Length);

    public int ImageCount =>
        Files.Count(file =>
            file.File.MediaType == MediaFileType.Image);

    public int ImagesWithExif =>
        Files.Count(file =>
            file.ImageMetadata?.HasExif == true);

    public int ImagesWithGps =>
        Files.Count(file =>
            file.ImageMetadata?.HasGps == true);

    public int ImagesWithCaptureDate =>
        Files.Count(file =>
            file.ImageMetadata?.HasCaptureDate == true);

    public int MissingCaptureDateCount =>
        Math.Max(
            0,
            ImageCount - ImagesWithCaptureDate);

    public int ImagesWithDimensions =>
        Files.Count(file =>
            file.ImageMetadata?.HasDimensions == true);

    public int LandscapeImageCount =>
        CountOrientation(
            ImageDisplayOrientation.Landscape);

    public int PortraitImageCount =>
        CountOrientation(
            ImageDisplayOrientation.Portrait);

    public int SquareImageCount =>
        CountOrientation(
            ImageDisplayOrientation.Square);

    public int UnknownOrientationCount =>
        CountOrientation(
            ImageDisplayOrientation.Unknown);

    public int MetadataErrorCount =>
        Issues.Count(issue =>
            issue.Kind == MediaAnalysisIssueKind.MetadataError);

    public int ScanErrorCount =>
        Issues.Count(issue =>
            issue.Kind == MediaAnalysisIssueKind.ScanError);

    public int ProblemCount =>
        Issues.Count(issue =>
            issue.Severity == MediaAnalysisSeverity.Problem);

    public int WarningCount =>
        Issues.Count(issue =>
            issue.Severity == MediaAnalysisSeverity.Warning);

    public int InfoCount =>
        Issues.Count(issue =>
            issue.Severity == MediaAnalysisSeverity.Info);

    public bool HasIssues =>
        Issues.Count > 0;

    public bool HasProblems =>
        ProblemCount > 0;

    public IReadOnlyDictionary<string, int> FormatCounts { get; }

    public IReadOnlyDictionary<string, int> CameraCounts { get; }

    public IReadOnlyList<MediaAnalyzedFile> LargestFiles { get; }

    private int CountOrientation(
        ImageDisplayOrientation orientation)
    {
        return Files.Count(file =>
            file.File.MediaType == MediaFileType.Image
            && (file.ImageMetadata?.DisplayOrientation
                ?? ImageDisplayOrientation.Unknown) == orientation);
    }

    private static IReadOnlyDictionary<string, int> BuildFormatCounts(
        IEnumerable<MediaAnalyzedFile> files)
    {
        return files
            .GroupBy(
                file =>
                    string.IsNullOrWhiteSpace(file.File.Extension)
                        ? "(ohne Endung)"
                        : file.File.Extension.ToLowerInvariant(),
                StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, int> BuildCameraCounts(
        IEnumerable<MediaAnalyzedFile> files)
    {
        return files
            .Select(file =>
                CreateCameraLabel(file.ImageMetadata))
            .Where(label =>
                !string.IsNullOrWhiteSpace(label))
            .Cast<string>()
            .GroupBy(
                label => label,
                StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string? CreateCameraLabel(
        ImageMetadata? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        var make =
            metadata.CameraMake?.Trim();

        var model =
            metadata.CameraModel?.Trim();

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
}
