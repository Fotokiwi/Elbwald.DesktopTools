using System.Text.Json.Nodes;
using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Core.Modules;
using Elbwald.DesktopTools.Core.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Modules;

public sealed class ModuleLoaderTests
{
    private static readonly Version HostVersion = new(0, 0, 1);

    [Fact]
    public void LoadModules_LoadsValidModuleAndRegistersServices()
    {
        using var environment = TestModuleEnvironment.Create();
        var registry = new ModuleRegistry();
        var loader = new ModuleLoader(registry);
        var services = new ServiceCollection();

        var results = loader.LoadModules(
            environment.ModulesRoot,
            HostVersion,
            services);

        var result = Assert.Single(results);
        Assert.True(result.Success, result.Message);
        Assert.Equal("elbwald.testmodule", result.ModuleId);
        Assert.Contains("Module API v2", result.Message);

        var module = Assert.Single(registry.Modules);
        Assert.Equal("elbwald.testmodule", module.Id);
        Assert.Equal("Test Module", module.GetDescriptor().Name);

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType.FullName
                == "Elbwald.DesktopTools.TestModule.TestToolModule");

        var extension = Assert.Single(
            services
                .Where(descriptor => descriptor.ServiceType == typeof(ExtensionDescriptor))
                .Select(descriptor => Assert.IsType<ExtensionDescriptor>(descriptor.ImplementationInstance)));
        Assert.Equal("elbwald.testmodule", extension.Id);
        Assert.Contains("test.service-registration", extension.Capabilities);

