using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Paths;

namespace Elbwald.DesktopTools.App.Services;

public sealed class WindowPlacementStore
{
    private const string FileName = "window-placement.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true
        };

    private readonly string _settingsPath;

    public WindowPlacementStore(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);

        _settingsPath =
            Path.Combine(
                appPaths.ConfigDirectory,
                FileName);
    }

    internal WindowPlacementSnapshot? Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return null;
            }

            var json =
                File.ReadAllText(_settingsPath);

            return JsonSerializer.Deserialize<WindowPlacementSnapshot>(
                json,
                JsonOptions);
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or JsonException)
        {
            // Fensterzustand ist Komfortzustand. Eine beschädigte oder nicht lesbare
            // Datei darf den Anwendungsstart niemals blockieren.
            return null;
        }
    }

    internal void Save(WindowPlacementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            var directory =
                Path.GetDirectoryName(_settingsPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath =
                _settingsPath + ".tmp";

            var json =
                JsonSerializer.Serialize(
                    snapshot,
                    JsonOptions);

            File.WriteAllText(
                temporaryPath,
                json);

            File.Move(
                temporaryPath,
                _settingsPath,
                overwrite: true);
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException)
        {
            // UI-Komfort darf den Shutdown nicht verhindern.
        }
    }
}

internal sealed record WindowPlacementSnapshot(
    double Width,
    double Height,
    int X,
    int Y,
    string State);
