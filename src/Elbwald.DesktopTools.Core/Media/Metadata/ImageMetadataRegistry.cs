using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Metadata;
using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.Core.Media.Metadata;

/// <summary>
/// Runtime registry combining host-owned and active extension metadata readers.
/// Activation changes rebuild the provider snapshot immediately.
/// </summary>
public sealed class ImageMetadataRegistry : IImageMetadataRegistry
{
    private readonly IExtensionRegistry _extensionRegistry;
    private readonly IImageMetadataProvider[] _builtInProviders;
    private IImageMetadataProvider[] _providers;

    public ImageMetadataRegistry(
        IExtensionRegistry extensionRegistry,
        IEnumerable<IImageMetadataProvider> builtInProviders)
    {
        ArgumentNullException.ThrowIfNull(extensionRegistry);
        ArgumentNullException.ThrowIfNull(builtInProviders);

        _extensionRegistry = extensionRegistry;
        _builtInProviders = builtInProviders
            .Select(ValidateProvider)
            .ToArray();
        _providers = BuildSnapshot();

        _extensionRegistry.ActivationChanged += OnActivationChanged;
    }

    public IReadOnlyList<IImageMetadataProvider> Providers => _providers;

    public IReadOnlyList<IImageMetadataProvider> FindReaders(MediaFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.MediaType != MediaFileType.Image)
        {
            return [];
        }

        var providers = _providers;

        return providers
            .Where(provider => provider.CanRead(file))
            .ToArray();
    }

    private void OnActivationChanged(
        object? sender,
        ExtensionActivationChangedEventArgs args)
    {
        _providers = BuildSnapshot();
    }

    private IImageMetadataProvider[] BuildSnapshot()
    {
        var extensionProviders = _extensionRegistry
            .GetProviders<IImageMetadataProvider>(MetadataCapabilityIds.ImageReaderProvider);

        return _builtInProviders
            .Concat(extensionProviders)
            .Select(ValidateProvider)
            .OrderByDescending(provider => provider.Priority)
            .ThenBy(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(provider => provider.GetType().FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IImageMetadataProvider ValidateProvider(IImageMetadataProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(provider.ProviderId))
        {
            throw new InvalidOperationException(
                $"Image-Metadata-Provider '{provider.GetType().FullName}' hat keine Provider-ID.");
        }

        return provider;
    }
}
