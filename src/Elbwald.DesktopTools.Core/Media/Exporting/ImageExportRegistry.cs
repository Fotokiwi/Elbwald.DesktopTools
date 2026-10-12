using Elbwald.DesktopTools.Contracts.Media.Exporting;
using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.Core.Media.Exporting;

/// <summary>
/// Combines host-owned and active extension image exporters. Extension activation
/// changes rebuild the snapshot immediately.
/// </summary>
public sealed class ImageExportRegistry : IImageExportRegistry
{
    private readonly IExtensionRegistry _extensionRegistry;
    private readonly IImageExportProvider[] _builtInProviders;
    private IImageExportProvider[] _providers;

    public ImageExportRegistry(
        IExtensionRegistry extensionRegistry,
        IEnumerable<IImageExportProvider> builtInProviders)
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

    public IReadOnlyList<IImageExportProvider> Providers => _providers;

    public IImageExportProvider? FindByFormatId(string formatId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatId);

        return _providers.FirstOrDefault(
            provider => string.Equals(
                provider.FormatId,
                formatId,
                StringComparison.OrdinalIgnoreCase));
    }

    private void OnActivationChanged(
        object? sender,
        ExtensionActivationChangedEventArgs args)
    {
        _providers = BuildSnapshot();
        ProvidersChanged?.Invoke(this, EventArgs.Empty);
    }

    private IImageExportProvider[] BuildSnapshot()
    {
        var extensionProviders = _extensionRegistry
            .GetProviders<IImageExportProvider>(ExportCapabilityIds.ImageExporterProvider);

        return _builtInProviders
            .Concat(extensionProviders)
            .Select(ValidateProvider)
            .OrderByDescending(provider => provider.Priority)
            .ThenBy(provider => provider.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase)
            .GroupBy(provider => provider.FormatId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static IImageExportProvider ValidateProvider(IImageExportProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(provider.ProviderId))
        {
            throw new InvalidOperationException(
                $"Image-Export-Provider '{provider.GetType().FullName}' hat keine Provider-ID.");
        }

        if (string.IsNullOrWhiteSpace(provider.FormatId))
        {
            throw new InvalidOperationException(
                $"Image-Export-Provider '{provider.ProviderId}' hat keine Format-ID.");
        }

        if (string.IsNullOrWhiteSpace(provider.DisplayName))
        {
            throw new InvalidOperationException(
                $"Image-Export-Provider '{provider.ProviderId}' hat keinen Anzeigenamen.");
        }

        if (string.IsNullOrWhiteSpace(provider.FileExtension)
            || !provider.FileExtension.StartsWith(".", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Image-Export-Provider '{provider.ProviderId}' hat keine gültige Dateiendung.");
        }

        if (string.IsNullOrWhiteSpace(provider.MimeType))
        {
            throw new InvalidOperationException(
                $"Image-Export-Provider '{provider.ProviderId}' hat keinen MIME-Typ.");
        }

        return provider;
    }
}
