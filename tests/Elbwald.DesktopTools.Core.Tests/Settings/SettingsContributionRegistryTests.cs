using Elbwald.DesktopTools.Contracts.Settings;
using Elbwald.DesktopTools.Core.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Core.Tests.Settings;

public sealed class SettingsContributionRegistryTests
{
    [Fact]
    public void Contributions_AreOrderedByOrderThenTitle()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var extensionRegistry = new ExtensionRegistry(provider, [], []);
        var registry = new SettingsContributionRegistry(
            [
                new SettingsContribution("ext.b", "b", "B", "B", "Settings", 20),
                new SettingsContribution("ext.a", "a", "A", "A", "Settings", 10)
            ],
            extensionRegistry);

        Assert.Collection(
            registry.Contributions,
            item => Assert.Equal("a", item.Id),
            item => Assert.Equal("b", item.Id));
    }
}
