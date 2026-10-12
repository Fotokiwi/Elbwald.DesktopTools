using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Thumbnailing;
using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Core.Media.Thumbnailing;
using Elbwald.DesktopTools.Core.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class ImageThumbnailRegistryTests
{
    [Fact]
    public void BuiltInProvider_IsAvailableForMatchingImage()
    {
        var builtIn = new TestThumbnailProvider("core.jpeg", 0, ".jpg");
        var registry = new ImageThumbnailRegistry(
            new ExtensionRegistry(new DictionaryServiceProvider(), [], []),
            [builtIn]);
        var file = CreateFile("photo.jpg", MediaFileType.Image);

        var provider = Assert.Single(registry.FindProviders(file));

        Assert.Same(builtIn, provider);
        Assert.Single(registry.Providers);
    }

    [Fact]
    public void HigherPriorityExtensionProvider_IsFirstWhileActive()
    {
        var activation = new TestActivationService();
        var builtIn = new TestThumbnailProvider("core.webp", 0, ".webp");
        var extensionProvider = new TestThumbnailProvider("extension.webp", 100, ".webp");
        var extension = CreateExtension("elbwald.test.webp");
        var extensionRegistry = new ExtensionRegistry(
            new DictionaryServiceProvider((typeof(TestThumbnailProvider), extensionProvider)),
            [extension],
            [new ExtensionCapabilityProviderRegistration(
                extension.Id,
                ThumbnailCapabilityIds.ImageProvider,
                typeof(IImageThumbnailProvider),
                typeof(TestThumbnailProvider),
                ServiceLifetime.Singleton)],
            activation);
        var registry = new ImageThumbnailRegistry(extensionRegistry, [builtIn]);
        var file = CreateFile("photo.webp", MediaFileType.Image);

        var providers = registry.FindProviders(file);
        Assert.Equal(2, providers.Count);
        Assert.Same(extensionProvider, providers[0]);
        Assert.Same(builtIn, providers[1]);

        activation.SetEnabled(extension.Id, false);

        Assert.Same(builtIn, Assert.Single(registry.FindProviders(file)));
    }

    [Fact]
    public void ActivationChange_RaisesProvidersChanged()
    {
        var activation = new TestActivationService();
        var extensionProvider = new TestThumbnailProvider("extension.webp", 100, ".webp");
        var extension = CreateExtension("elbwald.test.webp");
        var extensionRegistry = new ExtensionRegistry(
            new DictionaryServiceProvider((typeof(TestThumbnailProvider), extensionProvider)),
            [extension],
            [new ExtensionCapabilityProviderRegistration(
                extension.Id,
                ThumbnailCapabilityIds.ImageProvider,
                typeof(IImageThumbnailProvider),
                typeof(TestThumbnailProvider),
                ServiceLifetime.Singleton)],
            activation);
        var registry = new ImageThumbnailRegistry(extensionRegistry, []);
        var changes = 0;
        registry.ProvidersChanged += (_, _) => changes++;

        activation.SetEnabled(extension.Id, false);
        activation.SetEnabled(extension.Id, true);

        Assert.Equal(2, changes);
    }

    [Fact]
    public void NonImage_HasNoThumbnailProviders()
    {
        var builtIn = new TestThumbnailProvider("core.jpeg", 0, ".jpg");
        var registry = new ImageThumbnailRegistry(
            new ExtensionRegistry(new DictionaryServiceProvider(), [], []),
            [builtIn]);

        Assert.Empty(registry.FindProviders(CreateFile("cover.jpg", MediaFileType.Audio)));
    }

    private static MediaFile CreateFile(string fileName, MediaFileType type) =>
        new(
            Path.Combine(Path.GetTempPath(), fileName),
            fileName,
            Path.GetExtension(fileName),
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            type,
            IsSymbolicLink: false);

    private static ExtensionDescriptor CreateExtension(string id) =>
        new(
            id,
            id,
            string.Empty,
            "1.0.0",
            2,
            [ThumbnailCapabilityIds.ImageProvider],
            []);

    private sealed class TestThumbnailProvider(
        string providerId,
        int priority,
        params string[] extensions) : IImageThumbnailProvider
    {
        private readonly HashSet<string> _extensions = new(extensions, StringComparer.OrdinalIgnoreCase);

        public string ProviderId { get; } = providerId;
        public int Priority { get; } = priority;

        public bool CanCreate(MediaFile file) =>
            file.MediaType == MediaFileType.Image && _extensions.Contains(file.Extension);

        public Task<MediaThumbnailResult> CreateAsync(
            MediaFile file,
            int targetWidth,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new MediaThumbnailResult(
                    MediaThumbnailState.Success,
                    [1, 2, 3],
                    "image/png"));
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
