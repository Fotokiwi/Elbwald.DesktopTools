using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Decoding;
using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.Core.Media.Decoding;

/// <summary>
/// Combines host-owned and active extension image decoders. The editor can therefore
/// gain format support without referencing concrete format modules.
/// </summary>
public sealed class ImageDecodeRegistry : IImageDecodeRegistry
{
    private readonly IExtensionRegistry _extensionRegistry;
    private readonly IImageDecodeProvider[] _builtInProviders;
    private IImageDecodeProvider[] _providers;

    public ImageDecodeRegistry(
        IExtensionRegistry extensionRegistry,
        IEnumerable<IImageDecodeProvider> builtInProviders)
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

    public IReadOnlyList<IImageDecodeProvider> Providers => _providers;

    public IReadOnlyList<IImageDecodeProvider> FindProviders(MediaFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.MediaType != MediaFileType.Image)
        {
            return [];
        }

        return _providers
            .Where(provider => provider.CanDecode(file))
            .ToArray();
    }

    private void OnActivationChanged(
        object? sender,
        ExtensionActivationChangedEventArgs args)
    {
        _providers = BuildSnapshot();
        ProvidersChanged?.Invoke(this, EventArgs.Empty);
    }

    private IImageDecodeProvider[] BuildSnapshot()
    {
        var extensionProviders = _extensionRegistry
            .GetProviders<IImageDecodeProvider>(DecodeCapabilityIds.ImageProvider);

        return _builtInProviders
            .Concat(extensionProviders)
            .Select(ValidateProvider)
            .OrderByDescending(provider => provider.Priority)
            .ThenBy(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(provider => provider.GetType().FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IImageDecodeProvider ValidateProvider(IImageDecodeProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(provider.ProviderId))
        {
            throw new InvalidOperationException(
                $"Image-Decode-Provider '{provider.GetType().FullName}' hat keine Provider-ID.");
        }

        return provider;
    }
}
