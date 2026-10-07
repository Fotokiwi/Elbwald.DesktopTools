namespace Elbwald.DesktopTools.Contracts.Modules;

public interface IToolModule
{
    string Id { get; }

    ToolModuleDescriptor GetDescriptor();
}
