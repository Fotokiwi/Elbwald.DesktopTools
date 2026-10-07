using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Core.Modules;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Modules;

public sealed class ModuleRegistryTests
{
    [Fact]
    public void Register_AddsModule()
    {
        var registry = new ModuleRegistry();
        var module = new FakeModule("elbwald.test.one");

        registry.Register(module);

        var registered = Assert.Single(registry.Modules);
        Assert.Same(module, registered);
    }

    [Fact]
    public void Register_DuplicateId_Throws()
    {
        var registry = new ModuleRegistry();

        registry.Register(new FakeModule("elbwald.test.duplicate"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => registry.Register(new FakeModule("elbwald.test.duplicate")));

        Assert.Contains("already registered", exception.Message);
    }

    private sealed class FakeModule : IToolModule
    {
        public FakeModule(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public ToolModuleDescriptor GetDescriptor()
        {
            return new ToolModuleDescriptor(
                Id,
                "Fake Module",
                "Nur für Tests.",
                "Test",
                typeof(FakeModule),
                typeof(FakeModule));
        }
    }
}
