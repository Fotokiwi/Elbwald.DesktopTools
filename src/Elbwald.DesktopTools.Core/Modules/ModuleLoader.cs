using System.Reflection;
using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Core.Modules;

public sealed class ModuleLoader : IModuleLoader
{
    public const int SupportedApiVersion = 2;

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IModuleRegistry _registry;
    private readonly List<ModuleLoadContext> _loadContexts = [];

    public ModuleLoader(IModuleRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public IReadOnlyList<ModuleLoadResult> LoadModules(
        string modulesRoot,
        Version hostVersion,
        IServiceCollection services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulesRoot);
        ArgumentNullException.ThrowIfNull(hostVersion);
        ArgumentNullException.ThrowIfNull(services);

        var fullModulesRoot = Path.GetFullPath(modulesRoot);
        Directory.CreateDirectory(fullModulesRoot);

        var moduleDirectories = Directory
            .EnumerateDirectories(fullModulesRoot)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var resultsByDirectory = new Dictionary<string, ModuleLoadResult>(
            StringComparer.OrdinalIgnoreCase);
        var candidates = new Dictionary<string, ModuleCandidate>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var moduleDirectory in moduleDirectories)
        {
            TryReadCandidate(
                moduleDirectory,
                hostVersion,
                candidates,
                resultsByDirectory);
        }

        LoadCandidates(
            candidates,
            services,
            resultsByDirectory);

