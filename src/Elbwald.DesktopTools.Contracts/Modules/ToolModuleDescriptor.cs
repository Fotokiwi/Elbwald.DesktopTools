namespace Elbwald.DesktopTools.Contracts.Modules;

public sealed record ToolModuleDescriptor(
    string Id,
    string Name,
    string Description,
    string Icon,
    Type ViewModelType,
    Type ViewType);
