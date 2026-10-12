using Elbwald.DesktopTools.Contracts.Navigation;
using Elbwald.DesktopTools.Core.Modules;
using Elbwald.DesktopTools.Core.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Core.Tests.Navigation;

public sealed class NavigationContributionRegistryTests
{
    [Fact]
    public void GetSectionContributions_FiltersAndOrdersContributions()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var extensionRegistry = new ExtensionRegistry(provider, [], []);
        var registry = new NavigationContributionRegistry(
            [
                new NavigationContribution(
                    "ext.b", "b", "route.b", NavigationContributionPlacement.Section,
                    "organize", "B", "B", "Folder", 20),
                new NavigationContribution(
                    "ext.a", "a", "route.a", NavigationContributionPlacement.Section,
                    "organize", "A", "A", "Folder", 10),
                new NavigationContribution(
                    "ext.primary", "analyze", "route.primary", NavigationContributionPlacement.Primary,
                    null, "Analyze", "Analyze", "Search", 300)
            ],
            extensionRegistry);

        var contributions = registry.GetSectionContributions("ORGANIZE");

        Assert.Collection(
            contributions,
            item => Assert.Equal("a", item.Id),
            item => Assert.Equal("b", item.Id));
    }

    [Fact]
    public void FindByRoute_IsCaseInsensitive()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var extensionRegistry = new ExtensionRegistry(provider, [], []);
        var registry = new NavigationContributionRegistry(
            [
                new NavigationContribution(
                    "ext.sort", "sort", "elbwald.photosort", NavigationContributionPlacement.Section,
                    "organize", "Sortieren", "Sortieren", "Folder", 20)
            ],
            extensionRegistry);

        var result = registry.FindByRoute("ELBWALD.PHOTOSORT");

        Assert.NotNull(result);
        Assert.Equal("organize", result.SectionId);
    }
}
