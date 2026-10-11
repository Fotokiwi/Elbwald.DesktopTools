using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.Core.Modules;

/// <summary>
/// Immutable runtime registry built from validated startup registrations.
/// Multiple extensions may provide the same capability; ordering is stable so
/// specialized registries can apply their own deterministic selection rules.
/// </summary>
public sealed class ExtensionRegistry : IExtensionRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ExtensionDescriptor[] _extensions;
    private readonly ExtensionCapabilityProviderRegistration[] _providerRegistrations;

    public ExtensionRegistry(
        IServiceProvider serviceProvider,
        IEnumerable<ExtensionDescriptor> extensions,
        IEnumerable<ExtensionCapabilityProviderRegistration> providerRegistrations)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(providerRegistrations);

        _serviceProvider = serviceProvider;
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

    public IReadOnlyCollection<ExtensionDescriptor> Extensions => _extensions;

    public IReadOnlyCollection<ExtensionCapabilityProviderRegistration> ProviderRegistrations =>
        _providerRegistrations;

    public bool HasCapability(string capabilityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityId);

        return _extensions.Any(
            extension => extension.Capabilities.Any(
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
                extension => extension.Capabilities.Any(
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
                    string.Equals(
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
}
