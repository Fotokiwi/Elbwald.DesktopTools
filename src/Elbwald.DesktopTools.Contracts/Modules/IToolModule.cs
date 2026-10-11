namespace Elbwald.DesktopTools.Contracts.Modules;

/// <summary>
/// Extension that contributes a user-facing tool with its own view/view model.
/// Capability-only and service-only extensions implement <see cref="IExtensionModule"/>
/// directly and do not need to appear in navigation.
/// </summary>
public interface IToolModule : IExtensionModule
{
    ToolModuleDescriptor GetDescriptor();
}
