using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Metadata;
using Elbwald.DesktopTools.Core.Media.Formats;
using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Core.Media.Metadata;
using Elbwald.DesktopTools.Core.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class ImageMetadataRegistryTests
{
    [Fact]
    public void CoreProvider_OnlyClaimsFormatsStillOwnedByCoreCompatibilityProvider()
    {
        var inner = new TestMetadataProvider("inner", 0, ".jpg");
        var provider = new CoreImageMetadataProvider(
            inner,
            new CoreImageFormatProvider());

        Assert.True(provider.CanRead(CreateImage("photo.jpg")));
        Assert.True(provider.CanRead(CreateImage("photo.png")));
        Assert.True(provider.CanRead(CreateImage("photo.dng"))); // temporary RAW compatibility bridge
        Assert.False(provider.CanRead(CreateImage("photo.tiff")));
        Assert.False(provider.CanRead(CreateImage("photo.heic")));
        Assert.False(provider.CanRead(CreateImage("photo.webp")));
    }

    [Fact]
    public void FindReaders_OrdersHigherPriorityProviderFirst()
    {
        var builtIn = new TestMetadataProvider("core", 0, ".jpg");
        var extensionProvider = new ExtensionMetadataProvider(
            new TestMetadataProvider("extension", 100, ".jpg"));
        var descriptor = CreateExtension();
        var extensionRegistry = CreateExtensionRegistry(
            extensionProvider,
            descriptor);
        var registry = new ImageMetadataRegistry(
            extensionRegistry,
            [builtIn]);

        var readers = registry.FindReaders(CreateImage("photo.jpg"));

        Assert.Equal(2, readers.Count);
        Assert.Equal("extension", readers[0].ProviderId);
        Assert.Equal("core", readers[1].ProviderId);
    }

    [Fact]
    public void ActivationChange_RemovesAndRestoresExtensionProviderImmediately()
    {
        var activation = new TestActivationService();
        var extensionProvider = new ExtensionMetadataProvider(
            new TestMetadataProvider("extension", 100, ".webp"));
        var descriptor = CreateExtension();
        var extensionRegistry = CreateExtensionRegistry(
            extensionProvider,
            descriptor,
            activation);
        var registry = new ImageMetadataRegistry(
            extensionRegistry,
            []);
        var file = CreateImage("photo.webp");

        Assert.Single(registry.FindReaders(file));

        activation.SetEnabled(descriptor.Id, false);
        Assert.Empty(registry.FindReaders(file));

        activation.SetEnabled(descriptor.Id, true);
        Assert.Single(registry.FindReaders(file));
    }

    [Fact]
    public async Task RegistryReader_NoMatchingProvider_ReturnsUnsupportedFormat()
    {
        var registry = new ImageMetadataRegistry(
            new ExtensionRegistry(
                new DictionaryServiceProvider(),
                [],
                []),
            [new TestMetadataProvider("core", 0, ".jpg")]);
        var reader = new RegistryImageMetadataReader(registry);

        var result = await reader.ReadAsync(CreateImage("photo.webp"));

        Assert.Equal(ImageMetadataReadState.UnsupportedFormat, result.State);
        Assert.Contains("kein aktiver Metadaten-Reader", result.ErrorMessage);
    }

    [Fact]
    public async Task RegistryReader_PreferredProviderReportsUnsupported_FallsBackToNextReader()
    {
        var preferred = new TestMetadataProvider(
            "preferred",
            100,
            ".jpg",
            ImageMetadataReadState.UnsupportedFormat);
        var fallback = new TestMetadataProvider(
            "fallback",
            0,
            ".jpg",
            ImageMetadataReadState.Success);
        var registry = new ImageMetadataRegistry(
            new ExtensionRegistry(
                new DictionaryServiceProvider(),
                [],
                []),
            [fallback, preferred]);
        var reader = new RegistryImageMetadataReader(registry);

        var result = await reader.ReadAsync(CreateImage("photo.jpg"));

        Assert.Equal(ImageMetadataReadState.Success, result.State);
        Assert.Equal(1, preferred.ReadCount);
        Assert.Equal(1, fallback.ReadCount);
    }

    private static ExtensionDescriptor CreateExtension()
    {
        return new ExtensionDescriptor(
            "elbwald.test.metadata",
            "Test Metadata",
            string.Empty,
            "1.0.0",
            2,
            [MetadataCapabilityIds.ImageReaderProvider],
            []);
    }

    private static ExtensionRegistry CreateExtensionRegistry(
        ExtensionMetadataProvider provider,
        ExtensionDescriptor descriptor,
        IExtensionActivationService? activation = null)
    {
        var serviceProvider = new DictionaryServiceProvider(
            (typeof(ExtensionMetadataProvider), provider));
        var registrations = new[]
        {
            new ExtensionCapabilityProviderRegistration(
                descriptor.Id,
                MetadataCapabilityIds.ImageReaderProvider,
                typeof(IImageMetadataProvider),
                typeof(ExtensionMetadataProvider),
                ServiceLifetime.Singleton)
        };

        return activation is null
            ? new ExtensionRegistry(
                serviceProvider,
                [descriptor],
                registrations)
            : new ExtensionRegistry(
                serviceProvider,
                [descriptor],
                registrations,
                activation);
    }

    private static MediaFile CreateImage(string fileName)
    {
        var path = Path.Combine("/media", fileName);

        return new MediaFile(
            path,
            fileName,
            Path.GetExtension(fileName),
            100,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            MediaFileType.Image,
            IsSymbolicLink: false);
    }

    private class TestMetadataProvider : IImageMetadataProvider
    {
        private readonly string _extension;
        private readonly ImageMetadataReadState _state;

        public TestMetadataProvider(
            string providerId,
            int priority,
            string extension,
            ImageMetadataReadState state = ImageMetadataReadState.Success)
        {
            ProviderId = providerId;
            Priority = priority;
            _extension = extension;
            _state = state;
        }

        public string ProviderId { get; }

        public int Priority { get; }

        public int ReadCount { get; private set; }

        public bool CanRead(MediaFile file)
        {
            return string.Equals(
                file.Extension,
                _extension,
                StringComparison.OrdinalIgnoreCase);
        }

        public Task<ImageMetadataReadResult> ReadAsync(
            MediaFile file,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;

            return Task.FromResult(
                new ImageMetadataReadResult(
                    _state,
                    _state == ImageMetadataReadState.Success
                        ? new ImageMetadata(
                            10,
                            10,
                            null,
                            null,
                            null,
                            null,
                            null,
                            HasExif: false,
                            HasGps: false,
                            null,
                            null)
                        : null));
        }
    }

    private sealed class ExtensionMetadataProvider(TestMetadataProvider inner)
        : IImageMetadataProvider
    {
        public string ProviderId => inner.ProviderId;

        public int Priority => inner.Priority;

        public bool CanRead(MediaFile file) => inner.CanRead(file);

        public Task<ImageMetadataReadResult> ReadAsync(
            MediaFile file,
            CancellationToken cancellationToken = default)
        {
            return inner.ReadAsync(file, cancellationToken);
        }
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
