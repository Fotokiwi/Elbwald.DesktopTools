using System.Reflection;
using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.Core.Modules;

public sealed class ModuleLoader : IModuleLoader
{
    public const int SupportedApiVersion = 1;

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IModuleRegistry _registry;
    private readonly List<ModuleLoadContext> _loadContexts = [];

    public ModuleLoader(IModuleRegistry registry)
    {
        _registry = registry;
    }

    public IReadOnlyList<ModuleLoadResult> LoadModules(
        string modulesRoot,
        Version hostVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulesRoot);
        ArgumentNullException.ThrowIfNull(hostVersion);

        var results = new List<ModuleLoadResult>();
        var fullModulesRoot = Path.GetFullPath(modulesRoot);

        Directory.CreateDirectory(fullModulesRoot);

        foreach (var moduleDirectory in Directory
                     .EnumerateDirectories(fullModulesRoot)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            results.Add(LoadModule(moduleDirectory, hostVersion));
        }

        return results;
    }

    private ModuleLoadResult LoadModule(
        string moduleDirectory,
        Version hostVersion)
    {
        ModuleManifest? manifest = null;

        try
        {
            var manifestPath = Path.Combine(moduleDirectory, "module.json");

            if (!File.Exists(manifestPath))
            {
                return Failure(moduleDirectory, null, "module.json fehlt.");
            }

            manifest = ReadManifest(manifestPath);
            ValidateManifest(manifest, hostVersion);

            if (!manifest.Enabled)
            {
                return Failure(moduleDirectory, manifest.Id, "Modul ist deaktiviert.");
            }

            var assemblyPath = ResolveModuleAssemblyPath(moduleDirectory, manifest.Assembly);

            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException(
                    $"Modul-Assembly wurde nicht gefunden: {manifest.Assembly}",
                    assemblyPath);
            }

            var loadContext = new ModuleLoadContext(assemblyPath);
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var moduleType = FindModuleType(assembly);

            if (Activator.CreateInstance(moduleType) is not IToolModule module)
            {
                throw new InvalidOperationException("Das Modul konnte nicht erstellt werden.");
            }

            if (!string.Equals(module.Id, manifest.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Manifest-ID '{manifest.Id}' und Modul-ID '{module.Id}' stimmen nicht überein.");
            }

            _registry.Register(module);

            // Keep the context alive for as long as the registered module is alive.
            _loadContexts.Add(loadContext);

            return new ModuleLoadResult(
                moduleDirectory,
                manifest.Id,
                true,
                $"{manifest.Name} {manifest.Version} geladen.");
        }
        catch (Exception exception)
        {
            return Failure(moduleDirectory, manifest?.Id, exception.Message);
        }
    }

    private static ModuleManifest ReadManifest(string manifestPath)
    {
        var json = File.ReadAllText(manifestPath);

        return JsonSerializer.Deserialize<ModuleManifest>(json, ManifestJsonOptions)
               ?? throw new InvalidDataException("module.json konnte nicht gelesen werden.");
    }

    private static Type FindModuleType(Assembly assembly)
    {
        var moduleTypes = assembly
            .GetTypes()
            .Where(type =>
                !type.IsAbstract
                && !type.IsInterface
                && typeof(IToolModule).IsAssignableFrom(type))
            .ToArray();

        return moduleTypes.Length switch
        {
            1 => moduleTypes[0],
            0 => throw new InvalidOperationException("Die Assembly enthält kein IToolModule."),
            _ => throw new InvalidOperationException(
                $"Es wurde genau ein IToolModule erwartet, gefunden: {moduleTypes.Length}.")
        };
    }

    private static string ResolveModuleAssemblyPath(
        string moduleDirectory,
        string assemblyRelativePath)
    {
        if (Path.IsPathRooted(assemblyRelativePath))
        {
            throw new InvalidDataException("Der Assembly-Pfad muss relativ zum Modulverzeichnis sein.");
        }

        var fullModuleDirectory = Path.GetFullPath(moduleDirectory);
        var assemblyPath = Path.GetFullPath(
            Path.Combine(fullModuleDirectory, assemblyRelativePath));
        var relativePath = Path.GetRelativePath(fullModuleDirectory, assemblyPath);

        if (relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException(
                "Der Assembly-Pfad liegt außerhalb des Modulverzeichnisses.");
        }

        return assemblyPath;
    }

    private static void ValidateManifest(
        ModuleManifest manifest,
        Version hostVersion)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            throw new InvalidDataException("Modul-ID fehlt.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            throw new InvalidDataException("Modulname fehlt.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Assembly))
        {
            throw new InvalidDataException("Assembly-Angabe fehlt.");
        }

        if (!string.Equals(
                Path.GetExtension(manifest.Assembly),
                ".dll",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Die Modul-Assembly muss eine DLL sein.");
        }

        if (manifest.ApiVersion != SupportedApiVersion)
        {
            throw new InvalidDataException(
                $"API-Version {manifest.ApiVersion} wird nicht unterstützt. "
                + $"Unterstützt wird Version {SupportedApiVersion}.");
        }

        if (!Version.TryParse(manifest.MinimumHostVersion, out var minimumHostVersion))
        {
            throw new InvalidDataException("minimumHostVersion ist ungültig.");
        }

        if (hostVersion < minimumHostVersion)
        {
            throw new InvalidDataException(
                $"Das Modul benötigt mindestens Host-Version {minimumHostVersion}.");
        }

        if (!Version.TryParse(manifest.Version, out _))
        {
            throw new InvalidDataException("Modulversion ist ungültig.");
        }
    }

    private static ModuleLoadResult Failure(
        string moduleDirectory,
        string? moduleId,
        string message)
    {
        return new ModuleLoadResult(
            moduleDirectory,
            moduleId,
            false,
            message);
    }
}
