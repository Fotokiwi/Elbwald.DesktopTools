using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.PhotoSort.ViewModels;
using Elbwald.DesktopTools.PhotoSort.Views;

namespace Elbwald.DesktopTools.PhotoSort;

public sealed class PhotoSortModule : IToolModule
{
    public string Id => "elbwald.photosort";

    public ToolModuleDescriptor GetDescriptor()
    {
        return new ToolModuleDescriptor(
            Id: Id,
            Name: "Sortieren",
            Description: "Fotos per Dry Run analysieren und sicher nach Metadaten strukturieren.",
            Icon: "Folder",
            ViewModelType: typeof(PhotoSortViewModel),
            ViewType: typeof(PhotoSortView));
    }
}
