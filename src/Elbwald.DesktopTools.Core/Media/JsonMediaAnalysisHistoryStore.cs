using System.IO.Compression;
using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Paths;

namespace Elbwald.DesktopTools.Core.Media;

public sealed class JsonMediaAnalysisHistoryStore
    : IMediaAnalysisHistoryStore
{
    private const int CurrentVersion = 1;
    private readonly string _historyPath;
    private readonly string _resultSnapshotPath;

    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public JsonMediaAnalysisHistoryStore(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);

        _historyPath = Path.Combine(
            appPaths.ConfigDirectory,
            "media-analysis-history.json");

        _resultSnapshotPath = Path.Combine(
            appPaths.CacheDirectory,
            "media-analysis-last-result.json.gz");
    }

    public async Task<MediaAnalysisHistoryEntry?> LoadLastAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_historyPath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                _historyPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var document = await JsonSerializer.DeserializeAsync<HistoryDocument>(
                stream,
                SerializerOptions,
                cancellationToken);

            if (document is null
                || document.Version != CurrentVersion
                || document.LastAnalysis is null
                || string.IsNullOrWhiteSpace(document.LastAnalysis.SourcePath))
            {
                return null;
            }

            return document.LastAnalysis;
        }
        catch (JsonException)
        {
            // Verlauf ist Komfortzustand. Eine beschädigte Datei darf die App
            // oder den read-only Analyzer nicht blockieren.
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

    public async Task<MediaAnalysisResultSnapshot?> LoadLastResultAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_resultSnapshotPath))
        {
            return null;
        }

        try
        {
            await using var fileStream = new FileStream(
                _resultSnapshotPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await using var gzipStream = new GZipStream(
                fileStream,
                CompressionMode.Decompress,
                leaveOpen: false);

            return await JsonSerializer.DeserializeAsync<MediaAnalysisResultSnapshot>(
                gzipStream,
                SerializerOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidDataException)
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

    public async Task SaveLastAsync(
        MediaAnalysisHistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.SourcePath);
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(_historyPath)
            ?? throw new InvalidOperationException(
                "Das Konfigurationsverzeichnis für den Analyseverlauf konnte nicht ermittelt werden.");

        Directory.CreateDirectory(directory);

        var temporaryPath =
            _historyPath + ".tmp." + Guid.NewGuid().ToString("N");

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
                var document = new HistoryDocument(
                    CurrentVersion,
                    entry);

                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    SerializerOptions,
                    cancellationToken);

                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(
                temporaryPath,
                _historyPath,
                overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    public async Task SaveLastResultAsync(
        MediaAnalysisResultSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.RootPath);
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(_resultSnapshotPath)
            ?? throw new InvalidOperationException(
                "Das Cacheverzeichnis für das letzte Analyseergebnis konnte nicht ermittelt werden.");

        Directory.CreateDirectory(directory);

        var temporaryPath =
            _resultSnapshotPath + ".tmp." + Guid.NewGuid().ToString("N");

        try
        {
            await using (var fileStream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var gzipStream = new GZipStream(
                             fileStream,
                             CompressionLevel.Optimal,
                             leaveOpen: true))
            {
                await JsonSerializer.SerializeAsync(
                    gzipStream,
                    snapshot,
                    SerializerOptions,
                    cancellationToken);
            }

            File.Move(
                temporaryPath,
                _resultSnapshotPath,
                overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Eine temporäre Restdatei ist weniger kritisch als ein
            // zusätzlicher Fehler beim Beenden des Speichervorgangs.
        }
    }

    private sealed record HistoryDocument(
        int Version,
        MediaAnalysisHistoryEntry? LastAnalysis);
}
