using Elbwald.DesktopTools.Contracts.Consolidation;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Core.Consolidation;

public sealed class MediaConsolidationPlanner : IMediaConsolidationPlanner
{
    private readonly IMediaIndexService _mediaIndexService;
    private readonly IStorageSettingsStore _storageSettingsStore;
    private readonly IStorageLocationResolver _storageResolver;

    public MediaConsolidationPlanner(
        IMediaIndexService mediaIndexService,
        IStorageSettingsStore storageSettingsStore,
        IStorageLocationResolver storageResolver)
    {
        ArgumentNullException.ThrowIfNull(mediaIndexService);
        ArgumentNullException.ThrowIfNull(storageSettingsStore);
        ArgumentNullException.ThrowIfNull(storageResolver);

        _mediaIndexService = mediaIndexService;
        _storageSettingsStore = storageSettingsStore;
        _storageResolver = storageResolver;
    }

    public async Task<MediaConsolidationPlan> PlanAsync(
        MediaConsolidationPlanRequest request,
        IProgress<MediaConsolidationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = new List<string>();
        var sources = NormalizeSourcePaths(request.SourcePaths, issues);
        if (sources.Count == 0)
        {
            issues.Add("Es wurde keine gültige Quelle ausgewählt.");
            return Empty(request, issues);
        }

        if (!TryNormalizeAbsolutePath(request.TargetPath, out var targetPath))
        {
            issues.Add("Der Zielpfad ist ungültig.");
            return Empty(request, issues);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new MediaConsolidationProgress(
            "Speicherorte und Medienindex werden geladen …",
            0,
            0));

        var settingsTask = _storageSettingsStore.LoadAsync(cancellationToken);
        var entriesTask = _mediaIndexService.GetPresentPlanningEntriesAsync(cancellationToken);
        await Task.WhenAll(settingsTask, entriesTask).ConfigureAwait(false);

        var settings = await settingsTask.ConfigureAwait(false);
        var entries = await entriesTask.ConfigureAwait(false);
        var endpointRoots = ResolveEndpointRoots(settings, issues);

        var mapped = new List<MappedEntry>(entries.Count);
        var processed = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;

            if (!endpointRoots.TryGetValue(entry.Location.EndpointId, out var endpointRoot))
            {
                continue;
            }

            var absolutePath = CombineUnderRoot(endpointRoot, entry.Location.RelativePath);
            if (absolutePath is null)
            {
                continue;
            }

            mapped.Add(new MappedEntry(entry, absolutePath));

            if (processed % 500 == 0 || processed == entries.Count)
            {
                progress?.Report(new MediaConsolidationProgress(
                    "Index-Fundorte werden zugeordnet …",
                    processed,
                    entries.Count,
                    absolutePath));
            }
        }

        var sourceMapped = mapped
            .Select(item => new
            {
                Entry = item,
                SourceIndex = FindContainingSourceIndex(item.AbsolutePath, sources)
            })
            .Where(item => item.SourceIndex >= 0)
            .Where(item => !IsSameOrBelow(item.Entry.AbsolutePath, targetPath))
            .GroupBy(item => item.Entry.Entry.Location.Id)
            .Select(group => group.OrderBy(item => item.SourceIndex).First())
            .OrderBy(item => item.SourceIndex)
            .ThenBy(item => item.Entry.AbsolutePath, PathComparer)
            .ToArray();

        for (var index = 0; index < sources.Count; index++)
        {
            var sourcePath = sources[index];
            if (!Directory.Exists(sourcePath))
            {
                issues.Add($"Quelle ist derzeit nicht erreichbar: {sourcePath}");
            }

            if (!sourceMapped.Any(item => IsSameOrBelow(item.Entry.AbsolutePath, sourcePath)))
            {
                issues.Add($"Im Medienindex wurden unter dieser Quelle keine aktuellen Medien gefunden: {sourcePath}");
            }
        }

        if (sources.Any(source => IsSameOrBelow(targetPath, source)))
        {
            issues.Add("Der Zielordner liegt innerhalb einer ausgewählten Quelle. Bereits im Ziel liegende Dateien werden deshalb aus der Quellmenge ausgeschlossen.");
        }

        var targetMapped = mapped
            .Where(item => IsSameOrBelow(item.AbsolutePath, targetPath))
            .ToArray();

        if (Directory.Exists(targetPath) && targetMapped.Length == 0 && !IsPathCoveredByResolvedEndpoint(targetPath, endpointRoots.Values))
        {
            issues.Add("Der Zielordner liegt außerhalb der aktuell aufgelösten Speicher-Endpunkte. Bereits vorhandene Zieldateien können im Dry-Run deshalb nicht hashbasiert erkannt werden.");
        }