        return moduleDirectories
            .Select(directory =>
                resultsByDirectory.TryGetValue(directory, out var result)
                    ? result
                    : Failure(
                        directory,
                        null,
                        "Modul konnte keinem Ladeergebnis zugeordnet werden."))
            .ToArray();
    }

    private static void TryReadCandidate(
        string moduleDirectory,
        Version hostVersion,
        IDictionary<string, ModuleCandidate> candidates,
        IDictionary<string, ModuleLoadResult> resultsByDirectory)
    {
        ModuleManifest? manifest = null;

        try
        {
            var manifestPath = Path.Combine(moduleDirectory, "module.json");

            if (!File.Exists(manifestPath))
            {
                resultsByDirectory[moduleDirectory] =
                    Failure(moduleDirectory, null, "module.json fehlt.");
                return;
            }

            manifest = ReadManifest(manifestPath);
            ValidateManifest(manifest, hostVersion);

            if (!manifest.Enabled)
            {
                resultsByDirectory[moduleDirectory] =
                    Failure(moduleDirectory, manifest.Id, "Modul ist deaktiviert.");
                return;
            }

            if (candidates.ContainsKey(manifest.Id))
            {
                resultsByDirectory[moduleDirectory] =
                    Failure(
                        moduleDirectory,
                        manifest.Id,
                        $"Modul-ID '{manifest.Id}' ist mehrfach vorhanden.");
                return;
            }

            candidates.Add(
                manifest.Id,
                new ModuleCandidate(moduleDirectory, manifest));
        }
        catch (Exception exception)
        {
            resultsByDirectory[moduleDirectory] =
                Failure(moduleDirectory, manifest?.Id, exception.Message);
        }
    }

    private void LoadCandidates(
        IReadOnlyDictionary<string, ModuleCandidate> candidates,
        IServiceCollection services,
        IDictionary<string, ModuleLoadResult> resultsByDirectory)
    {
        var pending = candidates.Values
            .OrderBy(candidate => candidate.Directory, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var loadedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pending.Count > 0)
        {
            var madeProgress = false;

            for (var index = pending.Count - 1; index >= 0; index--)
            {
                var candidate = pending[index];
                var dependencyState = EvaluateDependencies(
                    candidate,
                    candidates,
                    loadedIds,
                    failedIds);

                if (dependencyState.State == DependencyState.Waiting)
                {
                    continue;
                }

                pending.RemoveAt(index);
                madeProgress = true;

                if (dependencyState.State == DependencyState.Failed)
                {
                    resultsByDirectory[candidate.Directory] =
                        Failure(
                            candidate.Directory,
                            candidate.Manifest.Id,
                            dependencyState.Message
                            ?? "Modulabhängigkeit ist nicht verfügbar.");
                    failedIds.Add(candidate.Manifest.Id);
                    continue;
                }

                var result = LoadCandidate(candidate, services);
                resultsByDirectory[candidate.Directory] = result;

                if (result.Success)
                {
                    loadedIds.Add(candidate.Manifest.Id);
                }
                else
                {
                    failedIds.Add(candidate.Manifest.Id);
                }
            }

            if (madeProgress)
            {
                continue;
            }

            foreach (var candidate in pending)
            {
                resultsByDirectory[candidate.Directory] =
                    Failure(
                        candidate.Directory,
                        candidate.Manifest.Id,
                        "Modulabhängigkeiten bilden einen Zyklus oder konnten nicht aufgelöst werden.");
                failedIds.Add(candidate.Manifest.Id);
            }

            pending.Clear();
        }
    }

    private ModuleLoadResult LoadCandidate(
        ModuleCandidate candidate,
        IServiceCollection services)
    {
        var manifest = candidate.Manifest;
        ModuleLoadContext? loadContext = null;
        ExtensionServiceCollection? extensionServices = null;

        try
        {
            var assemblyPath = ResolveModuleAssemblyPath(
                candidate.Directory,
                manifest.Assembly);

            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException(
                    $"Modul-Assembly wurde nicht gefunden: {manifest.Assembly}",
                    assemblyPath);
            }

            loadContext = new ModuleLoadContext(assemblyPath);
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var moduleType = FindModuleType(assembly);

            if (Activator.CreateInstance(moduleType) is not IExtensionModule module)
            {
                throw new InvalidOperationException(
                    "Die Extension konnte nicht erstellt werden.");
            }

            if (!string.Equals(module.Id, manifest.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Manifest-ID '{manifest.Id}' und Extension-ID '{module.Id}' stimmen nicht überein.");
            }

            extensionServices = new ExtensionServiceCollection(services);
            module.ConfigureServices(extensionServices);

            ValidateCapabilityProviderRegistrations(
                module,
                manifest,
                extensionServices);

            extensionServices.AddSingleton(
                new ExtensionDescriptor(
                    manifest.Id.Trim(),
                    manifest.Name.Trim(),
                    manifest.Description,
                    manifest.Version.Trim(),
                    manifest.ApiVersion,
                    NormalizeValues(manifest.Capabilities),
                    NormalizeValues(manifest.Permissions)));

            if (module is IToolModule toolModule)
            {
                var descriptor = toolModule.GetDescriptor();

                if (!string.Equals(
                        descriptor.Id,
                        module.Id,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Tool-Descriptor-ID '{descriptor.Id}' und Extension-ID '{module.Id}' stimmen nicht überein.");
                }

                _registry.Register(toolModule);
            }

            // Keep the context alive for as long as services or registered tools
            // can reference types from the extension assembly.
            _loadContexts.Add(loadContext);

            return new ModuleLoadResult(
                candidate.Directory,
                manifest.Id,
                true,
                $"{manifest.Name} {manifest.Version} geladen (Module API v{manifest.ApiVersion}).");
        }
        catch (Exception exception)
        {
            extensionServices?.Rollback();

            return Failure(
                candidate.Directory,
                manifest.Id,
                exception.GetBaseException().Message);
        }
    }

    private static DependencyEvaluation EvaluateDependencies(
        ModuleCandidate candidate,
        IReadOnlyDictionary<string, ModuleCandidate> candidates,
        IReadOnlySet<string> loadedIds,
        IReadOnlySet<string> failedIds)
    {
        foreach (var dependency in candidate.Manifest.Dependencies ?? [])
        {
            if (!candidates.TryGetValue(dependency.Id, out var dependencyCandidate))
            {
                return DependencyEvaluation.Failed(
                    $"Benötigte Extension '{dependency.Id}' ist nicht installiert oder nicht kompatibel.");
            }

            if (!MeetsMinimumVersion(
                    dependencyCandidate.Manifest.Version,
                    dependency.MinimumVersion))
            {
                return DependencyEvaluation.Failed(
                    $"Extension '{dependency.Id}' erfüllt die benötigte Mindestversion "
                    + $"{dependency.MinimumVersion} nicht.");
            }

            if (failedIds.Contains(dependency.Id))
            {
                return DependencyEvaluation.Failed(
                    $"Benötigte Extension '{dependency.Id}' konnte nicht geladen werden.");
            }

            if (!loadedIds.Contains(dependency.Id))
            {
                return DependencyEvaluation.Waiting();
            }
        }

        return DependencyEvaluation.Ready();
    }

    private static bool MeetsMinimumVersion(
        string installedVersion,
        string? minimumVersion)
    {
        if (string.IsNullOrWhiteSpace(minimumVersion))
        {
            return true;
        }

        return Version.TryParse(installedVersion, out var installed)
               && Version.TryParse(minimumVersion, out var minimum)
               && installed >= minimum;
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
                && typeof(IExtensionModule).IsAssignableFrom(type))
            .ToArray();

        return moduleTypes.Length switch
        {
            1 => moduleTypes[0],
            0 => throw new InvalidOperationException(
                "Die Assembly enthält kein IExtensionModule."),
            _ => throw new InvalidOperationException(
                $"Es wurde genau ein IExtensionModule erwartet, gefunden: {moduleTypes.Length}.")
        };
    }

    private static string ResolveModuleAssemblyPath(
        string moduleDirectory,
        string assemblyRelativePath)
    {
        if (Path.IsPathRooted(assemblyRelativePath))
        {
            throw new InvalidDataException(
                "Der Assembly-Pfad muss relativ zum Modulverzeichnis sein.");
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

        ValidateUniqueValues(
            manifest.Capabilities,
            "Capability");
        ValidateUniqueValues(
            manifest.Permissions,
            "Permission");
        ValidateDependencies(manifest);
    }

    private static void ValidateUniqueValues(
        IEnumerable<string>? values,
        string valueName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"{valueName} darf nicht leer sein.");
            }

            if (!seen.Add(value.Trim()))
            {
                throw new InvalidDataException(
                    $"{valueName} '{value}' ist mehrfach deklariert.");
            }
        }
    }

    private static void ValidateCapabilityProviderRegistrations(
        IExtensionModule module,
        ModuleManifest manifest,
        ExtensionServiceCollection extensionServices)
    {
        var declaredCapabilities = new HashSet<string>(
            NormalizeValues(manifest.Capabilities),
            StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var registrations = extensionServices.AddedDescriptors
            .Where(descriptor =>
                descriptor.ServiceType == typeof(ExtensionCapabilityProviderRegistration))
            .Select(
                descriptor => descriptor.ImplementationInstance
                    as ExtensionCapabilityProviderRegistration
                    ?? throw new InvalidDataException(
                        "Capability-Provider-Registrierungen müssen als feste Metadateninstanz registriert werden."))
            .ToArray();

        foreach (var registration in registrations)
        {
            if (!string.Equals(
                    registration.ExtensionId,
                    module.Id,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Capability-Provider '{registration.CapabilityId}' ist Extension "
                    + $"'{registration.ExtensionId}' zugeordnet, geladen wird aber '{module.Id}'.");
            }

            if (!declaredCapabilities.Contains(registration.CapabilityId))
            {
                throw new InvalidDataException(
                    $"Capability-Provider '{registration.CapabilityId}' wurde registriert, "
                    + "ist aber nicht im module.json deklariert.");
            }

            if (!registration.ContractType.IsAssignableFrom(registration.ImplementationType))
            {
                throw new InvalidDataException(
                    $"Provider '{registration.ImplementationType.FullName}' implementiert "
                    + $"'{registration.ContractType.FullName}' nicht.");
            }

            var implementationRegistration = extensionServices.AddedDescriptors
                .FirstOrDefault(descriptor =>
                    descriptor.ServiceType == registration.ImplementationType
                    && descriptor.Lifetime == registration.Lifetime);

            if (implementationRegistration is null)
            {
                throw new InvalidDataException(
                    $"Provider '{registration.ImplementationType.FullName}' wurde nicht mit "
                    + $"der deklarierten Lebensdauer '{registration.Lifetime}' registriert.");
            }

            var key = string.Join(
                "|",
                registration.CapabilityId,
                registration.ContractType.AssemblyQualifiedName,
                registration.ImplementationType.AssemblyQualifiedName);

            if (!seen.Add(key))
            {
                throw new InvalidDataException(
                    $"Capability-Provider '{registration.CapabilityId}' für "
                    + $"'{registration.ImplementationType.FullName}' ist mehrfach registriert.");
            }
        }
    }

    private static string[] NormalizeValues(IEnumerable<string>? values)
    {
        return (values ?? [])
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToArray();
    }

    private static void ValidateDependencies(ModuleManifest manifest)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dependency in manifest.Dependencies ?? [])
        {
            if (string.IsNullOrWhiteSpace(dependency.Id))
            {
                throw new InvalidDataException("Dependency-ID darf nicht leer sein.");
            }

            if (string.Equals(
                    dependency.Id,
                    manifest.Id,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Eine Extension darf nicht von sich selbst abhängen.");
            }

            if (!seen.Add(dependency.Id.Trim()))
            {
                throw new InvalidDataException(
                    $"Dependency '{dependency.Id}' ist mehrfach deklariert.");
            }

            if (!string.IsNullOrWhiteSpace(dependency.MinimumVersion)
                && !Version.TryParse(dependency.MinimumVersion, out _))
            {
                throw new InvalidDataException(
                    $"minimumVersion für Dependency '{dependency.Id}' ist ungültig.");
            }
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

    private sealed record ModuleCandidate(
        string Directory,
        ModuleManifest Manifest);

    private enum DependencyState
    {
        Ready,
        Waiting,
        Failed
    }

    private sealed record DependencyEvaluation(
        DependencyState State,
        string? Message)
    {
        public static DependencyEvaluation Ready() =>
            new(DependencyState.Ready, null);

        public static DependencyEvaluation Waiting() =>
            new(DependencyState.Waiting, null);

        public static DependencyEvaluation Failed(string message) =>
            new(DependencyState.Failed, message);
    }
}
