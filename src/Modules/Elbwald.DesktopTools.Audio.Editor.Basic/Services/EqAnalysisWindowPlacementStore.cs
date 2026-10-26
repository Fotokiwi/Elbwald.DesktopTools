using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Paths;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

public sealed class EqAnalysisWindowPlacementStore
{
    private const string FileName = "audio-eq-analysis-window.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true
        };

    private readonly string _settingsPath;

    public EqAnalysisWindowPlacementStore(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _settingsPath = Path.Combine(appPaths.ConfigDirectory, FileName);
    }

    internal EqAnalysisWindowPlacementSnapshot? Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return null;
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<EqAnalysisWindowPlacementSnapshot>(json, JsonOptions);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    internal void Save(EqAnalysisWindowPlacementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = _settingsPath + ".tmp";
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            // Tool-window placement is convenience state and must never block shutdown.
        }
    }
}

internal sealed record EqAnalysisWindowPlacementSnapshot(
    double Width,
    double Height,
    int X,
    int Y,
    string State,
    bool Topmost);