        var targetByMediaId = targetMapped
            .GroupBy(item => item.Entry.Item.Id, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.AbsolutePath).OrderBy(path => path, PathComparer).ToArray(),
                StringComparer.Ordinal);

        var targetByAbsolutePath = targetMapped
            .GroupBy(item => NormalizeForComparison(item.AbsolutePath), PathComparer)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Entry.Item.Id).Distinct(StringComparer.Ordinal).ToArray(),
                PathComparer);

        var sourceGroups = sourceMapped
            .GroupBy(item => item.Entry.Entry.Item.Id, StringComparer.Ordinal)
            .ToArray();

        var plannedItems = new List<MediaConsolidationPlanItem>(sourceGroups.Length);
        processed = 0;

        foreach (var group in sourceGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;

            var orderedOccurrences = group
                .OrderBy(item => item.SourceIndex)
                .ThenBy(item => item.Entry.AbsolutePath, PathComparer)
                .ToArray();
            var representative = orderedOccurrences[0];
            var indexEntry = representative.Entry.Entry;
            var effectiveDate = indexEntry.CaptureDate
                ?? indexEntry.Location.LastWriteTimeUtc.ToLocalTime().DateTime;
            var relativeTarget = BuildTargetRelativePath(
                representative.Entry.AbsolutePath,
                sources[representative.SourceIndex],
                effectiveDate,
                request.StructureMode);

            if (relativeTarget is null)
            {
                plannedItems.Add(new MediaConsolidationPlanItem(
                    indexEntry.Item.Id,
                    indexEntry.Item.Sha256,
                    indexEntry.Item.Length,
                    representative.Entry.AbsolutePath,
                    orderedOccurrences.Select(item => item.Entry.AbsolutePath).ToArray(),
                    string.Empty,
                    targetPath,
                    effectiveDate,
                    indexEntry.CaptureDate.HasValue,
                    MediaConsolidationPlanItemStatus.Issue,
                    "Für diese Datei konnte kein sicherer Zielpfad erzeugt werden."));
                continue;
            }

            var absoluteTarget = Path.GetFullPath(Path.Combine(targetPath, relativeTarget));
            MediaConsolidationPlanItemStatus status;
            string message;

            if (targetByMediaId.TryGetValue(indexEntry.Item.Id, out var existingTargetPaths))
            {
                status = MediaConsolidationPlanItemStatus.AlreadyInTarget;
                message = "Identischer Inhalt ist bereits im Zielbestand vorhanden: " + existingTargetPaths[0];
            }
            else if (targetByAbsolutePath.TryGetValue(NormalizeForComparison(absoluteTarget), out var occupiedBy)
                     && occupiedBy.Any(id => !string.Equals(id, indexEntry.Item.Id, StringComparison.Ordinal)))
            {
                status = MediaConsolidationPlanItemStatus.NameConflict;
                message = "Am geplanten Zielpfad liegt bereits ein anderer Inhalt.";
            }
            else
            {
                status = MediaConsolidationPlanItemStatus.Copy;
                message = orderedOccurrences.Length > 1
                    ? $"Eine Kopie wird geplant; {orderedOccurrences.Length - 1:N0} identische Quellfundort(e) werden zusammengefasst."
                    : "Datei wird für die spätere Konsolidierung eingeplant.";
            }

            plannedItems.Add(new MediaConsolidationPlanItem(
                indexEntry.Item.Id,
                indexEntry.Item.Sha256,
                indexEntry.Item.Length,
                representative.Entry.AbsolutePath,
                orderedOccurrences.Select(item => item.Entry.AbsolutePath).ToArray(),
                relativeTarget,
                absoluteTarget,
                effectiveDate,
                indexEntry.CaptureDate.HasValue,
                status,
                message));

            if (processed % 250 == 0 || processed == sourceGroups.Length)
            {
                progress?.Report(new MediaConsolidationProgress(
                    "Konsolidierungsplan wird aufgebaut …",
                    processed,
                    sourceGroups.Length,
                    representative.Entry.AbsolutePath));
            }
        }

        // Two different media identities can independently produce the same new target name.
        // Mark every participant as a conflict instead of silently inventing a rename rule.
        var collisions = plannedItems
            .Where(item => item.Status == MediaConsolidationPlanItemStatus.Copy)
            .GroupBy(item => NormalizeForComparison(item.TargetPath), PathComparer)
            .Where(group => group.Select(item => item.MediaItemId).Distinct(StringComparer.Ordinal).Count() > 1)
            .SelectMany(group => group.Select(item => item.MediaItemId))
            .ToHashSet(StringComparer.Ordinal);

        if (collisions.Count > 0)
        {
            for (var index = 0; index < plannedItems.Count; index++)
            {
                var item = plannedItems[index];
                if (!collisions.Contains(item.MediaItemId))
                {
                    continue;
                }

                plannedItems[index] = item with
                {
                    Status = MediaConsolidationPlanItemStatus.NameConflict,
                    Message = "Mehrere unterschiedliche Inhalte würden denselben Zielpfad erhalten. Es wird nichts automatisch umbenannt."
                };
            }
        }

        var indexedSourceLocationCount = sourceMapped.Length;
        var uniqueContentCount = sourceGroups.Length;
        var duplicateSourceCount = Math.Max(0, indexedSourceLocationCount - uniqueContentCount);
        var copyCount = plannedItems.Count(item => item.Status == MediaConsolidationPlanItemStatus.Copy);
        var alreadyInTargetCount = plannedItems.Count(item => item.Status == MediaConsolidationPlanItemStatus.AlreadyInTarget);
        var conflictCount = plannedItems.Count(item => item.Status == MediaConsolidationPlanItemStatus.NameConflict);
        var itemIssueCount = plannedItems.Count(item => item.Status == MediaConsolidationPlanItemStatus.Issue);
        var plannedCopyBytes = plannedItems
            .Where(item => item.Status == MediaConsolidationPlanItemStatus.Copy)
            .Sum(item => item.Length);

        progress?.Report(new MediaConsolidationProgress(
            "Dry-Run abgeschlossen.",
            sourceGroups.Length,
            sourceGroups.Length));

        return new MediaConsolidationPlan(
            targetPath,
            request.StructureMode,
            indexedSourceLocationCount,
            uniqueContentCount,
            duplicateSourceCount,
            copyCount,
            alreadyInTargetCount,
            conflictCount,
            itemIssueCount + issues.Count,
            plannedCopyBytes,
            plannedItems,
            issues);
    }

    private Dictionary<string, string> ResolveEndpointRoots(
        StorageSettings settings,
        ICollection<string> issues)
    {
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in settings.Endpoints.Where(endpoint => endpoint.Enabled))
        {
            var resolution = _storageResolver.Resolve(endpoint.Location);
            if (!resolution.IsAvailable || string.IsNullOrWhiteSpace(resolution.ResolvedPath))
            {
                continue;
            }

            if (!TryNormalizeAbsolutePath(resolution.ResolvedPath, out var root))
            {
                issues.Add($"Speicherort '{endpoint.Name}' konnte nicht in einen gültigen Pfad aufgelöst werden.");
                continue;
            }

            roots[endpoint.Id] = root;
        }

        return roots;
    }

    private static List<string> NormalizeSourcePaths(
        IReadOnlyList<string> sourcePaths,
        ICollection<string> issues)
    {
        var result = new List<string>();
        foreach (var raw in sourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (!TryNormalizeAbsolutePath(raw, out var normalized))
            {
                issues.Add($"Ungültiger Quellpfad: {raw}");
                continue;
            }

            if (result.Any(existing => PathsEqual(existing, normalized)))
            {
                continue;
            }

            result.Add(normalized);
        }

        return result;
    }

    private static int FindContainingSourceIndex(string path, IReadOnlyList<string> sources)
    {
        for (var index = 0; index < sources.Count; index++)
        {
            if (IsSameOrBelow(path, sources[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static string? BuildTargetRelativePath(
        string sourcePath,
        string selectedSourceRoot,
        DateTime effectiveDate,
        MediaConsolidationStructureMode mode)
    {
        var fileName = Path.GetFileName(sourcePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        return mode switch
        {
            MediaConsolidationStructureMode.YearOriginalName =>
                Path.Combine(effectiveDate.Year.ToString("0000"), fileName),

            MediaConsolidationStructureMode.PreserveRelativeStructure =>
                NormalizeRelativePath(Path.GetRelativePath(selectedSourceRoot, sourcePath)),

            _ =>
                Path.Combine(
                    effectiveDate.Year.ToString("0000"),
                    effectiveDate.Month.ToString("00"),
                    fileName)
        };
    }

    private static string? CombineUnderRoot(string root, string relativePath)
    {
        try
        {
            var normalizedRelative = relativePath.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var combined = Path.GetFullPath(Path.Combine(root, normalizedRelative));
            return IsSameOrBelow(combined, root) ? combined : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return null;
        }
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

    private static bool IsPathCoveredByResolvedEndpoint(
        string path,
        IEnumerable<string> endpointRoots) =>
        endpointRoots.Any(root => IsSameOrBelow(path, root));

    private static bool TryNormalizeAbsolutePath(string? path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
            return Path.IsPathFullyQualified(normalized);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsSameOrBelow(string candidate, string root)
    {
        if (PathsEqual(candidate, root))
        {
            return true;
        }

        var rootWithSeparator = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSeparator, PathComparison);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            NormalizeForComparison(left),
            NormalizeForComparison(right),
            PathComparison);

    private static string NormalizeForComparison(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static MediaConsolidationPlan Empty(
        MediaConsolidationPlanRequest request,
        IReadOnlyList<string> issues) =>
        new(
            request.TargetPath ?? string.Empty,
            request.StructureMode,
            0,
            0,
            0,
            0,
            0,
            0,
            issues.Count,
            0,
            Array.Empty<MediaConsolidationPlanItem>(),
            issues);

    private sealed record MappedEntry(
        MediaIndexPlanningEntry Entry,
        string AbsolutePath);
}
