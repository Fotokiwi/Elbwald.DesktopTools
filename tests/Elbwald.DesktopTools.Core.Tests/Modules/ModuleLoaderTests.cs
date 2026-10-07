using Elbwald.DesktopTools.Core.Modules;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Modules;

public sealed class ModuleLoaderTests
{
    private static readonly Version HostVersion = new(0, 0, 1);

    [Fact]
    public void LoadModules_LoadsValidModule()
    {
        using var environment = TestModuleEnvironment.Create();
        var registry = new ModuleRegistry();
        var loader = new ModuleLoader(registry);

        var results = loader.LoadModules(environment.ModulesRoot, HostVersion);

        var result = Assert.Single(results);
        Assert.True(result.Success, result.Message);
        Assert.Equal("elbwald.testmodule", result.ModuleId);

        var module = Assert.Single(registry.Modules);
        Assert.Equal("elbwald.testmodule", module.Id);
        Assert.Equal("Test Module", module.GetDescriptor().Name);
    }

    [Fact]
    public void LoadModules_CreatesMissingRootAndReturnsNoModules()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Elbwald.DesktopTools.Tests",
            Guid.NewGuid().ToString("N"),
            "Modules");

        try
        {
            var loader = new ModuleLoader(new ModuleRegistry());

            var results = loader.LoadModules(root, HostVersion);

            Assert.Empty(results);
            Assert.True(Directory.Exists(root));
        }
        finally
        {
            var testRoot = Directory.GetParent(root)?.FullName;
            if (testRoot is not null && Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadModules_DisabledModule_ReturnsFailureAndDoesNotRegister()
    {
        using var environment = TestModuleEnvironment.Create();
        environment.UpdateManifest(manifest => manifest["enabled"] = false);

        var registry = new ModuleRegistry();
        var loader = new ModuleLoader(registry);

        var result = Assert.Single(loader.LoadModules(environment.ModulesRoot, HostVersion));

        Assert.False(result.Success);
        Assert.Contains("deaktiviert", result.Message.ToLowerInvariant());
        Assert.Empty(registry.Modules);
    }

    [Fact]
    public void LoadModules_UnsupportedApiVersion_ReturnsFailure()
    {
        using var environment = TestModuleEnvironment.Create();
        environment.UpdateManifest(manifest => manifest["apiVersion"] = 999);

        var loader = new ModuleLoader(new ModuleRegistry());

        var result = Assert.Single(loader.LoadModules(environment.ModulesRoot, HostVersion));

        Assert.False(result.Success);
        Assert.Contains("api-version", result.Message.ToLowerInvariant());
    }

    [Fact]
    public void LoadModules_HostVersionTooOld_ReturnsFailure()
    {
        using var environment = TestModuleEnvironment.Create();
        environment.UpdateManifest(manifest => manifest["minimumHostVersion"] = "9.0.0");

        var loader = new ModuleLoader(new ModuleRegistry());

        var result = Assert.Single(loader.LoadModules(environment.ModulesRoot, HostVersion));

        Assert.False(result.Success);
        Assert.Contains("mindestens host-version", result.Message.ToLowerInvariant());
    }

    [Fact]
    public void LoadModules_MissingAssembly_ReturnsFailure()
    {
        using var environment = TestModuleEnvironment.Create();
        environment.UpdateManifest(manifest => manifest["assembly"] = "missing.dll");

        var loader = new ModuleLoader(new ModuleRegistry());

        var result = Assert.Single(loader.LoadModules(environment.ModulesRoot, HostVersion));

        Assert.False(result.Success);
        Assert.Contains("nicht gefunden", result.Message.ToLowerInvariant());
    }

    [Fact]
    public void LoadModules_AssemblyOutsideModuleDirectory_ReturnsFailure()
    {
        using var environment = TestModuleEnvironment.Create();
        environment.UpdateManifest(manifest => manifest["assembly"] = "../outside.dll");

        var loader = new ModuleLoader(new ModuleRegistry());

        var result = Assert.Single(loader.LoadModules(environment.ModulesRoot, HostVersion));

        Assert.False(result.Success);
        Assert.Contains("außerhalb", result.Message.ToLowerInvariant());
    }

    [Fact]
    public void LoadModules_MissingManifest_ReturnsFailure()
    {
        using var environment = TestModuleEnvironment.Create();
        File.Delete(environment.ManifestPath);

        var loader = new ModuleLoader(new ModuleRegistry());

        var result = Assert.Single(loader.LoadModules(environment.ModulesRoot, HostVersion));

        Assert.False(result.Success);
        Assert.Contains("module.json", result.Message);
    }

    [Fact]
    public void LoadModules_InvalidJson_ReturnsFailureInsteadOfThrowing()
    {
        using var environment = TestModuleEnvironment.Create();
        File.WriteAllText(environment.ManifestPath, "{ invalid json }");

        var registry = new ModuleRegistry();
        var loader = new ModuleLoader(registry);

        var result = Assert.Single(loader.LoadModules(environment.ModulesRoot, HostVersion));

        Assert.False(result.Success);
        Assert.Empty(registry.Modules);
    }
}
