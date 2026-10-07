using System.Text.Json;
using System.Text.Json.Nodes;

namespace Elbwald.DesktopTools.Core.Tests.Support;

internal sealed class TestModuleEnvironment : IDisposable
{
    private readonly string _rootDirectory;

    private TestModuleEnvironment(string rootDirectory)
    {
        _rootDirectory = rootDirectory;
        ModulesRoot = Path.Combine(rootDirectory, "Modules");
        ModuleDirectory = Path.Combine(ModulesRoot, "TestModule");
    }

    public string ModulesRoot { get; }

    public string ModuleDirectory { get; }

    public string ManifestPath => Path.Combine(ModuleDirectory, "module.json");

    public static TestModuleEnvironment Create()
    {
        var sourceDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "TestModules",
            "TestModule");

        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Testmodul wurde nicht in den Test-Ausgabeordner kopiert: {sourceDirectory}");
        }

        var rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "Elbwald.DesktopTools.Tests",
            Guid.NewGuid().ToString("N"));

        var environment = new TestModuleEnvironment(rootDirectory);
        Directory.CreateDirectory(environment.ModuleDirectory);
        CopyDirectory(sourceDirectory, environment.ModuleDirectory);

        return environment;
    }

    public void UpdateManifest(Action<JsonObject> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        var manifest = JsonNode.Parse(File.ReadAllText(ManifestPath))?.AsObject()
                       ?? throw new InvalidDataException("Test-manifest konnte nicht gelesen werden.");

        update(manifest);

        File.WriteAllText(
            ManifestPath,
            manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_rootDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Test cleanup must not hide the actual test result.
        }
        catch (UnauthorizedAccessException)
        {
            // Same rationale as above; Windows may briefly keep handles open.
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(
                file,
                Path.Combine(destinationDirectory, Path.GetFileName(file)),
                overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            CopyDirectory(
                directory,
                Path.Combine(destinationDirectory, Path.GetFileName(directory)));
        }
    }
}
