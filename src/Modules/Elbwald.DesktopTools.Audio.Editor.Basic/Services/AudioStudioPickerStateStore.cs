using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Paths;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

/// <summary>
/// Persists the last successfully used directory for the standalone audio studio.
/// This is convenience state only and must never block a picker when it is stale.
/// </summary>
public sealed class AudioStudioPickerStateStore
{
    private const string FileName = "audio-studio-picker.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true
        };

    private readonly string _settingsPath;

    public AudioStudioPickerStateStore(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _settingsPath = Path.Combine(appPaths.ConfigDirectory, FileName);
    }

    public string? LoadLastDirectory()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return null;
            }

            var json = File.ReadAllText(_settingsPath);
            var state = JsonSerializer.Deserialize<AudioStudioPickerState>(json, JsonOptions);
            if (string.IsNullOrWhiteSpace(state?.LastDirectory))
            {
                return null;
            }

            var fullPath = Path.GetFullPath(state.LastDirectory);
            return Directory.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    public void SaveDirectoryFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var directory = Directory.Exists(path)
            ? path
            : Path.GetDirectoryName(path);

        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        SaveDirectory(directory);
    }

    private void SaveDirectory(string directory)
    {
        try
        {
            var fullPath = Path.GetFullPath(directory);
            if (!Directory.Exists(fullPath))
            {
                return;
            }

            var settingsDirectory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(settingsDirectory))
            {
                Directory.CreateDirectory(settingsDirectory);
            }

            var state = new AudioStudioPickerState(fullPath);
            var temporaryPath = _settingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Remembering a directory is convenience state only.
        }
    }

    private sealed record AudioStudioPickerState(string LastDirectory);
}
