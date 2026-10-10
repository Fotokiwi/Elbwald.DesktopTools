using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elbwald.DesktopTools.App.Services;

public sealed record MediaThumbnailOptions
{
    private const int MinimumWidth = 64;
    private const int MaximumAllowedWidth = 4096;

    public required string CacheDirectory { get; init; }

    [JsonIgnore]
    public string? SettingsPath { get; init; }

    public int DefaultWidth { get; set; } = 640;

    public int MaximumWidth { get; set; } = MaximumAllowedWidth;

    public int GridWidth { get; set; } = 168;

    public int DetailWidth { get; set; } = 720;

    public int ViewerWidth { get; set; } = 1920;

    public void Normalize()
    {
        DefaultWidth = Clamp(DefaultWidth, 320, 1920);
        GridWidth = Clamp(GridWidth, 96, 512);
        DetailWidth = Clamp(DetailWidth, 320, 1920);
        ViewerWidth = Clamp(ViewerWidth, 640, MaximumAllowedWidth);
        MaximumWidth = Math.Clamp(
            Math.Max(
                Math.Max(DefaultWidth, DetailWidth),
                ViewerWidth),
            MinimumWidth,
            MaximumAllowedWidth);
    }

    public void Save()
    {
        if (string.IsNullOrWhiteSpace(SettingsPath))
        {
            return;
        }

        Normalize();

        var fullPath = Path.GetFullPath(SettingsPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Der Pfad für die Thumbnail-Einstellungen ist ungültig.");
        }

        Directory.CreateDirectory(directory);

        var payload = new PersistedThumbnailSettings(
            GridWidth,
            DetailWidth,
            ViewerWidth);

        var json = JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        var partialPath = fullPath + ".partial";
        File.WriteAllText(partialPath, json);
        File.Move(partialPath, fullPath, overwrite: true);
    }

    public static MediaThumbnailOptions Load(
        string cacheDirectory,
        string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);

        var options = new MediaThumbnailOptions
        {
            CacheDirectory = Path.GetFullPath(cacheDirectory),
            SettingsPath = Path.GetFullPath(settingsPath)
        };

        try
        {
            if (File.Exists(options.SettingsPath))
            {
                var json = File.ReadAllText(options.SettingsPath);
                var persisted = JsonSerializer.Deserialize<PersistedThumbnailSettings>(json);
                if (persisted is not null)
                {
                    options.GridWidth = persisted.GridWidth;
                    options.DetailWidth = persisted.DetailWidth;
                    options.ViewerWidth = persisted.ViewerWidth;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException)
        {
            // Optional settings: invalid/unreadable files fall back to safe defaults.
        }

        options.Normalize();
        return options;
    }

    private static int Clamp(int value, int minimum, int maximum) =>
        Math.Clamp(value, minimum, maximum);

    private sealed record PersistedThumbnailSettings(
        int GridWidth,
        int DetailWidth,
        int ViewerWidth);
}
