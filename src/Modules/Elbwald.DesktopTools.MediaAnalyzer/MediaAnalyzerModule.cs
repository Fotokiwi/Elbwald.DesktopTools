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
                Id: "analyze.media-analyzer",
                RouteId: Id,
                Placement: NavigationContributionPlacement.Section,
                SectionId: "analyze",
                Title: "Media Analyzer",
                Description: "Ordner und Medien read-only prüfen und auswerten.",
                Icon: "Search",
                Order: 100,
                ShowSeparatorBefore: false,
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
