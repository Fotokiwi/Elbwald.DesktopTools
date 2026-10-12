using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Thumbnailing;
using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.Core.Media.Thumbnailing;

/// <summary>
/// Combines host-owned and active extension thumbnail providers. Extension
/// activation changes rebuild the snapshot immediately.
/// </summary>
public sealed class ImageThumbnailRegistry : IImageThumbnailRegistry
{
    private readonly IExtensionRegistry _extensionRegistry;
    private readonly IImageThumbnailProvider[] _builtInProviders;
    private IImageThumbnailProvider[] _providers;

    public ImageThumbnailRegistry(
        IExtensionRegistry extensionRegistry,
        IEnumerable<IImageThumbnailProvider> builtInProviders)
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

    public event EventHandler? ProvidersChanged;

    public IReadOnlyList<IImageThumbnailProvider> Providers => _providers;

    public IReadOnlyList<IImageThumbnailProvider> FindProviders(MediaFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.MediaType != MediaFileType.Image)
        {
            return [];
        }

        return _providers
            .Where(provider => provider.CanCreate(file))
            .ToArray();
    }

    private void OnActivationChanged(
        object? sender,
        ExtensionActivationChangedEventArgs args)
    {
        _providers = BuildSnapshot();
        ProvidersChanged?.Invoke(this, EventArgs.Empty);
    }

    private IImageThumbnailProvider[] BuildSnapshot()
    {
        var extensionProviders = _extensionRegistry
            .GetProviders<IImageThumbnailProvider>(ThumbnailCapabilityIds.ImageProvider);

        return _builtInProviders
            .Concat(extensionProviders)
            .Select(ValidateProvider)
            .OrderByDescending(provider => provider.Priority)
            .ThenBy(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(provider => provider.GetType().FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IImageThumbnailProvider ValidateProvider(IImageThumbnailProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(provider.ProviderId))
        {
            throw new InvalidOperationException(
                $"Image-Thumbnail-Provider '{provider.GetType().FullName}' hat keine Provider-ID.");
        }

        return provider;
    }
}
