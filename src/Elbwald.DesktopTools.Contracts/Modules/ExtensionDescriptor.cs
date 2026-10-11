namespace Elbwald.DesktopTools.Contracts.Modules;

/// <summary>
/// Runtime description of a successfully loaded extension.
/// The descriptor is created from the validated module manifest and remains
/// available through <see cref="IExtensionRegistry"/> after startup.
/// </summary>
public sealed record ExtensionDescriptor(
    string Id,
    string Name,
    string Description,
    string Version,
    int ApiVersion,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Permissions);
