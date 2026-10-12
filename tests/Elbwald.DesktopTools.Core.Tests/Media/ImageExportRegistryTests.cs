using Elbwald.DesktopTools.Contracts.Media.Exporting;
using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Core.Media.Exporting;
using Elbwald.DesktopTools.Core.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class ImageExportRegistryTests
{
    [Fact]
    public void BuiltInProvider_IsAvailableByFormatId()
    {
        var builtIn = new TestExportProvider(
            "core.jpeg",
            "image/jpeg",
            "JPEG",
            ".jpg",
            priority: 0);
        var registry = new ImageExportRegistry(
            new ExtensionRegistry(new DictionaryServiceProvider(), [], []),
            [builtIn]);

        var provider = registry.FindByFormatId("IMAGE/JPEG");

        Assert.Same(builtIn, provider);
        Assert.Single(registry.Providers);
    }

    [Fact]
    public void HigherPriorityExtensionProvider_ReplacesSameFormatWhileActive()
    {
        var activation = new TestActivationService();
        var builtIn = new TestExportProvider(
            "core.jpeg",
            "image/jpeg",
            "JPEG",
            ".jpg",
            priority: 0);
        var extensionProvider = new TestExportProvider(
            "extension.jpeg",
            "image/jpeg",
            "JPEG Extension",
            ".jpeg",
            priority: 100);
        var extension = CreateExtension("elbwald.test.jpeg");
        var extensionRegistry = new ExtensionRegistry(
            new DictionaryServiceProvider((typeof(TestExportProvider), extensionProvider)),
            [extension],
            [new ExtensionCapabilityProviderRegistration(
                extension.Id,
                ExportCapabilityIds.ImageExporterProvider,
                typeof(IImageExportProvider),
                typeof(TestExportProvider),
                ServiceLifetime.Singleton)],
            activation);
        var registry = new ImageExportRegistry(extensionRegistry, [builtIn]);

        Assert.Same(extensionProvider, registry.FindByFormatId("image/jpeg"));

        activation.SetEnabled(extension.Id, false);

        Assert.Same(builtIn, registry.FindByFormatId("image/jpeg"));
    }

    [Fact]
    public void ActivationChange_RaisesProvidersChangedAndRestoresProvider()
    {
        var activation = new TestActivationService();
        var extensionProvider = new TestExportProvider(
            "extension.webp",
            "image/webp",
            "WebP",
            ".webp",
            priority: 100);
        var extension = CreateExtension("elbwald.test.webp");
        var extensionRegistry = new ExtensionRegistry(
            new DictionaryServiceProvider((typeof(TestExportProvider), extensionProvider)),
            [extension],
            [new ExtensionCapabilityProviderRegistration(
                extension.Id,
                ExportCapabilityIds.ImageExporterProvider,
                typeof(IImageExportProvider),
                typeof(TestExportProvider),
                ServiceLifetime.Singleton)],
            activation);
        var registry = new ImageExportRegistry(extensionRegistry, []);
        var changes = 0;
        registry.ProvidersChanged += (_, _) => changes++;

        Assert.Same(extensionProvider, registry.FindByFormatId("image/webp"));

        activation.SetEnabled(extension.Id, false);
        Assert.Null(registry.FindByFormatId("image/webp"));

        activation.SetEnabled(extension.Id, true);
        Assert.Same(extensionProvider, registry.FindByFormatId("image/webp"));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void InvalidProviderExtension_IsRejected()
    {
        var invalid = new TestExportProvider(
            "broken",
            "image/test",
            "Broken",
            "test",
            priority: 0);

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ImageExportRegistry(
                new ExtensionRegistry(new DictionaryServiceProvider(), [], []),
                [invalid]));

        Assert.Contains("Dateiendung", exception.Message);
    }

    private static ExtensionDescriptor CreateExtension(string id)
    {
        return new ExtensionDescriptor(
            id,
            id,
            string.Empty,
            "1.0.0",
            2,
            [ExportCapabilityIds.ImageExporterProvider],
            []);
    }

    private sealed class TestExportProvider(
        string providerId,
        string formatId,
        string displayName,
        string fileExtension,
        int priority) : IImageExportProvider
    {
        public string ProviderId { get; } = providerId;
        public string FormatId { get; } = formatId;
        public string DisplayName { get; } = displayName;
        public string FileExtension { get; } = fileExtension;
        public string MimeType => FormatId;
        public int Priority { get; } = priority;
        public bool SupportsResize => true;
        public bool SupportsQuality => true;

        public ImageExportSupport CheckSupport(ImageExportRequest request) =>
            ImageExportSupport.Supported;

        public Task ExportAsync(
            ImageExportRequest request,
            Stream destination,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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

        public object? GetService(Type serviceType) =>
            _services.GetValueOrDefault(serviceType);
    }
}
