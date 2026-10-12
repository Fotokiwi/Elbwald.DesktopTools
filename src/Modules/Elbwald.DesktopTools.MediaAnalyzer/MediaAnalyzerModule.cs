using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Contracts.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Elbwald.DesktopTools.MediaAnalyzer.ViewModels;
using Elbwald.DesktopTools.MediaAnalyzer.Views;

namespace Elbwald.DesktopTools.MediaAnalyzer;

public sealed class MediaAnalyzerModule
    : IToolModule
{
    public string Id =>
        "elbwald.mediaanalyzer";


    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNavigationContribution(
            new NavigationContribution(
                ExtensionId: Id,
                Id: "analyze",
                RouteId: Id,
                Placement: NavigationContributionPlacement.Primary,
                SectionId: null,
                Title: "Analysieren",
                Description: "Bibliothek read-only prüfen",
                Icon: "Search",
                Order: 300,
                ShowSeparatorBefore: true,
                ToolModuleId: Id));
    }

    public ToolModuleDescriptor GetDescriptor()
    {
        return new ToolModuleDescriptor(
            Id: Id,
            Name: "Analysieren",
            Description:
                "Ordner und Medien prüfen, bevor sie importiert oder bearbeitet werden.",
            Icon: "Search",
            ViewModelType:
                typeof(MediaAnalyzerViewModel),
            ViewType:
                typeof(MediaAnalyzerView));
    }
}
