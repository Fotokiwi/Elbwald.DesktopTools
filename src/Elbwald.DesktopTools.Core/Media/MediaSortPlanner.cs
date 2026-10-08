using System.Globalization;
using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Dates;
using Elbwald.DesktopTools.Contracts.Media.Sorting;

namespace Elbwald.DesktopTools.Core.Media;

public sealed class MediaSortPlanner
    : IMediaSortPlanner
{
    private const string UnknownCameraSegment =
        "Unbekannte Kamera";

    private readonly IFileOperationPlanner _fileOperationPlanner;
    private readonly IMediaDateResolver _dateResolver;

    public MediaSortPlanner(
        IFileOperationPlanner fileOperationPlanner,
        IMediaDateResolver dateResolver)
    {
        ArgumentNullException.ThrowIfNull(fileOperationPlanner);
        ArgumentNullException.ThrowIfNull(dateResolver);

        _fileOperationPlanner =
            fileOperationPlanner;

        _dateResolver =
            dateResolver;
    }

    public MediaSortPlan CreatePlan(
        string sourceRoot,
        string destinationRoot,
        IEnumerable<MediaAnalyzedFile> files,
        MediaSortOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        ArgumentNullException.ThrowIfNull(files);

        options ??=
            MediaSortOptions.Default;

        var normalizedSourceRoot =
            Path.GetFullPath(sourceRoot);

        var normalizedDestinationRoot =
            Path.GetFullPath(destinationRoot);

        var pathComparer =
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        var issues =
            new List<MediaSortIssue>();

        if (IsSameOrChildPath(
                normalizedDestinationRoot,
                normalizedSourceRoot,
                pathComparer))
        {
            issues.Add(
                new MediaSortIssue(
                    normalizedDestinationRoot,
                    MediaSortIssueKind.DestinationInsideSource,
                    MediaSortIssueSeverity.Warning,
                    "Das Ziel liegt innerhalb der Quelle. Bereits vorhandene Zieldateien können bei späteren Scans erneut erfasst werden."));
        }

        var items =
            new List<MediaSortPlanItem>();

        var requests =
            new List<FileOperationRequest>();

        var ignoredNonImageCount = 0;

        foreach (var analyzedFile in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file =
                analyzedFile.File;

            if (file.MediaType != MediaFileType.Image)
            {
                ignoredNonImageCount++;
                continue;
            }

            var dateResolution =
                _dateResolver.Resolve(
                    analyzedFile);

            if (!dateResolution.IsResolved)
            {
                issues.Add(
                    new MediaSortIssue(
                        file.FullPath,
                        MediaSortIssueKind.MissingCaptureDate,
                        MediaSortIssueSeverity.Warning,
                        dateResolution.Explanation));

                continue;
            }

            var meetsConfidence =
                dateResolution.Confidence
                >= options.MinimumDateConfidence;

            if (dateResolution.NeedsReview
                || !meetsConfidence)
            {
                issues.Add(
                    new MediaSortIssue(
                        file.FullPath,
                        MediaSortIssueKind.DateReview,
                        ResolveDateReviewSeverity(
                            dateResolution,
                            meetsConfidence),
                        BuildDateReviewMessage(
                            dateResolution,
                            options.MinimumDateConfidence,
                            meetsConfidence)));
            }

            if (!meetsConfidence)
            {
                continue;
            }

            var capturedAt =
                dateResolution.Value!.Value;

            var relativeDirectory =
                BuildRelativeDirectory(
                    analyzedFile,
                    capturedAt,
                    options,
                    issues);

            var destinationPath =
                Path.GetFullPath(
                    Path.Combine(
                        normalizedDestinationRoot,
                        relativeDirectory,
                        file.FileName));

            var operationIndex =
                requests.Count;

            items.Add(
                new MediaSortPlanItem(
                    operationIndex,
                    file,
                    capturedAt,
                    dateResolution,
                    relativeDirectory,
                    destinationPath));

            requests.Add(
                options.OperationKind switch
                {
                    FileOperationKind.Copy =>
                        FileOperationRequest.Copy(
                            file.FullPath,
                            destinationPath),

                    FileOperationKind.Move =>
                        FileOperationRequest.Move(
                            file.FullPath,
                            destinationPath),

                    _ =>
                        throw new InvalidOperationException(
                            $"Nicht unterstützte Dateioperation: {options.OperationKind}.")
                });
        }

        var operationPlan =
            _fileOperationPlanner.CreatePlan(
                requests);

        foreach (var conflict in operationPlan.Conflicts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path =
                conflict.OperationIndex >= 0
                && conflict.OperationIndex < items.Count
                    ? items[conflict.OperationIndex].File.FullPath
                    : normalizedSourceRoot;

            issues.Add(
                new MediaSortIssue(
                    path,
                    MediaSortIssueKind.FileOperationConflict,
                    MediaSortIssueSeverity.Problem,
                    DescribeConflict(conflict),
                    conflict.OperationIndex));
        }

        return new MediaSortPlan(
            normalizedSourceRoot,
            normalizedDestinationRoot,
            options,
            items,
            issues,
            operationPlan,
            ignoredNonImageCount);
    }

    private static string BuildRelativeDirectory(
        MediaAnalyzedFile analyzedFile,
        DateTime capturedAt,
        MediaSortOptions options,
        ICollection<MediaSortIssue> issues)
    {
        var year =
            capturedAt.Year.ToString(
                "0000",
                CultureInfo.InvariantCulture);

        var month =
            capturedAt.Month.ToString(
                "00",
                CultureInfo.InvariantCulture);

        if (options.Rule == MediaSortRule.YearMonth)
        {
            return Path.Combine(
                year,
                month);
        }

        var camera =
            CreateCameraLabel(
                analyzedFile.ImageMetadata);

        if (string.IsNullOrWhiteSpace(camera))
        {
            camera =
                UnknownCameraSegment;

            issues.Add(
                new MediaSortIssue(
                    analyzedFile.File.FullPath,
                    MediaSortIssueKind.MissingCameraInformation,
                    MediaSortIssueSeverity.Info,
                    $"Keine Kamerainformation vorhanden. Zielordner: '{UnknownCameraSegment}'."));
        }

        return Path.Combine(
            year,
            SanitizePathSegment(camera),
            month);
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

    private static string SanitizePathSegment(
        string value)
    {
        var forbidden =
            new HashSet<char>(
                Path.GetInvalidFileNameChars());

        foreach (var character in
                 "<>:\"/\\|?*")
        {
            forbidden.Add(character);
        }

        var buffer =
            value
                .Trim()
                .Select(character =>
                    char.IsControl(character)
                    || forbidden.Contains(character)
                        ? '_'
                        : character)
                .ToArray();

        var sanitized =
            new string(buffer)
                .Trim()
                .TrimEnd('.');

        while (sanitized.Contains(
                   "__",
                   StringComparison.Ordinal))
        {
            sanitized =
                sanitized.Replace(
                    "__",
                    "_",
                    StringComparison.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(sanitized)
            || sanitized is "." or "..")
        {
            return UnknownCameraSegment;
        }

        return sanitized.Length <= 80
            ? sanitized
            : sanitized[..80].TrimEnd();
    }

    private static bool IsSameOrChildPath(
        string candidate,
        string parent,
        StringComparer comparer)
    {
        if (comparer.Equals(
                candidate,
                parent))
        {
            return true;
        }

        var parentWithSeparator =
            parent.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal)
                ? parent
                : parent
                  + Path.DirectorySeparatorChar;

        return candidate.StartsWith(
            parentWithSeparator,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private static MediaSortIssueSeverity ResolveDateReviewSeverity(
        MediaDateResolution resolution,
        bool meetsConfidence)
    {
        if (!meetsConfidence
            || (int)resolution.HighestDeviation
                >= (int)MediaDateDeviationLevel.Noticeable)
        {
            return MediaSortIssueSeverity.Warning;
        }

        return MediaSortIssueSeverity.Info;
    }

    private static string BuildDateReviewMessage(
        MediaDateResolution resolution,
        MediaDateConfidence minimumConfidence,
        bool meetsConfidence)
    {
        var parts =
            new List<string>();

        if (!meetsConfidence)
        {
            parts.Add(
                $"Automatische Sortierung ausgesetzt: "
                + $"{FormatConfidence(resolution.Confidence)} < "
                + $"{FormatConfidence(minimumConfidence)}.");
        }
        else
        {
            parts.Add(
                "Aufnahmezeit bleibt im Plan, sollte aber geprüft werden.");
        }

        if (resolution.NeedsReview)
        {
            parts.Add(
                $"Stärkste Abweichung: "
                + $"{FormatDeviation(resolution.HighestDeviation)}.");
        }

        parts.Add(
            resolution.Explanation);

        return string.Join(
            " ",
            parts);
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
                "gering",

            MediaDateDeviationLevel.Noticeable =>
                "auffällig",

            MediaDateDeviationLevel.Strong =>
                "stark",

            _ =>
                "unbekannt"
        };
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

    private static string DescribeConflict(
        FileOperationConflict conflict)
    {
        return conflict.Kind switch
        {
            FileOperationConflictKind.InvalidSourcePath =>
                "Ungültiger Quellpfad.",

            FileOperationConflictKind.InvalidDestinationPath =>
                "Ungültiger Zielpfad.",

            FileOperationConflictKind.SourceFileMissing =>
                "Die Quelldatei existiert nicht mehr.",

            FileOperationConflictKind.SameSourceAndDestination =>
                "Quelle und Ziel sind identisch.",

            FileOperationConflictKind.DestinationAlreadyExists =>
                "Am Ziel existiert bereits eine Datei oder ein Verzeichnis mit diesem Namen.",

            FileOperationConflictKind.DuplicateDestination =>
                "Mehrere Quelldateien würden auf denselben Zielpfad geschrieben.",

            FileOperationConflictKind.SourceUsedMultipleTimes =>
                "Dieselbe Quelldatei ist mehrfach im Plan enthalten.",

            FileOperationConflictKind.DestinationIsAnotherSource =>
                "Ein Zielpfad ist gleichzeitig Quelle einer anderen geplanten Operation.",

            _ =>
                $"Unbekannter Planungskonflikt: {conflict.Kind}."
        };
    }
}
