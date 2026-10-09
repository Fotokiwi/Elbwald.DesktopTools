using System.Security.Cryptography;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Core.MediaIndex;

public sealed class MediaIndexService : IMediaIndexService
{
    private readonly IMediaScanner _mediaScanner;
    private readonly IStorageLocationResolver _storageResolver;
    private readonly SqliteMediaIndexStore _store;

    public MediaIndexService(
        IMediaScanner mediaScanner,
        IStorageLocationResolver storageResolver,
        MediaIndexOptions options)
    {
        ArgumentNullException.ThrowIfNull(mediaScanner);
        ArgumentNullException.ThrowIfNull(storageResolver);
        ArgumentNullException.ThrowIfNull(options);

        _mediaScanner = mediaScanner;
        _storageResolver = storageResolver;
        _store = new SqliteMediaIndexStore(options);
    }

    public async Task<MediaIndexRunResult> IndexAsync(
        StorageSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var startedAt = DateTimeOffset.UtcNow;
        var issues = new List<MediaIndexIssue>();
        var filesDiscovered = 0;
        var filesIndexed = 0;
        var newItems = 0;
        var newLocations = 0;
        var reusedHashes = 0;
        var locationsMarkedMissing = 0;
        var relocationNewCandidates = new List<RelocationNewCandidate>();
        var relocationMissingCandidates = new List<MissingIndexLocation>();
        var enabledEndpoints = settings.Endpoints.Where(endpoint => endpoint.Enabled).ToArray();

        foreach (var endpoint in enabledEndpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolution = _storageResolver.Resolve(endpoint.Location);
            if (!resolution.IsAvailable)
            {
                issues.Add(new MediaIndexIssue(
                    endpoint.Id,
                    MediaIndexIssueKind.EndpointUnavailable,
                    resolution.Message,
                    resolution.ResolvedPath));
                continue;
            }

            var rootPath = resolution.ResolvedPath!;
            var scanId = Guid.NewGuid().ToString("N");
            var endpointComplete = true;
            var endpointNewCandidates = new List<RelocationNewCandidate>();

            MediaScanResult scan;
            try
            {
                scan = await _mediaScanner.ScanAsync(
                    rootPath,
                    new MediaScanOptions
                    {
                        Recursive = true,
                        IncludeUnknownFiles = false
                    },
                    cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidOperationException)
            {
                issues.Add(new MediaIndexIssue(
                    endpoint.Id,
                    MediaIndexIssueKind.ScanError,
                    "Der Speicher-Endpunkt konnte nicht vollständig gescannt werden: " + exception.Message,
                    rootPath));
                continue;
            }

            filesDiscovered += scan.FilesDiscovered;
            if (scan.Errors.Count > 0)
            {
                endpointComplete = false;
                foreach (var error in scan.Errors)
                {
                    issues.Add(new MediaIndexIssue(
                        endpoint.Id,
                        MediaIndexIssueKind.ScanError,
                        $"{error.Operation}: {error.Message}",
                        error.Path));
                }
            }

            foreach (var file in scan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (file.IsSymbolicLink)
                {
                    endpointComplete = false;
                    issues.Add(new MediaIndexIssue(
                        endpoint.Id,
                        MediaIndexIssueKind.SymbolicLinkSkipped,
                        "Symbolische Links werden vom Media Index nicht verfolgt.",
                        file.FullPath));
                    continue;
                }

                if (!TryGetSafeRelativePath(rootPath, file.FullPath, out var relativePath))
                {
                    endpointComplete = false;
                    issues.Add(new MediaIndexIssue(
                        endpoint.Id,
                        MediaIndexIssueKind.InvalidRelativePath,
                        "Der Dateipfad liegt nicht eindeutig innerhalb des konfigurierten Endpunkts und wurde nicht indexiert.",
                        file.FullPath));
                    continue;
                }

                try
                {
                    var indexedState = await _store.TryGetCurrentFileStateAsync(
                        endpoint.Id,
                        relativePath,
                        cancellationToken);

                    if (indexedState is not null
                        && indexedState.Length == file.Length
                        && indexedState.LastWriteTimeUtc.UtcDateTime == file.LastWriteTimeUtc.UtcDateTime)
                    {
                        await _store.TouchExistingLocationAsync(
                            indexedState.LocationId,
                            endpoint.Kind,
                            endpoint.Location.VolumeId,
                            file.Length,
                            file.LastWriteTimeUtc,
                            scanId,
                            DateTimeOffset.UtcNow,
                            cancellationToken);

                        reusedHashes++;
                        filesIndexed++;
                        continue;
                    }

                    var hash = await HashStableFileAsync(file, cancellationToken);
                    if (hash is null)
                    {
                        endpointComplete = false;
                        issues.Add(new MediaIndexIssue(
                            endpoint.Id,
                            MediaIndexIssueKind.FileReadError,
                            "Die Datei hat sich während des Hashens verändert und wurde deshalb nicht in den Index übernommen.",
                            file.FullPath));
                        continue;
                    }

                    var upsert = await _store.UpsertFileAsync(
                        endpoint.Id,
                        endpoint.Kind,
                        endpoint.Location.VolumeId,
                        relativePath,
                        file.MediaType,
                        file.Length,
                        file.LastWriteTimeUtc,
                        hash,
                        scanId,
                        DateTimeOffset.UtcNow,
                        cancellationToken);

                    if (upsert.IsNewItem)
                    {
                        newItems++;
                    }

                    if (upsert.IsNewLocation)
                    {
                        newLocations++;
                        endpointNewCandidates.Add(new RelocationNewCandidate(
                            upsert.MediaItemId,
                            endpoint.Id,
                            relativePath));
                    }

                    filesIndexed++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is IOException
                        or UnauthorizedAccessException
                        or CryptographicException)
                {
                    endpointComplete = false;
                    issues.Add(new MediaIndexIssue(
                        endpoint.Id,
                        MediaIndexIssueKind.FileReadError,
                        "Die Datei konnte nicht sicher gelesen und gehasht werden: " + exception.Message,
                        file.FullPath));
                }
                catch (Exception exception) when (exception is InvalidOperationException)
                {
                    endpointComplete = false;
                    issues.Add(new MediaIndexIssue(
                        endpoint.Id,
                        MediaIndexIssueKind.IndexStoreError,
                        "Der Index konnte für diese Datei nicht aktualisiert werden: " + exception.Message,
                        file.FullPath));
                }
            }

            if (endpointComplete)
            {
                var missing = await _store.MarkUnseenLocationsMissingAsync(
                    endpoint.Id,
                    scanId,
                    cancellationToken);

                locationsMarkedMissing += missing.Count;
                relocationMissingCandidates.AddRange(missing);
                relocationNewCandidates.AddRange(endpointNewCandidates);
            }
        }

        var relocations = ClassifyRelocations(
            relocationNewCandidates,
            relocationMissingCandidates);

        return new MediaIndexRunResult(
            startedAt,
            DateTimeOffset.UtcNow,
            enabledEndpoints.Length,
            filesDiscovered,
            filesIndexed,
            newItems,
            newLocations,
            reusedHashes,
            locationsMarkedMissing,
            relocations,
            issues);
    }

    public Task<MediaIndexSummary> GetSummaryAsync(CancellationToken cancellationToken = default) =>
        _store.GetSummaryAsync(cancellationToken);

    public Task<MediaIndexItem?> GetItemAsync(
        string mediaItemId,
        CancellationToken cancellationToken = default) =>
        _store.GetItemAsync(mediaItemId, cancellationToken);

    public Task<MediaIndexItem?> FindByHashAsync(
        string sha256,
        long length,
        CancellationToken cancellationToken = default) =>
        _store.FindByHashAsync(sha256, length, cancellationToken);

    public Task<IReadOnlyList<MediaIndexLocation>> GetLocationsAsync(
        string mediaItemId,
        CancellationToken cancellationToken = default) =>
        _store.GetLocationsAsync(mediaItemId, cancellationToken);

    public Task<IReadOnlyList<MediaIndexSearchResult>> SearchAsync(
        MediaIndexSearchQuery query,
        CancellationToken cancellationToken = default) =>
        _store.SearchAsync(query, cancellationToken);

    private static IReadOnlyList<MediaIndexRelocation> ClassifyRelocations(
        IReadOnlyList<RelocationNewCandidate> newCandidates,
        IReadOnlyList<MissingIndexLocation> missingCandidates)
    {
        var newByItem = newCandidates
            .GroupBy(candidate => candidate.MediaItemId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var missingByItem = missingCandidates
            .GroupBy(candidate => candidate.MediaItemId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var result = new List<MediaIndexRelocation>();
        foreach (var mediaItemId in newByItem.Keys.Intersect(missingByItem.Keys, StringComparer.Ordinal))
        {
            var added = newByItem[mediaItemId];
            var removed = missingByItem[mediaItemId];

            if (added.Length != 1 || removed.Length != 1)
            {
                continue;
            }

            result.Add(new MediaIndexRelocation(
                mediaItemId,
                removed[0].EndpointId,
                removed[0].RelativePath,
                added[0].EndpointId,
                added[0].RelativePath));
        }

        return result;
    }

    private static bool TryGetSafeRelativePath(
        string rootPath,
        string filePath,
        out string relativePath)
    {
        relativePath = string.Empty;
        string relative;
        try
        {
            relative = Path.GetRelativePath(
                Path.GetFullPath(rootPath),
                Path.GetFullPath(filePath));
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or IOException)
        {
            return false;
        }

        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return false;
        }

        relativePath = relative
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        return !string.IsNullOrWhiteSpace(relativePath)
            && relativePath != ".";
    }

    private static async Task<string?> HashStableFileAsync(
        MediaFile file,
        CancellationToken cancellationToken)
    {
        var before = new FileInfo(file.FullPath);
        if (!before.Exists)
        {
            throw new FileNotFoundException("Die Datei ist nicht mehr vorhanden.", file.FullPath);
        }

        var beforeLength = before.Length;
        var beforeWriteUtc = before.LastWriteTimeUtc;

        await using var stream = new FileStream(
            file.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1024 * 128,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);

        before.Refresh();
        if (!before.Exists
            || before.Length != beforeLength
            || before.LastWriteTimeUtc != beforeWriteUtc)
        {
            return null;
        }

        return Convert.ToHexString(hash);
    }
}

internal sealed record RelocationNewCandidate(
    string MediaItemId,
    string EndpointId,
    string RelativePath);
