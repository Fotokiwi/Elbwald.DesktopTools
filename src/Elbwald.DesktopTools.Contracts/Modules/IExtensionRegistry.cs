namespace Elbwald.DesktopTools.Contracts.Modules;

/// <summary>
/// Central read-only view over loaded extensions and the capability providers
/// they contributed during startup.
/// </summary>
public interface IExtensionRegistry
{
    IReadOnlyCollection<ExtensionDescriptor> Extensions { get; }

    IReadOnlyCollection<ExtensionCapabilityProviderRegistration> ProviderRegistrations { get; }

    bool HasCapability(string capabilityId);

    IReadOnlyList<ExtensionDescriptor> FindExtensionsByCapability(string capabilityId);

    IReadOnlyList<TProvider> GetProviders<TProvider>(string capabilityId)
        where TProvider : class;
}
