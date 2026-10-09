using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Paths;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Core.Storage;

public sealed class JsonStorageSettingsStore
    : IStorageSettingsStore
{
    private const int CurrentVersion = 2;
    private readonly string _settingsPath;

    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public JsonStorageSettingsStore(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _settingsPath = Path.Combine(appPaths.ConfigDirectory, "storage.json");
    }

    public async Task<StorageSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_settingsPath))
        {
            return StorageSettings.Default;
        }

        await using var stream = new FileStream(
            _settingsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var version = json.RootElement.TryGetProperty("Version", out var versionElement)
            ? versionElement.GetInt32()
            : 0;

        return version switch
        {
            1 => LoadVersion1(json.RootElement),
            CurrentVersion => LoadVersion2(json.RootElement),
            _ => throw new InvalidDataException(
                "Die Speicherort-Konfiguration besitzt ein unbekanntes Format.")
        };
    }

    public async Task SaveAsync(
        StorageSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        Validate(settings);

        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException(
                "Das Konfigurationsverzeichnis konnte nicht ermittelt werden.");

        Directory.CreateDirectory(directory);

        var temporaryPath = _settingsPath + ".tmp." + Guid.NewGuid().ToString("N");

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
                var document = new StorageSettingsDocument(
                    CurrentVersion,
                    settings.Endpoints.ToArray());

                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    SerializerOptions,
                    cancellationToken);

                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _settingsPath, overwrite: true);
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
                // Eine übrig gebliebene temporäre Settings-Datei ist harmloser
                // als ein zweiter Fehler beim Speichern.
            }
        }
    }

    private static StorageSettings LoadVersion1(JsonElement root)
    {
        var legacy = root.Deserialize<LegacyStorageSettingsDocument>(SerializerOptions)
            ?? throw new InvalidDataException("Die Speicherort-Konfiguration ist leer.");

        var settings = StorageSettings.Default;

        if (legacy.ImportHoldingArea is not null)
        {
            settings = settings.WithEndpoint(new StorageEndpoint(
                StorageEndpoint.DefaultImportId,
                "Import-Wartehalle",
                StorageEndpointKind.Import,
                legacy.ImportHoldingArea));
        }

        if (legacy.MediaLibrary is not null)
        {
            settings = settings.WithEndpoint(new StorageEndpoint(
                StorageEndpoint.DefaultLibraryId,
                "Medienbibliothek",
                StorageEndpointKind.MediaLibrary,
                legacy.MediaLibrary));
        }

        return settings;
    }

    private static StorageSettings LoadVersion2(JsonElement root)
    {
        var document = root.Deserialize<StorageSettingsDocument>(SerializerOptions)
            ?? throw new InvalidDataException("Die Speicherort-Konfiguration ist leer.");

        var settings = new StorageSettings(document.Endpoints ?? Array.Empty<StorageEndpoint>());
        Validate(settings);
        return settings;
    }

    private static void Validate(StorageSettings settings)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in settings.Endpoints)
        {
            if (string.IsNullOrWhiteSpace(endpoint.Id)
                || string.IsNullOrWhiteSpace(endpoint.Name)
                || endpoint.Location is null
                || string.IsNullOrWhiteSpace(endpoint.Location.VolumeId))
            {
                throw new InvalidDataException(
                    "Mindestens ein Speicher-Endpunkt ist unvollständig konfiguriert.");
            }

            if (!ids.Add(endpoint.Id))
            {
                throw new InvalidDataException(
                    $"Die Speicher-Endpunkt-ID '{endpoint.Id}' ist mehrfach vorhanden.");
            }
        }
    }

    private sealed record StorageSettingsDocument(
        int Version,
        StorageEndpoint[]? Endpoints);

    private sealed record LegacyStorageSettingsDocument(
        int Version,
        StorageLocation? ImportHoldingArea,
        StorageLocation? MediaLibrary);
}
