namespace Elbwald.DesktopTools.Contracts.Settings;

/// <summary>
/// A settings section contributed by an extension. View/ViewModel types are optional,
/// allowing capability-only packages to expose descriptive settings entries without
/// taking a UI dependency.
/// </summary>
public sealed record SettingsContribution(
    string ExtensionId,
    string Id,
    string Title,
    string Description,
    string Icon,
    int Order,
    Type? ViewModelType = null,
    Type? ViewType = null);
