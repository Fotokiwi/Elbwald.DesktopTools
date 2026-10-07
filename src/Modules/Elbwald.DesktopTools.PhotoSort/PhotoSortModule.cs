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
            Name: "Photo Sort",
            Description: "Fotos analysieren, organisieren und sicher sortieren.",
            Icon: "Image",
            ViewModelType: typeof(PhotoSortViewModel),
            ViewType: typeof(PhotoSortView));
    }
}