        var providerRegistration = Assert.Single(
            services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(ExtensionCapabilityProviderRegistration))
                .Select(descriptor =>
                    Assert.IsType<ExtensionCapabilityProviderRegistration>(
                        descriptor.ImplementationInstance)));
        Assert.Equal("elbwald.testmodule", providerRegistration.ExtensionId);
        Assert.Equal("test.service-registration", providerRegistration.CapabilityId);
        Assert.Equal(
            "Elbwald.DesktopTools.TestModule.TestCapabilityProvider",
            providerRegistration.ImplementationType.FullName);
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

            var results = loader.LoadModules(
                root,
                HostVersion,
                new ServiceCollection());

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

        var result = Assert.Single(
            loader.LoadModules(
                environment.ModulesRoot,
                HostVersion,
                new ServiceCollection()));

        Assert.False(result.Success);
        Assert.Contains("deaktiviert", result.Message.ToLowerInvariant());
        Assert.Empty(registry.Modules);
    }

    [Fact]
    public void LoadModules_LegacyApiVersion_ReturnsFailure()
    {
        using var environment = TestModuleEnvironment.Create();
        environment.UpdateManifest(manifest => manifest["apiVersion"] = 1);

        var loader = new ModuleLoader(new ModuleRegistry());

        var result = Assert.Single(
            loader.LoadModules(
                environment.ModulesRoot,
                HostVersion,
                new ServiceCollection()));

        Assert.False(result.Success);
        Assert.Contains("api-version 1", result.Message.ToLowerInvariant());
        Assert.Contains("version 2", result.Message.ToLowerInvariant());
    }

    [Fact]
    public void LoadModules_UnsupportedApiVersion_ReturnsFailure()
    {
        using var environment = TestModuleEnvironment.Create();
        environment.UpdateManifest(manifest => manifest["apiVersion"] = 999);

        var loader = new ModuleLoader(new ModuleRegistry());

        var result = Assert.Single(
            loader.LoadModules(
                environment.ModulesRoot,
                HostVersion,
                new ServiceCollection()));

        Assert.False(result.Success);
        Assert.Contains("api-version", result.Message.ToLowerInvariant());
    }

    [Fact]
    public void LoadModules_HostVersionTooOld_ReturnsFailure()
    {
        using var environment = TestModuleEnvironment.Create();
        environment.UpdateManifest(manifest => manifest["minimumHostVersion"] = "9.0.0");

        var loader = new ModuleLoader(new ModuleRegistry());

        var result = Assert.Single(
            loader.LoadModules(
                environment.ModulesRoot,
                HostVersion,
                new ServiceCollection()));

        Assert.False(result.Success);
        Assert.Contains("mindestens host-version", result.Message.ToLowerInvariant());
    }

    [Fact]
    public void LoadModules_MissingAssembly_ReturnsFailure()
    {
        using var environment = TestModuleEnvironment.Create();
        environment.UpdateManifest(manifest => manifest["assembly"] = "missing.dll");

        var loader = new ModuleLoader(new ModuleRegistry());

        var result = Assert.Single(
            loader.LoadModules(
                environment.ModulesRoot,
                HostVersion,
                new ServiceCollection()));

        Assert.False(result.Success);
        Assert.Contains("nicht gefunden", result.Message.ToLowerInvariant());
    }

    [Fact]
    public void LoadModules_AssemblyOutsideModuleDirectory_ReturnsFailure()
    {
        using var environment = TestModuleEnvironment.Create();
        environment.UpdateManifest(manifest => manifest["assembly"] = "../outside.dll");

        var loader = new ModuleLoader(new ModuleRegistry());

        var result = Assert.Single(
            loader.LoadModules(
                environment.ModulesRoot,
                HostVersion,
                new ServiceCollection()));

        Assert.False(result.Success);
        Assert.Contains("außerhalb", result.Message.ToLowerInvariant());
    }

    [Fact]
    public void LoadModules_MissingDependency_ReturnsFailureBeforeAssemblyLoad()
    {
        using var environment = TestModuleEnvironment.Create();
        environment.UpdateManifest(
            manifest =>
                manifest["dependencies"] = new JsonArray(
                    new JsonObject
                    {
                        ["id"] = "elbwald.missing",
                        ["minimumVersion"] = "1.0.0"
                    }));

        var registry = new ModuleRegistry();
        var loader = new ModuleLoader(registry);
        var services = new ServiceCollection();

        var result = Assert.Single(
            loader.LoadModules(
                environment.ModulesRoot,
                HostVersion,
                services));

        Assert.False(result.Success);
        Assert.Contains("elbwald.missing", result.Message);
        Assert.Empty(registry.Modules);
        Assert.DoesNotContain(
            services,
            descriptor =>
                descriptor.ServiceType.FullName
                == "Elbwald.DesktopTools.TestModule.TestToolModule");
    }

    [Fact]
    public void LoadModules_ProviderCapabilityNotDeclaredInManifest_RollsBackRegistrations()
    {
        using var environment = TestModuleEnvironment.Create();
        environment.UpdateManifest(
            manifest => manifest["capabilities"] = new JsonArray());

        var registry = new ModuleRegistry();
        var loader = new ModuleLoader(registry);
        var services = new ServiceCollection();

        var result = Assert.Single(
            loader.LoadModules(
                environment.ModulesRoot,
                HostVersion,
                services));

        Assert.False(result.Success);
        Assert.Contains("nicht im module.json deklariert", result.Message);
        Assert.Empty(registry.Modules);
        Assert.DoesNotContain(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(ExtensionCapabilityProviderRegistration));
        Assert.DoesNotContain(
            services,
            descriptor =>
                descriptor.ServiceType.FullName
                == "Elbwald.DesktopTools.TestModule.TestCapabilityProvider");
    }

    [Fact]
    public void LoadModules_DuplicateCapability_ReturnsFailure()
    {
        using var environment = TestModuleEnvironment.Create();
        environment.UpdateManifest(
            manifest =>
                manifest["capabilities"] = new JsonArray(
                    JsonValue.Create("media.image.webp.read"),
                    JsonValue.Create("media.image.webp.read")));

        var loader = new ModuleLoader(new ModuleRegistry());

        var result = Assert.Single(
            loader.LoadModules(
                environment.ModulesRoot,
                HostVersion,
                new ServiceCollection()));

        Assert.False(result.Success);
        Assert.Contains("capability", result.Message.ToLowerInvariant());
        Assert.Contains("mehrfach", result.Message.ToLowerInvariant());
    }

    [Fact]
    public void LoadModules_MissingManifest_ReturnsFailure()
    {
        using var environment = TestModuleEnvironment.Create();
        File.Delete(environment.ManifestPath);

        var loader = new ModuleLoader(new ModuleRegistry());

        var result = Assert.Single(
            loader.LoadModules(
                environment.ModulesRoot,
                HostVersion,
                new ServiceCollection()));

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

        var result = Assert.Single(
            loader.LoadModules(
                environment.ModulesRoot,
                HostVersion,
                new ServiceCollection()));

        Assert.False(result.Success);
        Assert.Empty(registry.Modules);
    }
}
