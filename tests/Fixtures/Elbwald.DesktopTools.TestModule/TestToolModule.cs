using Elbwald.DesktopTools.Contracts.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.TestModule;

public sealed class TestToolModule : IToolModule
{
    public string Id => "elbwald.testmodule";

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(this);
        services.AddExtensionCapabilityProvider<ITestCapabilityProvider, TestCapabilityProvider>(
            Id,
            "test.service-registration");
    }

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

public interface ITestCapabilityProvider
{
    string Value { get; }
}

public sealed class TestCapabilityProvider : ITestCapabilityProvider
{
    public string Value => "ready";
}
