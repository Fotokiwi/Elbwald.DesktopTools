namespace Elbwald.DesktopTools.Contracts.Modules;

/// <summary>
/// Central read-only view over loaded extensions and the capability providers
/// they contributed during startup.
/// </summary>
public interface IExtensionRegistry
{
    /// <summary>
    /// Raised after the persisted activation state of a loaded extension changed.
    /// </summary>
    event EventHandler<ExtensionActivationChangedEventArgs>? ActivationChanged;

    IReadOnlyCollection<ExtensionDescriptor> Extensions { get; }

    IReadOnlyCollection<ExtensionCapabilityProviderRegistration> ProviderRegistrations { get; }

    bool IsEnabled(string extensionId);

    bool HasCapability(string capabilityId);

    IReadOnlyList<ExtensionDescriptor> FindExtensionsByCapability(string capabilityId);

    IReadOnlyList<TProvider> GetProviders<TProvider>(string capabilityId)
        where TProvider : class;
}
