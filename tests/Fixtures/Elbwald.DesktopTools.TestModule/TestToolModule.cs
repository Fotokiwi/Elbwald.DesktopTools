using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.TestModule;

public sealed class TestToolModule : IToolModule
{
    public string Id => "elbwald.testmodule";

    public ToolModuleDescriptor GetDescriptor()
    {
        return new ToolModuleDescriptor(
            Id: Id,
            Name: "Test Module",
            Description: "Testmodul für automatisierte ModuleLoader-Tests.",
            Icon: "Test",
            ViewModelType: typeof(TestToolModule),
            ViewType: typeof(TestToolModule));
    }
}
