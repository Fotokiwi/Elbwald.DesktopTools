using System.Text.Json;
using System.Text.Json.Serialization;
using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Contracts.Paths;

namespace Elbwald.DesktopTools.Core.Modules;

public sealed class JsonExtensionActivationService : IExtensionActivationService
{
    private const string FileName = "extensions.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

    private readonly object _gate = new();
    private readonly string _configPath;
    private HashSet<string> _disabledIds;

    public JsonExtensionActivationService(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);

        _configPath = Path.Combine(appPaths.ConfigDirectory, FileName);
        _disabledIds = LoadDisabledIds(_configPath);
    }

    public event EventHandler<ExtensionActivationChangedEventArgs>? ActivationChanged;

    public bool IsEnabled(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);

        lock (_gate)
        {
            return !_disabledIds.Contains(extensionId.Trim());
        }
    }

    public bool SetEnabled(string extensionId, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);

        var normalizedId = extensionId.Trim();
        bool changed;

        lock (_gate)
        {
            var currentlyEnabled = !_disabledIds.Contains(normalizedId);

            if (currentlyEnabled == enabled)
            {
                return false;
            }

            var next = new HashSet<string>(
                _disabledIds,
                StringComparer.OrdinalIgnoreCase);

            if (enabled)
            {
                next.Remove(normalizedId);
            }
            else
            {
                next.Add(normalizedId);
            }

            Persist(next);
            _disabledIds = next;
            changed = true;
        }

        if (changed)
        {
            ActivationChanged?.Invoke(
                this,
                new ExtensionActivationChangedEventArgs(
                    normalizedId,
                    enabled));
        }

        return true;
    }

    private void Persist(IReadOnlyCollection<string> disabledIds)
    {
        var directory = Path.GetDirectoryName(_configPath)
                        ?? throw new InvalidOperationException(
                            "Das Extension-Konfigurationsverzeichnis konnte nicht bestimmt werden.");

        Directory.CreateDirectory(directory);

        var model = new ActivationFileModel
        {
            DisabledExtensionIds = disabledIds
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        var json = JsonSerializer.Serialize(model, JsonOptions);
        var tempPath = _configPath + ".tmp";

        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _configPath, overwrite: true);
    }

    private static HashSet<string> LoadDisabledIds(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(configPath);
            var model = JsonSerializer.Deserialize<ActivationFileModel>(json, JsonOptions);

            return (model?.DisabledExtensionIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            Console.Error.WriteLine(
                "Extension-Aktivierungsstatus konnte nicht gelesen werden. "
                + "Alle geladenen Extensions bleiben aktiv: "
                + exception.Message);

            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed record ActivationFileModel
    {
        [JsonPropertyName("disabledExtensionIds")]
        public string[] DisabledExtensionIds { get; init; } = [];
    }
}
