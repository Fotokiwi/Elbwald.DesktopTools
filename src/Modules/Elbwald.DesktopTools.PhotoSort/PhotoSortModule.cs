using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Contracts.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Elbwald.DesktopTools.PhotoSort.ViewModels;
using Elbwald.DesktopTools.PhotoSort.Views;

namespace Elbwald.DesktopTools.PhotoSort;

public sealed class PhotoSortModule : IToolModule
{
    public string Id => "elbwald.photosort";


    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNavigationContribution(
            new NavigationContribution(
                ExtensionId: Id,
                Id: "sort",
                RouteId: Id,
                Placement: NavigationContributionPlacement.Section,
                SectionId: "organize",
                Title: "Sortieren",
                Description: "Fotos und später Videos nach nachvollziehbaren Regeln strukturieren.",
                Icon: "Folder",
                Order: 20,
                ToolModuleId: Id));
    }

    public ToolModuleDescriptor GetDescriptor()
    {
        return new ToolModuleDescriptor(
            Id: Id,
            Name: "Sortieren",
            Description: "Fotos zuerst in einer Vorschau ordnen und anschließend sicher kopieren oder verschieben.",
            Icon: "Folder",
            ViewModelType: typeof(PhotoSortViewModel),
            ViewType: typeof(PhotoSortView));
    }
}
