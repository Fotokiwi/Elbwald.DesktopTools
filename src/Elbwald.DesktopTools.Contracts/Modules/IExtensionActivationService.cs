namespace Elbwald.DesktopTools.Contracts.Modules;

/// <summary>
/// Persists the user-controlled runtime activation state of installed extensions.
/// Activation controls whether capabilities and tool contributions are offered by
/// the host; it is not a security or trust boundary and does not unload assemblies.
/// </summary>
public interface IExtensionActivationService
{
    event EventHandler<ExtensionActivationChangedEventArgs>? ActivationChanged;

    bool IsEnabled(string extensionId);

    bool SetEnabled(string extensionId, bool enabled);
}
