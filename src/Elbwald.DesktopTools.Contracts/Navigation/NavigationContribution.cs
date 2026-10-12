namespace Elbwald.DesktopTools.Contracts.Navigation;

/// <summary>
/// Declarative navigation contribution supplied by an extension.
/// Contributions never own navigation state; the host decides how they are rendered.
/// </summary>
public sealed record NavigationContribution(
    string ExtensionId,
    string Id,
    string RouteId,
    NavigationContributionPlacement Placement,
    string? SectionId,
    string Title,
    string Description,
    string Icon,
    int Order,
    bool ShowSeparatorBefore = false,
    string? ToolModuleId = null);
