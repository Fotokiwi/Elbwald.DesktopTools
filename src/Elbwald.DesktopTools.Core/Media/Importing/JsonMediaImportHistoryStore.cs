using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Media.Importing;
using Elbwald.DesktopTools.Contracts.Paths;

namespace Elbwald.DesktopTools.Core.Media.Importing;

public sealed class JsonMediaImportHistoryStore : IMediaImportHistoryStore
{
    private const int CurrentVersion = 1;
    private readonly string _historyPath;

    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public JsonMediaImportHistoryStore(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);

        _historyPath = Path.Combine(
            appPaths.ConfigDirectory,
            "media-import-history.json");
    }

    public async Task<MediaImportHistoryEntry?> LoadLastAsync(
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
                || document.LastImport is null
                || string.IsNullOrWhiteSpace(document.LastImport.SourcePath)
                || string.IsNullOrWhiteSpace(document.LastImport.DestinationPath))
            {
                return null;
            }

            return document.LastImport;
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

    public async Task SaveLastAsync(
        MediaImportHistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.SourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.DestinationPath);
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(_historyPath)
            ?? throw new InvalidOperationException(
                "Das Konfigurationsverzeichnis für den Importverlauf konnte nicht ermittelt werden.");

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
                await JsonSerializer.SerializeAsync(
                    stream,
                    new HistoryDocument(CurrentVersion, entry),
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
            // Importverlauf ist Komfortzustand. Eine temporäre Restdatei darf
            // den eigentlichen Import nicht nachträglich als fehlgeschlagen markieren.
        }
    }

    private sealed record HistoryDocument(
        int Version,
        MediaImportHistoryEntry? LastImport);
}
