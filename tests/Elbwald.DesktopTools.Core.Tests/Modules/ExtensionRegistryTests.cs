using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Core.Modules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Modules;

public sealed class ExtensionRegistryTests
{
    [Fact]
    public void HasCapabilityAndFindExtensions_UseManifestCapabilitiesCaseInsensitively()
    {
        var extension = CreateExtension(
            "elbwald.webp",
            "media.image.webp.read",
            "media.image.webp.thumbnail");
        var registry = new ExtensionRegistry(
            new DictionaryServiceProvider(),
            [extension],
            []);

        Assert.True(registry.HasCapability("MEDIA.IMAGE.WEBP.READ"));
        Assert.False(registry.HasCapability("media.image.tiff.read"));

        var match = Assert.Single(
            registry.FindExtensionsByCapability("media.image.webp.thumbnail"));
        Assert.Equal("elbwald.webp", match.Id);
    }

    [Fact]
    public void GetProviders_ReturnsAllMatchingProvidersInStableExtensionOrder()
    {
        var providerA = new TestProvider("A");
        var providerB = new TestProvider("B");
        var serviceProvider = new DictionaryServiceProvider(
            (typeof(ProviderA), new ProviderA(providerA)),
            (typeof(ProviderB), new ProviderB(providerB)));

        var registry = new ExtensionRegistry(
            serviceProvider,
            [
                CreateExtension("elbwald.b", "test.read"),
                CreateExtension("elbwald.a", "test.read")
            ],
            [
                new ExtensionCapabilityProviderRegistration(
                    "elbwald.b",
                    "test.read",
                    typeof(ITestProvider),
                    typeof(ProviderB),
                    ServiceLifetime.Singleton),
                new ExtensionCapabilityProviderRegistration(
                    "elbwald.a",
                    "test.read",
                    typeof(ITestProvider),
                    typeof(ProviderA),
                    ServiceLifetime.Singleton)
            ]);

        var providers = registry.GetProviders<ITestProvider>("test.read");

        Assert.Equal(2, providers.Count);
        Assert.Equal("A", providers[0].Name);
        Assert.Equal("B", providers[1].Name);
    }


    [Fact]
    public void DisabledExtension_IsHiddenFromCapabilitiesAndProviders()
    {
        var activation = new TestActivationService();
        var provider = new ProviderA(new TestProvider("A"));
        var extension = CreateExtension("elbwald.a", "test.read");
        var registry = new ExtensionRegistry(
            new DictionaryServiceProvider((typeof(ProviderA), provider)),
            [extension],
            [
                new ExtensionCapabilityProviderRegistration(
                    extension.Id,
                    "test.read",
                    typeof(ITestProvider),
                    typeof(ProviderA),
                    ServiceLifetime.Singleton)
            ],
            activation);

        Assert.True(registry.HasCapability("test.read"));
        Assert.Single(registry.GetProviders<ITestProvider>("test.read"));

        activation.SetEnabled(extension.Id, false);

        Assert.False(registry.HasCapability("test.read"));
        Assert.Empty(registry.FindExtensionsByCapability("test.read"));
        Assert.Empty(registry.GetProviders<ITestProvider>("test.read"));
        Assert.Single(registry.Extensions);
    }

    [Fact]
    public void GetProviders_NoTypedProvider_ReturnsEmptyList()
    {
        var registry = new ExtensionRegistry(
            new DictionaryServiceProvider(),
            [CreateExtension("elbwald.feature", "feature.only")],
            []);

        Assert.Empty(registry.GetProviders<ITestProvider>("feature.only"));
    }

    [Fact]
    public void GetProviders_ProviderCannotBeResolved_ThrowsUsefulError()
    {
        var registry = new ExtensionRegistry(
            new DictionaryServiceProvider(),
            [CreateExtension("elbwald.a", "test.read")],
            [
                new ExtensionCapabilityProviderRegistration(
                    "elbwald.a",
                    "test.read",
                    typeof(ITestProvider),
                    typeof(ProviderA),
                    ServiceLifetime.Singleton)
            ]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => registry.GetProviders<ITestProvider>("test.read"));

        Assert.Contains("konnte nicht", exception.Message);
        Assert.Contains("test.read", exception.Message);
    }

    private static ExtensionDescriptor CreateExtension(
        string id,
        params string[] capabilities)
    {
        return new ExtensionDescriptor(
            id,
            id,
            string.Empty,
            "1.0.0",
            2,
            capabilities,
            []);
    }

    private interface ITestProvider
    {
        string Name { get; }
    }

    private sealed class TestProvider(string name)
    {
        public string Name { get; } = name;
    }

    private sealed class ProviderA(TestProvider provider) : ITestProvider
    {
        public string Name => provider.Name;
    }

    private sealed class ProviderB(TestProvider provider) : ITestProvider
    {
        public string Name => provider.Name;
    }

    private sealed class TestActivationService : IExtensionActivationService
    {
        private readonly HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<ExtensionActivationChangedEventArgs>? ActivationChanged;

        public bool IsEnabled(string extensionId) => !_disabled.Contains(extensionId);

        public bool SetEnabled(string extensionId, bool enabled)
        {
            var changed = enabled
                ? _disabled.Remove(extensionId)
                : _disabled.Add(extensionId);

            if (changed)
            {
                ActivationChanged?.Invoke(
                    this,
                    new ExtensionActivationChangedEventArgs(extensionId, enabled));
            }

            return changed;
        }
    }

    private sealed class DictionaryServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services;

        public DictionaryServiceProvider(params (Type Type, object Instance)[] services)
        {
            _services = services.ToDictionary(
                service => service.Type,
                service => service.Instance);
        }

        public object? GetService(Type serviceType)
        {
            return _services.GetValueOrDefault(serviceType);
        }
    }
}
