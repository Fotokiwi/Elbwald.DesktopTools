using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.Core.Modules;

/// <summary>
/// Runtime registry built from validated startup registrations. Multiple extensions
/// may provide the same capability. Loaded extensions remain discoverable even when
/// the user deactivates them, while capability/provider resolution only considers
/// currently active extensions.
/// </summary>
public sealed class ExtensionRegistry : IExtensionRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ExtensionDescriptor[] _extensions;
    private readonly ExtensionCapabilityProviderRegistration[] _providerRegistrations;
    private readonly IExtensionActivationService _activationService;

    public ExtensionRegistry(
        IServiceProvider serviceProvider,
        IEnumerable<ExtensionDescriptor> extensions,
        IEnumerable<ExtensionCapabilityProviderRegistration> providerRegistrations)
        : this(
            serviceProvider,
            extensions,
            providerRegistrations,
            AlwaysEnabledExtensionActivationService.Instance)
    {
    }

    public ExtensionRegistry(
        IServiceProvider serviceProvider,
        IEnumerable<ExtensionDescriptor> extensions,
        IEnumerable<ExtensionCapabilityProviderRegistration> providerRegistrations,
        IExtensionActivationService activationService)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(providerRegistrations);
        ArgumentNullException.ThrowIfNull(activationService);

        _serviceProvider = serviceProvider;
        _activationService = activationService;
        _extensions = extensions
            .OrderBy(extension => extension.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _providerRegistrations = providerRegistrations
            .OrderBy(registration => registration.CapabilityId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(registration => registration.ExtensionId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(registration => registration.ContractType.FullName, StringComparer.Ordinal)
            .ThenBy(registration => registration.ImplementationType.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    public event EventHandler<ExtensionActivationChangedEventArgs>? ActivationChanged
    {
        add => _activationService.ActivationChanged += value;
        remove => _activationService.ActivationChanged -= value;
    }

    /// <summary>
    /// All successfully loaded extensions, including user-deactivated ones.
    /// </summary>
    public IReadOnlyCollection<ExtensionDescriptor> Extensions => _extensions;

    public IReadOnlyCollection<ExtensionCapabilityProviderRegistration> ProviderRegistrations =>
        _providerRegistrations;

    public bool IsEnabled(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        return _activationService.IsEnabled(extensionId);
    }

    public bool HasCapability(string capabilityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityId);

        return _extensions.Any(
            extension =>
                IsEnabled(extension.Id)
                && extension.Capabilities.Any(
                    capability => string.Equals(
                        capability,
                        capabilityId,
                        StringComparison.OrdinalIgnoreCase)));
    }

    public IReadOnlyList<ExtensionDescriptor> FindExtensionsByCapability(string capabilityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityId);

        return _extensions
            .Where(
                extension =>
                    IsEnabled(extension.Id)
                    && extension.Capabilities.Any(
                        capability => string.Equals(
                            capability,
                            capabilityId,
                            StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    public IReadOnlyList<TProvider> GetProviders<TProvider>(string capabilityId)
        where TProvider : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityId);

        var requestedType = typeof(TProvider);
        var registrations = _providerRegistrations
            .Where(
                registration =>
                    IsEnabled(registration.ExtensionId)
                    && string.Equals(
                        registration.CapabilityId,
                        capabilityId,
                        StringComparison.OrdinalIgnoreCase)
                    && requestedType.IsAssignableFrom(registration.ContractType)
                    && requestedType.IsAssignableFrom(registration.ImplementationType))
            .ToArray();

        if (registrations.Length == 0)
        {
            return [];
        }

        var providers = new List<TProvider>(registrations.Length);

        foreach (var registration in registrations)
        {
            var provider = _serviceProvider.GetService(registration.ImplementationType);

            if (provider is not TProvider typedProvider)
            {
                throw new InvalidOperationException(
                    $"Capability-Provider '{registration.ImplementationType.FullName}' "
                    + $"für '{registration.CapabilityId}' konnte nicht als "
                    + $"'{requestedType.FullName}' aufgelöst werden.");
            }

            providers.Add(typedProvider);
        }

        return providers;
    }

    private sealed class AlwaysEnabledExtensionActivationService : IExtensionActivationService
    {
        public static AlwaysEnabledExtensionActivationService Instance { get; } = new();

        public event EventHandler<ExtensionActivationChangedEventArgs>? ActivationChanged
        {
            add { }
            remove { }
        }

        public bool IsEnabled(string extensionId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
            return true;
        }

        public bool SetEnabled(string extensionId, bool enabled)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
            return false;
        }
    }
}
