using CommunityToolkit.Mvvm.ComponentModel;

namespace Elbwald.DesktopTools.PhotoSort.ViewModels;

public partial class PhotoSortViewModel : ObservableObject
{
    [ObservableProperty]
    private string title = "Photo Sort";
}
