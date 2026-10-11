using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Formats;
using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Core.Media.Formats;
using Elbwald.DesktopTools.Core.Modules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class MediaFormatRegistryTests
{
    [Fact]
    public void FindByFilePath_BuiltInProvider_MatchesCaseInsensitively()
    {
        var registry = CreateRegistry(
            [new CoreImageFormatProvider()]);

        var providers = registry.FindByFilePath("/media/PHOTO.JPEG");

        var provider = Assert.Single(providers);
        Assert.Equal("image.core-compat", provider.FormatId);
        Assert.Equal(MediaFileType.Image, registry.DetectMediaType("/media/PHOTO.JPEG"));
    }

    [Fact]
    public void FindByFilePath_ExtensionProvider_IsDiscoveredThroughCapabilityRegistry()
    {
        var webpProvider = new WebpFormatProvider();
        var extensionRegistry = CreateExtensionRegistry(
            webpProvider,
            new ExtensionDescriptor(
                "elbwald.image.webp",
                "WebP",
                string.Empty,
                "1.0.0",
                2,
                [MediaCapabilityIds.FormatProvider, "media.image.webp.read"],
                []));
        var registry = new MediaFormatRegistry(extensionRegistry, []);

        var providers = registry.FindByFilePath("photo.WEBP");

        var provider = Assert.Single(providers);
        Assert.Same(webpProvider, provider);
        Assert.Equal(MediaFileType.Image, registry.DetectMediaType("photo.webp"));
        Assert.Single(registry.FindByFormatId("IMAGE.WEBP"));
    }

    [Fact]
    public void DetectMediaType_ProvidersDisagree_ReturnsUnknown()
    {
        var registry = CreateRegistry(
            [
                new StaticFormatProvider("image.foo", MediaFileType.Image, [".foo"]),
                new StaticFormatProvider("audio.foo", MediaFileType.Audio, ["foo"])
            ]);

        var providers = registry.FindByFilePath("sample.foo");

        Assert.Equal(2, providers.Count);
        Assert.Equal(MediaFileType.Unknown, registry.DetectMediaType("sample.foo"));
    }

    [Fact]
    public void DetectMediaType_NoProvider_ReturnsUnknown()
    {
        var registry = CreateRegistry(
            [new CoreImageFormatProvider()]);

        Assert.Empty(registry.FindByFilePath("notes.txt"));
        Assert.Equal(MediaFileType.Unknown, registry.DetectMediaType("notes.txt"));
    }

    [Fact]
    public void Providers_InvalidProviderWithoutExtensions_ThrowsUsefulError()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CreateRegistry(
                [new StaticFormatProvider("image.invalid", MediaFileType.Image, [])]));

        Assert.Contains("keine Dateiendungen", exception.Message);
    }

    private static MediaFormatRegistry CreateRegistry(
        IEnumerable<IMediaFormatProvider> builtInProviders)
    {
        return new MediaFormatRegistry(
            new ExtensionRegistry(
                new DictionaryServiceProvider(),
                [],
                []),
            builtInProviders);
    }

    private static ExtensionRegistry CreateExtensionRegistry(
        WebpFormatProvider provider,
        ExtensionDescriptor descriptor)
    {
        return new ExtensionRegistry(
            new DictionaryServiceProvider(
                (typeof(WebpFormatProvider), provider)),
            [descriptor],
            [
                new ExtensionCapabilityProviderRegistration(
                    descriptor.Id,
                    MediaCapabilityIds.FormatProvider,
                    typeof(IMediaFormatProvider),
                    typeof(WebpFormatProvider),
                    ServiceLifetime.Singleton)
            ]);
    }

    private sealed class WebpFormatProvider : IMediaFormatProvider
    {
        public string FormatId => "image.webp";

        public MediaFileType MediaType => MediaFileType.Image;

        public IReadOnlyCollection<string> FileExtensions => [".webp"];
    }

    private sealed class StaticFormatProvider(
        string formatId,
        MediaFileType mediaType,
        IReadOnlyCollection<string> extensions) : IMediaFormatProvider
    {
        public string FormatId { get; } = formatId;

        public MediaFileType MediaType { get; } = mediaType;

        public IReadOnlyCollection<string> FileExtensions { get; } = extensions;
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
