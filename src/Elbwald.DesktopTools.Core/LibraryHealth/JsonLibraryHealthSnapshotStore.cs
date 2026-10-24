using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elbwald.DesktopTools.Contracts.LibraryHealth;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Paths;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Core.LibraryHealth;

public sealed class JsonLibraryHealthSnapshotStore : ILibraryHealthSnapshotStore
{
    private const int CurrentVersion = 1;
    private readonly string _snapshotPath;

    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public JsonLibraryHealthSnapshotStore(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _snapshotPath = Path.Combine(appPaths.CacheDirectory, "library-health-snapshot.json");
    }

    public async Task<LibraryHealthSnapshot?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_snapshotPath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                _snapshotPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var document = await JsonSerializer.DeserializeAsync<SnapshotDocument>(
                stream,
                SerializerOptions,
                cancellationToken);

            return document is { Version: CurrentVersion, Snapshot: not null }
                ? document.Snapshot
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task SaveAsync(
        LibraryHealthReport report,
        StorageSettings settings,
        MediaIndexSummary mediaIndexSummary,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(mediaIndexSummary);
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(_snapshotPath)
            ?? throw new InvalidOperationException(
                "Das Cache-Verzeichnis für den Bibliotheksstatus konnte nicht ermittelt werden.");

        Directory.CreateDirectory(directory);

        var snapshot = new LibraryHealthSnapshot(
            DateTimeOffset.UtcNow,
            report,
            CreateStorageSettingsFingerprint(settings),
            CreateIndexState(mediaIndexSummary));

        var document = new SnapshotDocument(CurrentVersion, snapshot);
        var temporaryPath = _snapshotPath + ".tmp." + Guid.NewGuid().ToString("N");

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    SerializerOptions,
                    cancellationToken);

                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _snapshotPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // Ein liegen gebliebenes Temp-File darf einen erfolgreichen Health-Scan nicht verdecken.
            }
        }
    }

    public LibraryHealthSnapshotFreshness EvaluateFreshness(
        LibraryHealthSnapshot snapshot,
        StorageSettings settings,
        MediaIndexSummary mediaIndexSummary)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(mediaIndexSummary);

        var storageChanged = !string.Equals(
            snapshot.StorageSettingsFingerprint,
            CreateStorageSettingsFingerprint(settings),
            StringComparison.Ordinal);

        var currentIndexState = CreateIndexState(mediaIndexSummary);
        var indexChanged = snapshot.IndexState != currentIndexState;

        return storageChanged || indexChanged
            ? new LibraryHealthSnapshotFreshness(
                true,
                storageChanged,
                indexChanged)
            : LibraryHealthSnapshotFreshness.Current;
    }

    private static LibraryHealthSnapshotIndexState CreateIndexState(
        MediaIndexSummary summary) =>
        new(
            summary.ItemCount,
            summary.PresentLocationCount,
            summary.HistoricalLocationCount,
            summary.LastIndexedUtc);

    private static string CreateStorageSettingsFingerprint(StorageSettings settings)
    {
        var canonicalEndpoints = settings.Endpoints
            .OrderBy(endpoint => endpoint.Id, StringComparer.OrdinalIgnoreCase)
            .Select(endpoint => new StorageEndpointFingerprint(
                endpoint.Id,
                (int)endpoint.Kind,
                endpoint.Enabled,
                endpoint.Location.VolumeId,
                endpoint.Location.RelativePath))
            .ToArray();

        var json = JsonSerializer.Serialize(canonicalEndpoints);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    private sealed record SnapshotDocument(
        int Version,
        LibraryHealthSnapshot? Snapshot);

    private sealed record StorageEndpointFingerprint(
        string Id,
        int Kind,
        bool Enabled,
        string VolumeId,
        string RelativePath);
}
