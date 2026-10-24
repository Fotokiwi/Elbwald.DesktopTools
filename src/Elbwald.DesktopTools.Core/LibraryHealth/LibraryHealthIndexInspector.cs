using Elbwald.DesktopTools.Contracts.LibraryHealth;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Core.LibraryHealth;

public sealed class LibraryHealthIndexInspector : ILibraryHealthIndexInspector
{
    private const int PageSize = 500;
    private readonly IMediaIndexService _mediaIndexService;

    public LibraryHealthIndexInspector(IMediaIndexService mediaIndexService)
    {
        ArgumentNullException.ThrowIfNull(mediaIndexService);
        _mediaIndexService = mediaIndexService;
    }

    public Task<LibraryHealthIndexSummary> InspectAsync(
        IReadOnlyList<LibraryHealthIndexEndpointState> endpointStates,
        CancellationToken cancellationToken = default)
        => InspectAsync(endpointStates, cancellationToken, progress: null);

    public async Task<LibraryHealthIndexSummary> InspectAsync(
        IReadOnlyList<LibraryHealthIndexEndpointState> endpointStates,
        CancellationToken cancellationToken,
        IProgress<LibraryHealthProgress>? progress)
    {
        ArgumentNullException.ThrowIfNull(endpointStates);

        progress?.Report(new LibraryHealthProgress(
            LibraryHealthScanStage.InspectingIndexLocations));

        var summary = await _mediaIndexService.GetSummaryAsync(cancellationToken);
        if (summary.ItemCount <= 0)
        {
            return new LibraryHealthIndexSummary(
                0,
                0,
                0,
                0,
                summary.LastIndexedUtc);
        }

        var endpointStateById = endpointStates
            .GroupBy(state => state.EndpointId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var locations = await _mediaIndexService.GetAllLocationsAsync(cancellationToken);

        progress?.Report(new LibraryHealthProgress(
            LibraryHealthScanStage.InspectingIndexLocations,
            Processed: 0,
            Total: locations.Count));

        var mediaStates = new Dictionary<string, MediaReachability>(StringComparer.Ordinal);
        var endpointCounters = new Dictionary<string, EndpointCounter>(StringComparer.OrdinalIgnoreCase);
        var staleLocationCount = 0;

        for (var locationIndex = 0; locationIndex < locations.Count; locationIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var location = locations[locationIndex];
            ReportLocationProgress(progress, locationIndex, locations.Count);

            if (!mediaStates.TryGetValue(location.MediaItemId, out var mediaState))
            {
                mediaState = new MediaReachability();
                mediaStates[location.MediaItemId] = mediaState;
            }

            if (!endpointCounters.TryGetValue(location.EndpointId, out var endpointCounter))
            {
                endpointCounter = new EndpointCounter(location.EndpointId);
                endpointCounters[location.EndpointId] = endpointCounter;
            }

            endpointCounter.IndexedLocationCount++;

            if (!location.IsPresent)
            {
                endpointCounter.HistoricalLocationCount++;
                mediaState.HasHistoricalLocation = true;
                continue;
            }

            if (!endpointStateById.TryGetValue(location.EndpointId, out var endpointState))
            {
                endpointCounter.UnconfiguredLocationCount++;
                mediaState.HasUnconfiguredLocation = true;
                continue;
            }

            if (!endpointState.Enabled
                || endpointState.StorageStatus == StorageLocationStatus.VolumeUnavailable)
            {
                endpointCounter.OfflineLocationCount++;
                mediaState.HasOfflineLocation = true;
                continue;
            }

            if (endpointState.StorageStatus == StorageLocationStatus.PathMissing)
            {
                endpointCounter.MissingLocationCount++;
                mediaState.HasMissingLocation = true;
                staleLocationCount++;
                continue;
            }

            if (endpointState.StorageStatus != StorageLocationStatus.Available
                || string.IsNullOrWhiteSpace(endpointState.ResolvedPath))
            {
                endpointCounter.UnconfiguredLocationCount++;
                mediaState.HasUnconfiguredLocation = true;
                continue;
            }

            if (!TryResolveIndexedFilePath(
                    endpointState.ResolvedPath,
                    location.RelativePath,
                    out var absolutePath)
                || !File.Exists(absolutePath))
            {
                endpointCounter.MissingLocationCount++;
                mediaState.HasMissingLocation = true;
                staleLocationCount++;
                continue;
            }

            endpointCounter.ReachableLocationCount++;
            mediaState.HasReachableLocation = true;

            ReportLocationProgress(progress, locationIndex + 1, locations.Count);
        }

        if (locations.Count > 0)
        {
            progress?.Report(new LibraryHealthProgress(
                LibraryHealthScanStage.InspectingIndexLocations,
                Processed: locations.Count,
                Total: locations.Count));
        }

        var reachableMediaCount = 0;
        var offlineMediaCount = 0;
        var missingMediaCount = 0;
        var unconfiguredMediaCount = 0;

        foreach (var mediaState in mediaStates.Values)
        {
            if (mediaState.HasReachableLocation)
            {
                reachableMediaCount++;
            }
            else if (mediaState.HasOfflineLocation)
            {
                // A currently offline copy means we cannot prove the medium is missing.
                // Treat it as unavailable, not lost, even when another stale location exists.
                offlineMediaCount++;
            }
            else if (mediaState.HasUnconfiguredLocation)
            {
                unconfiguredMediaCount++;
            }
            else
            {
                missingMediaCount++;
            }
        }

        // Media items without any location are structurally orphaned and therefore missing.
        var itemsWithoutLocations = Math.Max(0, summary.ItemCount - mediaStates.Count);
        missingMediaCount += itemsWithoutLocations;

        var missingCaptureDateCount = await CountMissingCaptureDatesAsync(
            summary.ItemCount,
            cancellationToken,
            progress);

        return new LibraryHealthIndexSummary(
            summary.ItemCount,
            reachableMediaCount,
            missingMediaCount + unconfiguredMediaCount,
            missingCaptureDateCount,
            summary.LastIndexedUtc)
        {
            OfflineMediaCount = offlineMediaCount,
            MissingMediaCount = missingMediaCount,
            UnconfiguredMediaCount = unconfiguredMediaCount,
            StaleLocationCount = staleLocationCount,
            EndpointSummaries = endpointCounters.Values
                .OrderBy(counter => counter.EndpointId, StringComparer.OrdinalIgnoreCase)
                .Select(counter => counter.ToSummary())
                .ToArray()
        };
    }

    private async Task<int> CountMissingCaptureDatesAsync(
        int itemCount,
        CancellationToken cancellationToken,
        IProgress<LibraryHealthProgress>? progress)
    {
        progress?.Report(new LibraryHealthProgress(
            LibraryHealthScanStage.InspectingIndexMetadata,
            Processed: 0,
            Total: itemCount));

        var offset = 0;
        var inspected = 0;
        var missingCaptureDate = 0;

        while (inspected < itemCount)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = await _mediaIndexService.SearchAsync(
                new MediaIndexSearchQuery(
                    Limit: PageSize,
                    Offset: offset,
                    SortOrder: MediaIndexSortOrder.LastSeenDescending),
                cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            foreach (var result in page)
            {
                if (result.Item.MediaType == MediaFileType.Image
                    && result.CaptureDate is null)
                {
                    missingCaptureDate++;
                }
            }

            inspected += page.Count;
            offset += page.Count;

            progress?.Report(new LibraryHealthProgress(
                LibraryHealthScanStage.InspectingIndexMetadata,
                Processed: Math.Min(inspected, itemCount),
                Total: itemCount));
        }

        return missingCaptureDate;
    }

    private static void ReportLocationProgress(
        IProgress<LibraryHealthProgress>? progress,
        int processed,
        int total)
    {
        if (progress is null
            || processed <= 0
            || (processed % 256 != 0 && processed != total))
        {
            return;
        }

        progress.Report(new LibraryHealthProgress(
            LibraryHealthScanStage.InspectingIndexLocations,
            Processed: processed,
            Total: total));
    }

    private static bool TryResolveIndexedFilePath(
        string rootPath,
        string relativePath,
        out string absolutePath)
    {
        absolutePath = string.Empty;

        try
        {
            if (string.IsNullOrWhiteSpace(rootPath)
                || Path.IsPathRooted(relativePath))
            {
                return false;
            }

            var parts = relativePath
                .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

            if (parts.Any(part => part is "." or ".."))
            {
                return false;
            }

            var normalizedRoot = Path.GetFullPath(rootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var candidate = parts.Length == 0
                ? normalizedRoot
                : Path.GetFullPath(Path.Combine(normalizedRoot, Path.Combine(parts)));

            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!string.Equals(candidate, normalizedRoot, comparison)
                && !candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison))
            {
                return false;
            }

            absolutePath = candidate;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }

    private sealed class MediaReachability
    {
        public bool HasReachableLocation { get; set; }

        public bool HasOfflineLocation { get; set; }

        public bool HasMissingLocation { get; set; }

        public bool HasUnconfiguredLocation { get; set; }

        public bool HasHistoricalLocation { get; set; }
    }

    private sealed class EndpointCounter(string endpointId)
    {
        public string EndpointId { get; } = endpointId;

        public int IndexedLocationCount { get; set; }

        public int ReachableLocationCount { get; set; }

        public int OfflineLocationCount { get; set; }

        public int MissingLocationCount { get; set; }

        public int HistoricalLocationCount { get; set; }

        public int UnconfiguredLocationCount { get; set; }

        public LibraryHealthIndexEndpointSummary ToSummary() =>
            new(
                EndpointId,
                IndexedLocationCount,
                ReachableLocationCount,
                OfflineLocationCount,
                MissingLocationCount,
                HistoricalLocationCount,
                UnconfiguredLocationCount);
    }
}
