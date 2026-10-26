using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Paths;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

public sealed class StemRepairWindowPlacementStore
{
    private const string FileName = "audio-stem-repair-window.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true };

    private readonly string _settingsPath;

    public StemRepairWindowPlacementStore(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _settingsPath = Path.Combine(appPaths.ConfigDirectory, FileName);
    }

    internal StemRepairWindowPlacementSnapshot? Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<StemRepairWindowPlacementSnapshot>(
                File.ReadAllText(_settingsPath),
                JsonOptions);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    internal void Save(StemRepairWindowPlacementSnapshot snapshot)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = _settingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            // Convenience state must never block the editor.
        }
    }
}

internal sealed record StemRepairWindowPlacementSnapshot(
    double Width,
    double Height,
    int X,
    int Y,
    string State,
    bool Topmost);
